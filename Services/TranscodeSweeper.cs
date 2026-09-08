using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

/// <summary>
/// Re-publishes transcode jobs that never made it onto the queue, or whose worker died mid-job.
/// This is the cheap stand-in for a transactional outbox: <see cref="VideoService.CreateVideoAsync"/>
/// saves the row and then publishes, so a broker outage between the two leaves a Pending video
/// with no job. Consumers are idempotent, so re-publishing a job that is actually still running
/// only wastes work, it does not corrupt anything.
/// </summary>
public class TranscodeSweeper(
    IServiceScopeFactory scopeFactory,
    ITranscodeQueue queue,
    IOptions<TranscodeSweeperOptions> options,
    ILogger<TranscodeSweeper> logger) : BackgroundService
{
    private readonly TranscodeSweeperOptions settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.SweepIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Never let one bad sweep kill the loop; the next tick tries again.
                logger.LogError(e, "Transcode sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTimeOffset.UtcNow;
        var stuckBefore = now.AddMinutes(-settings.StuckAfterMinutes);

        // Pending is the normal state of a video queued behind a busy worker, so it only counts as
        // stranded once it is old enough that the job was probably never received. Without this the
        // sweeper duplicates jobs whenever the queue backs up.
        var pendingBefore = now.AddMinutes(-settings.PendingGraceMinutes);

        var stranded = await context.Videos
            .AsNoTracking()
            .Where(v => v.ObjectName != null
                && ((v.Status == VideoStatus.Pending && v.CreatedAt < pendingBefore)
                    || (v.Status == VideoStatus.Processing
                        && (v.ProcessingStartedAt == null || v.ProcessingStartedAt < stuckBefore))))
            .OrderBy(v => v.CreatedAt)
            .Take(settings.BatchSize)
            .Select(v => new TranscodeRequest(v.Id, v.PublicId, v.ObjectName!))
            .ToListAsync(cancellationToken);

        foreach (var request in stranded)
        {
            logger.LogInformation("Re-queueing stranded transcode job for video {VideoId}", request.VideoId);
            await queue.PublishAsync(request, cancellationToken);
        }
    }
}

public class TranscodeSweeperOptions
{
    public const string SectionName = "TranscodeSweeper";

    public int SweepIntervalSeconds { get; set; } = 120;

    /// <summary>How long a video may sit in Processing before its job is assumed lost.</summary>
    public int StuckAfterMinutes { get; set; } = 30;

    /// <summary>
    /// How long a video may sit in Pending before its job is assumed lost. Has to comfortably
    /// exceed the longest expected queue wait, or the sweeper re-queues jobs that are merely
    /// waiting their turn.
    /// </summary>
    public int PendingGraceMinutes { get; set; } = 10;

    public int BatchSize { get; set; } = 25;
}
