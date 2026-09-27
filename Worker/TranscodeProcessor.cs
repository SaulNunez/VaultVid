using Microsoft.EntityFrameworkCore;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using VideoHostingService.Services;

namespace VideoHostingService.Worker;

/// <summary>
/// Runs one transcode job: probe the source, then produce the rungs belonging to the job's stage.
/// </summary>
/// <remarks>
/// A Required job produces just enough to make the video playable, flips it to Ready, and then
/// enqueues an Optional job for the rest. Because those travel on separate queues, the long
/// high-resolution encodes can never delay the next upload becoming watchable.
/// </remarks>
public class TranscodeProcessor(
    ApplicationDbContext context,
    MediaStorage storage,
    VideoProbe probe,
    FfmpegTranscoder transcoder,
    ITranscodeQueue queue,
    ILogger<TranscodeProcessor> logger)
{
    public async Task ProcessAsync(TranscodeRequest request, CancellationToken cancellationToken)
    {
        var video = await context.Videos
            .Include(v => v.Renditions)
            .FirstOrDefaultAsync(v => v.Id == request.VideoId, cancellationToken);

        if (video is null)
        {
            logger.LogWarning("Video {VideoId} no longer exists; dropping the job", request.VideoId);
            return;
        }

        if (string.IsNullOrWhiteSpace(video.ObjectName))
        {
            throw new TranscodeException("The video has no source object.");
        }

        // Delivery is at-least-once and the sweeper can re-queue, so this method has to be safe to
        // re-run. A video that already reached Ready stays Ready while the remaining optional rungs
        // are produced - dropping it back to Processing would stop a video that is already playing.
        if (video.Status != VideoStatus.Ready)
        {
            video.Status = VideoStatus.Processing;
        }

        video.ProcessingStartedAt = DateTimeOffset.UtcNow;
        video.ProcessingError = null;
        await context.SaveChangesAsync(cancellationToken);

        // A per-job scratch directory, removed in the finally below however this ends.
        var workingDirectory = Directory.CreateTempSubdirectory($"vaultvid-{video.Id:N}-").FullName;

        try
        {
            var sourcePath = Path.Combine(workingDirectory, "source" + Path.GetExtension(video.ObjectName));
            await storage.DownloadAsync(video.ObjectName, sourcePath, cancellationToken);

            var probed = await probe.ProbeAsync(sourcePath, cancellationToken);
            video.SourceWidth = probed.Width;
            video.SourceHeight = probed.Height;
            video.DurationSeconds = probed.DurationSeconds;
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Video {VideoId} is {Width}x{Height}, {Duration:F1}s",
                video.Id, probed.Width, probed.Height, probed.DurationSeconds);

            await EnsureThumbnailAsync(video, sourcePath, workingDirectory, probed, cancellationToken);

            var requiredHeights = TranscodeLadder.Required(probed.Height).Select(r => r.Height).ToHashSet();

            // Each stage encodes only its own half of the ladder.
            var plan = request.Stage == TranscodeStage.Required
                ? TranscodeLadder.Required(probed.Height)
                : TranscodeLadder.Optional(probed.Height);

            logger.LogInformation(
                "{Stage} ladder for video {VideoId}: {Rungs}",
                request.Stage, video.Id,
                plan.Count == 0 ? "(nothing to do)" : string.Join(", ", plan.Select(r => r.Name)));

            // A re-delivered job may already satisfy the required rungs, in which case the video is
            // playable right now and should not wait behind the remaining encodes.
            await PromoteIfPlayableAsync(video, requiredHeights, cancellationToken);

            foreach (var rung in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A re-delivered or swept job must not redo work that already succeeded.
                if (video.Renditions.Any(r => r.Height == rung.Height))
                {
                    logger.LogDebug("Skipping {Rung} of {VideoId}; it already exists", rung.Name, video.Id);
                    continue;
                }

                await ProduceRenditionAsync(video, sourcePath, workingDirectory, rung, probed, cancellationToken);
                await PromoteIfPlayableAsync(video, requiredHeights, cancellationToken);
            }

            // Also evaluated outside the loop, because a re-delivered job skips every rung it has
            // already produced. Judging readiness only after doing work would leave a finished
            // video stuck below Ready and then fail it on the check underneath.
            await PromoteIfPlayableAsync(video, requiredHeights, cancellationToken);

            if (video.Status != VideoStatus.Ready)
            {
                // Only reachable if the required stage produced nothing usable at all.
                throw new TranscodeException("No renditions could be produced from this file.");
            }

            if (request.Stage == TranscodeStage.Required)
            {
                await EnqueueOptionalStageAsync(video, probed.Height, request, cancellationToken);
            }
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    /// <summary>
    /// Hands the remaining rungs to the optional queue, now that the video is playable. A failure
    /// here is not fatal: the video already plays, and the sweeper re-queues an unfinished ladder.
    /// </summary>
    private async Task EnqueueOptionalStageAsync(
        Video video,
        int sourceHeight,
        TranscodeRequest request,
        CancellationToken cancellationToken)
    {
        var outstanding = TranscodeLadder.Optional(sourceHeight)
            .Where(rung => video.Renditions.All(r => r.Height != rung.Height))
            .ToList();

        if (outstanding.Count == 0)
        {
            return;
        }

        try
        {
            await queue.PublishAsync(
                request with { Stage = TranscodeStage.Optional },
                cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(
                e, "Could not enqueue the optional rungs of video {VideoId}; the sweeper will retry", video.Id);
        }
    }

    /// <summary>
    /// Marks the video playable once every required rung exists, whether those renditions were
    /// produced by this run or an earlier one.
    /// </summary>
    private async Task PromoteIfPlayableAsync(
        Video video,
        HashSet<int> requiredHeights,
        CancellationToken cancellationToken)
    {
        if (video.Status == VideoStatus.Ready
            || !requiredHeights.IsSubsetOf(video.Renditions.Select(r => r.Height)))
        {
            return;
        }

        video.Status = VideoStatus.Ready;
        video.MasterPlaylistObjectName = MediaKeys.MasterPlaylist(video.Id);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Video {VideoId} is now playable", video.Id);
    }

    private async Task ProduceRenditionAsync(
        Video video,
        string sourcePath,
        string workingDirectory,
        LadderRung rung,
        ProbeResult probed,
        CancellationToken cancellationToken)
    {
        var renditionDirectory = Path.Combine(workingDirectory, rung.Name);

        await transcoder.TranscodeRenditionAsync(sourcePath, renditionDirectory, rung, probed.FrameRate, cancellationToken);
        await storage.UploadRenditionAsync(renditionDirectory, video.Id, rung.Height, cancellationToken);

        var rendition = new VideoRendition
        {
            VideoId = video.Id,
            Width = TranscodeLadder.WidthFor(rung, probed.Width, probed.Height),
            Height = rung.Height,
            BandwidthBitsPerSecond = rung.BandwidthBitsPerSecond,
            PlaylistObjectName = MediaKeys.RenditionPlaylist(video.Id, rung.Height),
            CompletedAt = DateTimeOffset.UtcNow,
        };

        video.Renditions.Add(rendition);
        await context.SaveChangesAsync(cancellationToken);

        // Rewritten after every rung so the new quality becomes selectable immediately.
        await storage.WriteMasterPlaylistAsync(video.Id, video.Renditions, cancellationToken);

        // The local copy is uploaded; don't carry every rendition's segments for the whole job.
        TryDeleteDirectory(renditionDirectory);
    }

    private async Task EnsureThumbnailAsync(
        Video video,
        string sourcePath,
        string workingDirectory,
        ProbeResult probed,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(video.ThumbnailLocation))
        {
            return;
        }

        var posterPath = await transcoder.TryExtractPosterAsync(
            sourcePath, workingDirectory, probed.DurationSeconds, cancellationToken);

        if (posterPath is null)
        {
            return;
        }

        video.ThumbnailLocation = await storage.UploadThumbnailAsync(posterPath, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not clean up {Path}", path);
        }
    }
}
