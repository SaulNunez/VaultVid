using System.Globalization;
using System.Text.Json;

namespace VideoHostingService.Worker;

/// <summary>Source dimensions as they should be displayed, plus duration and frame rate.</summary>
public record ProbeResult(int Width, int Height, double DurationSeconds, double FrameRate);

/// <summary>Reads a source file's dimensions and duration with ffprobe.</summary>
public class VideoProbe(ILogger<VideoProbe> logger)
{
    public async Task<ProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-print_format", "json",
                "-show_streams",
                "-show_format",
                path,
            ],
            workingDirectory: null,
            logger,
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new TranscodeException($"ffprobe could not read the file: {result.ErrorSummary}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        if (!root.TryGetProperty("streams", out var streams))
        {
            throw new TranscodeException("ffprobe reported no streams.");
        }

        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("codec_type", out var codecType)
                || codecType.GetString() != "video")
            {
                continue;
            }

            var width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
            var height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;

            if (width <= 0 || height <= 0)
            {
                continue;
            }

            // Phone video is usually stored landscape with a rotation in the display matrix.
            // Ladder decisions have to use the displayed orientation, or a portrait 1080x1920
            // clip would be treated as a 1080p-tall source and get too many rungs.
            if (IsQuarterTurn(GetRotation(stream)))
            {
                (width, height) = (height, width);
            }

            var duration = ReadDuration(stream, root);
            return new ProbeResult(width, height, duration, ReadFrameRate(stream));
        }

        throw new TranscodeException("The file has no usable video stream.");
    }

    private static bool IsQuarterTurn(double rotation)
    {
        var normalised = Math.Abs(rotation % 180);
        return Math.Abs(normalised - 90) < 1;
    }

    private static double GetRotation(JsonElement stream)
    {
        // ffprobe 5+ exposes rotation under side_data_list; older builds put it in tags.
        if (stream.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var entry in sideData.EnumerateArray())
            {
                if (entry.TryGetProperty("rotation", out var rotation)
                    && rotation.ValueKind == JsonValueKind.Number)
                {
                    return rotation.GetDouble();
                }
            }
        }

        if (stream.TryGetProperty("tags", out var tags)
            && tags.TryGetProperty("rotate", out var rotate)
            && double.TryParse(rotate.GetString(), out var tagged))
        {
            return tagged;
        }

        return 0;
    }

    /// <summary>
    /// Frame rate, needed to line the keyframe interval up with the segment length. ffprobe
    /// reports it as a rational such as "30000/1001"; 25 is a safe fallback.
    /// </summary>
    private static double ReadFrameRate(JsonElement stream)
    {
        const double Fallback = 25;

        if (!stream.TryGetProperty("r_frame_rate", out var rate) || rate.GetString() is not { } text)
        {
            return Fallback;
        }

        var parts = text.Split('/');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            || denominator == 0
            || numerator <= 0)
        {
            return Fallback;
        }

        var frameRate = numerator / denominator;
        return frameRate is > 0 and <= 240 ? frameRate : Fallback;
    }

    private static double ReadDuration(JsonElement stream, JsonElement root)
    {
        if (stream.TryGetProperty("duration", out var streamDuration)
            && double.TryParse(streamDuration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedStream))
        {
            return parsedStream;
        }

        // Matroska and friends often carry the duration only on the container.
        if (root.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var formatDuration)
            && double.TryParse(formatDuration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFormat))
        {
            return parsedFormat;
        }

        return 0;
    }
}

/// <summary>A transcode failed for a reason worth showing to the uploader.</summary>
public class TranscodeException : Exception
{
    public TranscodeException(string message) : base(message) { }

    public TranscodeException(string message, Exception innerException) : base(message, innerException) { }
}
