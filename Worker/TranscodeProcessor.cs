using Microsoft.EntityFrameworkCore;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Worker;

/// <summary>
/// Runs one transcode job end to end: probe the source, then walk the ladder producing renditions.
/// The required rungs come first so the video flips to Ready as early as possible; the higher
/// rungs continue afterwards and appear in the master playlist as they land.
/// </summary>
public class TranscodeProcessor(
    ApplicationDbContext context,
    MediaStorage storage,
    VideoProbe probe,
    FfmpegTranscoder transcoder,
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

            var plan = TranscodeLadder.Plan(probed.Height);
            var requiredHeights = TranscodeLadder.Required(probed.Height).Select(r => r.Height).ToHashSet();

            logger.LogInformation(
                "Ladder for video {VideoId}: {Rungs}",
                video.Id, string.Join(", ", plan.Select(r => r.Name)));

            // A re-delivered job may already satisfy the required rungs, in which case the video is
            // playable right now and should not wait behind the remaining optional encodes.
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
                // Only reachable if the plan produced nothing usable at all.
                throw new TranscodeException("No renditions could be produced from this file.");
            }
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
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
