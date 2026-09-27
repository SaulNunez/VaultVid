namespace VideoHostingService.Models;

/// <summary>
/// Object-key layout for the media bucket. Both the web app and the transcoding worker build keys
/// from here so the two never disagree about where a video's files live.
/// </summary>
public static class MediaKeys
{
    public const string VideoPrefix = "videos";
    public const string ThumbnailPrefix = "thumbnails";
    public const string HlsPrefix = "hls";

    public const string MasterPlaylistName = "master.m3u8";
    public const string MediaPlaylistName = "index.m3u8";

    public const string PlaylistContentType = "application/vnd.apple.mpegurl";
    public const string SegmentContentType = "video/mp2t";

    /// <summary>Everything belonging to one video's HLS output, including the trailing slash.</summary>
    public static string HlsDirectory(Guid videoId) => $"{HlsPrefix}/{videoId:N}/";

    public static string MasterPlaylist(Guid videoId) => $"{HlsDirectory(videoId)}{MasterPlaylistName}";

    public static string RenditionDirectory(Guid videoId, int height) => $"{HlsDirectory(videoId)}{height}p/";

    public static string RenditionPlaylist(Guid videoId, int height)
        => $"{RenditionDirectory(videoId, height)}{MediaPlaylistName}";
}
