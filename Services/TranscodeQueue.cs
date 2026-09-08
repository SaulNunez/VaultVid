using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using VideoHostingService.Models;

namespace VideoHostingService.Services;

public interface ITranscodeQueue
{
    /// <summary>
    /// Publishes a transcode job. Throws when the broker is unreachable; callers decide whether
    /// that is fatal - <see cref="VideoService.CreateVideoAsync"/> deliberately treats it as
    /// recoverable and leaves the video Pending for <see cref="TranscodeSweeper"/> to re-publish.
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
            "No message broker is configured; video {VideoId} will stay in the Pending state.",
            request.VideoId);
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
            routingKey: configuration.QueueName,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogInformation("Enqueued transcode job for video {VideoId}", request.VideoId);
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

            // Declared by both ends so neither has to start first.
            await channel.QueueDeclareAsync(
                queue: configuration.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

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
