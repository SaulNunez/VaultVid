using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Worker;

/// <summary>
/// Consumes transcode jobs and hands each to a <see cref="TranscodeProcessor"/> in its own scope.
/// Prefetch is one: transcoding saturates the CPU, so pulling a second job would only slow the
/// first one down. Run more replicas to go wider.
/// </summary>
public class TranscodeConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqConfiguration> rabbitOptions,
    IOptions<TranscodeWorkerOptions> workerOptions,
    ILogger<TranscodeConsumer> logger) : BackgroundService
{
    private readonly RabbitMqConfiguration rabbit = rabbitOptions.Value;
    private readonly TranscodeWorkerOptions settings = workerOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // The broker may still be starting, or the connection dropped mid-job. Back off
                // and redial rather than letting the worker exit.
                logger.LogError(e, "Lost the connection to the broker; retrying in {Seconds}s", settings.ReconnectDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(settings.ReconnectDelaySeconds), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = rabbit.Host,
            Port = rabbit.Port,
            UserName = rabbit.User,
            Password = rabbit.Password,
            VirtualHost = rabbit.VirtualHost,
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declared by both ends so neither the app nor the worker has to start first.
        await channel.QueueDeclareAsync(
            queue: rabbit.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) => await HandleDeliveryAsync(channel, args, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: rabbit.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Listening for transcode jobs on {Queue}", rabbit.QueueName);

        // Hold the connection open; deliveries arrive on the consumer above.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        TranscodeRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<TranscodeRequest>(args.Body.Span);
        }
        catch (JsonException e)
        {
            logger.LogError(e, "Discarding an unreadable message");
        }

        if (request is null)
        {
            // Requeuing a message we cannot parse would spin forever.
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        try
        {
            await ProcessWithRetriesAsync(request, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-job: hand the message back so another replica picks it up.
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            return;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Giving up on video {VideoId}", request.VideoId);
            await MarkFailedAsync(request, e, stoppingToken);
        }

        // Acked either way: a file that cannot be transcoded will not transcode on redelivery
        // either, and the row now records why. The sweeper only re-queues Pending/stale rows.
        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
    }

    private async Task ProcessWithRetriesAsync(TranscodeRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<TranscodeProcessor>();
                await processor.ProcessAsync(request, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (attempt < settings.MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(settings.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
                logger.LogWarning(
                    e, "Attempt {Attempt} for video {VideoId} failed; retrying in {Delay}",
                    attempt, request.VideoId, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task MarkFailedAsync(TranscodeRequest request, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var video = await context.Videos.FirstOrDefaultAsync(v => v.Id == request.VideoId, cancellationToken);
            if (video is null)
            {
                return;
            }

            // A video that already reached Ready keeps playing; only the missing higher rungs are
            // lost, and that is not worth showing the uploader an error over.
            if (video.Status != VideoStatus.Ready)
            {
                video.Status = VideoStatus.Failed;
            }

            video.ProcessingError = exception is TranscodeException
                ? exception.Message
                : "This video could not be processed.";

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not record the failure for video {VideoId}", request.VideoId);
        }
    }
}

public class TranscodeWorkerOptions
{
    public const string SectionName = "TranscodeWorker";

    public int MaxAttempts { get; set; } = 3;

    public int RetryBaseDelaySeconds { get; set; } = 5;

    public int ReconnectDelaySeconds { get; set; } = 10;
}
