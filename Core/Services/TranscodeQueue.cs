using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using VideoHostingService.Models;

namespace VideoHostingService.Services;

public interface ITranscodeQueue
{
    /// <summary>
    /// Publishes a transcode job onto the queue for its <see cref="TranscodeRequest.Stage"/>.
    /// Throws when the broker is unreachable; callers decide whether that is fatal - video upload
    /// deliberately treats it as recoverable and leaves the video Pending for the sweeper.
    /// </summary>
    Task PublishAsync(TranscodeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Used when there is no RabbitMq configuration section, so that `dotnet ef` and local runs
/// without a broker still start. Mirrors how MinIO registration is guarded in Program.cs.
/// </summary>
public class NullTranscodeQueue(ILogger<NullTranscodeQueue> logger) : ITranscodeQueue
{
    public Task PublishAsync(TranscodeRequest request, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "No message broker is configured; the {Stage} transcode of video {VideoId} will not run.",
            request.Stage, request.VideoId);
        return Task.CompletedTask;
    }
}

public sealed class RabbitMqTranscodeQueue : ITranscodeQueue, IAsyncDisposable
{
    private readonly RabbitMqConfiguration configuration;
    private readonly ILogger<RabbitMqTranscodeQueue> logger;
    private readonly ConnectionFactory factory;

    // The connection is opened on first publish rather than at startup so a broker that is not up
    // yet cannot take the web app down with it.
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public RabbitMqTranscodeQueue(IOptions<RabbitMqConfiguration> options, ILogger<RabbitMqTranscodeQueue> logger)
    {
        configuration = options.Value;
        this.logger = logger;

        factory = new ConnectionFactory
        {
            HostName = configuration.Host,
            Port = configuration.Port,
            UserName = configuration.User,
            Password = configuration.Password,
            VirtualHost = configuration.VirtualHost,
        };
    }

    public async Task PublishAsync(TranscodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await GetChannelAsync(cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(request);
        var properties = new BasicProperties
        {
            // Survive a broker restart: the source object is already stored, so losing the job
            // would strand the video in Pending until the sweeper notices.
            Persistent = true,
            ContentType = "application/json",
        };

        await target.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: configuration.QueueFor(request.Stage),
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Enqueued the {Stage} transcode of video {VideoId}", request.Stage, request.VideoId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true })
        {
            return channel;
        }

        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (channel is { IsOpen: true })
            {
                return channel;
            }

            // A dropped connection leaves a closed channel behind; throw both away and redial.
            await DisposeChannelAsync();

            connection = await factory.CreateConnectionAsync(cancellationToken);
            channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Both declared by both ends, so neither the app nor the worker has to start first
            // and neither stage's queue is missing when the other side publishes to it.
            foreach (var queue in (string[])[configuration.RequiredQueueName, configuration.OptionalQueueName])
            {
                await channel.QueueDeclareAsync(
                    queue: queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);
            }

            return channel;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task DisposeChannelAsync()
    {
        try
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Ignoring error while discarding a broken broker connection");
        }
        finally
        {
            channel = null;
            connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeChannelAsync();
        connectionLock.Dispose();
    }
}
