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

        var candidates = await context.Videos
            .AsNoTracking()
            .Where(v => v.ObjectName != null
                && ((v.Status == VideoStatus.Pending && v.CreatedAt < pendingBefore)
                    || (v.Status == VideoStatus.Processing
                        && (v.ProcessingStartedAt == null || v.ProcessingStartedAt < stuckBefore))
                    // A Ready video whose ladder is still incomplete is only stranded if nothing
                    // has touched it for a while; the optional rungs are normally still running.
                    || (v.Status == VideoStatus.Ready
                        && v.ProcessingStartedAt != null
                        && v.ProcessingStartedAt < stuckBefore)))
            .OrderBy(v => v.CreatedAt)
            .Take(settings.BatchSize)
            .Select(v => new
            {
                v.Id,
                v.PublicId,
                v.ObjectName,
                v.Status,
                v.SourceHeight,
                Heights = v.Renditions.Select(r => r.Height).ToList(),
            })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            // A Ready video only needs the optional lane, and only if rungs are actually missing.
            // Deciding that needs the ladder, which is not translatable to SQL.
            if (candidate.Status == VideoStatus.Ready)
            {
                if (candidate.SourceHeight is not { } height)
                {
                    continue;
                }

                var missing = TranscodeLadder.Optional(height)
                    .Any(rung => !candidate.Heights.Contains(rung.Height));

                if (!missing)
                {
                    continue;
                }

                await PublishAsync(candidate.Id, candidate.PublicId, candidate.ObjectName!, TranscodeStage.Optional, cancellationToken);
                continue;
            }

            await PublishAsync(candidate.Id, candidate.PublicId, candidate.ObjectName!, TranscodeStage.Required, cancellationToken);
        }
    }

    private async Task PublishAsync(
        Guid videoId,
        Guid publicId,
        string objectName,
        TranscodeStage stage,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Re-queueing the stranded {Stage} transcode of video {VideoId}", stage, videoId);

        await queue.PublishAsync(
            new TranscodeRequest(videoId, publicId, objectName, stage),
            cancellationToken);
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
