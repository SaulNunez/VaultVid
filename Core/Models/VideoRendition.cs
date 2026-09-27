namespace VideoHostingService.Models;

/// <summary>
/// One completed rung of a video's transcode ladder. Rows are inserted by the transcoding worker
/// as each rendition finishes, so the set grows while the video is already playable.
/// </summary>
public class VideoRendition
{
    public int Id { get; set; }

    public Guid VideoId { get; set; }

    public Video? Video { get; set; }

    public int Width { get; set; }

    /// <summary>Rung height in pixels: 360, 480, 720, 1080, 1440 or 2160.</summary>
    public int Height { get; set; }

    /// <summary>Advertised bandwidth for the master playlist's EXT-X-STREAM-INF tag.</summary>
    public int BandwidthBitsPerSecond { get; set; }

    /// <summary>Key of this rendition's media playlist, <c>hls/{videoId:N}/{height}p/index.m3u8</c>.</summary>
    public required string PlaylistObjectName { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}
