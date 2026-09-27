using System.Globalization;
using VideoHostingService.Models;

namespace VideoHostingService.Worker;

/// <summary>Produces one HLS rendition, or a poster frame, from a source file using ffmpeg.</summary>
public class FfmpegTranscoder(ILogger<FfmpegTranscoder> logger)
{
    /// <summary>Target segment length. Also drives the keyframe interval below.</summary>
    private const int SegmentSeconds = 6;

    /// <summary>
    /// Encodes <paramref name="sourcePath"/> into an HLS rendition inside
    /// <paramref name="outputDirectory"/>, producing index.m3u8 plus seg_NNNNN.ts files.
    /// </summary>
    public async Task TranscodeRenditionAsync(
        string sourcePath,
        string outputDirectory,
        LadderRung rung,
        double frameRate,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        // Segments can only be cut on a keyframe, so pin the GOP to the segment length and stop
        // the encoder from inserting extra keyframes on scene changes. Without this, segment
        // durations drift and switching between renditions stutters.
        var gop = Math.Max(1, (int)Math.Round(frameRate * SegmentSeconds));

        var arguments = new List<string>
        {
            "-y",
            "-i", sourcePath,
            // -2 keeps the aspect ratio and rounds the width to an even number, which libx264
            // requires for yuv420p output.
            "-vf", $"scale=-2:{rung.Height}",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-profile:v", "main",
            "-crf", "21",
            "-maxrate", $"{rung.VideoBitrateKbps}k",
            "-bufsize", $"{rung.VideoBitrateKbps * 2}k",
            "-pix_fmt", "yuv420p",
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            "-c:a", "aac",
            "-b:a", $"{TranscodeLadder.AudioBitrateKbps}k",
            "-ac", "2",
            "-hls_time", SegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-hls_playlist_type", "vod",
            "-hls_flags", "independent_segments",
            "-hls_segment_filename", Path.Combine(outputDirectory, "seg_%05d.ts"),
            Path.Combine(outputDirectory, MediaKeys.MediaPlaylistName),
        };

        var result = await ProcessRunner.RunAsync("ffmpeg", arguments, outputDirectory, logger, cancellationToken);

        if (!result.Succeeded)
        {
            throw new TranscodeException($"ffmpeg failed encoding {rung.Name}: {result.ErrorSummary}");
        }

        var playlist = Path.Combine(outputDirectory, MediaKeys.MediaPlaylistName);
        if (!File.Exists(playlist))
        {
            throw new TranscodeException($"ffmpeg reported success for {rung.Name} but wrote no playlist.");
        }
    }

    /// <summary>
    /// Grabs a still to use as a thumbnail when the uploader did not supply one. Taken a little
    /// way in, because the first frame of a video is very often black.
    /// </summary>
    public async Task<string?> TryExtractPosterAsync(
        string sourcePath,
        string outputDirectory,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var seek = durationSeconds > 0 ? durationSeconds * 0.1 : 0;
        var posterPath = Path.Combine(outputDirectory, "poster.jpg");

        var result = await ProcessRunner.RunAsync(
            "ffmpeg",
            [
                "-y",
                // Before -i so ffmpeg seeks rather than decoding up to the timestamp.
                "-ss", seek.ToString("F3", CultureInfo.InvariantCulture),
                "-i", sourcePath,
                "-frames:v", "1",
                "-q:v", "3",
                posterPath,
            ],
            outputDirectory,
            logger,
            cancellationToken);

        if (!result.Succeeded || !File.Exists(posterPath))
        {
            // A missing thumbnail is cosmetic; never fail a transcode over it.
            logger.LogWarning("Could not extract a poster frame: {Error}", result.ErrorSummary);
            return null;
        }

        return posterPath;
    }
}
