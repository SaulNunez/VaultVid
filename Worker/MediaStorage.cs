using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using VideoHostingService.Models;

namespace VideoHostingService.Worker;

/// <summary>Moves source files and HLS output between the worker's temp directory and MinIO.</summary>
public class MediaStorage(
    IMinioClient minio,
    IOptions<ObjectStorageConfiguration> storageOptions,
    ILogger<MediaStorage> logger)
{
    private readonly string bucket = storageOptions.Value.BucketName;

    /// <summary>Downloads the uploaded source to a local file. ffmpeg needs seekable input.</summary>
    public async Task DownloadAsync(string objectName, string destinationPath, CancellationToken cancellationToken)
    {
        var args = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithCallbackStream(async (stream, ct) =>
            {
                await using var file = File.Create(destinationPath);
                await stream.CopyToAsync(file, ct);
            });

        await minio.GetObjectAsync(args, cancellationToken);
        logger.LogDebug("Downloaded {ObjectName} to {Path}", objectName, destinationPath);
    }

    /// <summary>Uploads every file in a rendition directory under its HLS prefix.</summary>
    public async Task UploadRenditionAsync(
        string directory,
        Guid videoId,
        int height,
        CancellationToken cancellationToken)
    {
        var prefix = MediaKeys.RenditionDirectory(videoId, height);

        // Segments first: a player that fetches the playlist must never find it pointing at
        // segments that are not there yet.
        var files = Directory.GetFiles(directory)
            .OrderBy(path => path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            await PutFileAsync(prefix + name, path, ContentTypeFor(name), cancellationToken);
        }

        logger.LogInformation("Uploaded the {Height}p rendition of {VideoId}", height, videoId);
    }

    /// <summary>
    /// Writes the master playlist listing every rendition completed so far. Called again after
    /// each rung, so a viewer who started on 480p picks up 1080p when they next reload.
    /// </summary>
    public async Task WriteMasterPlaylistAsync(
        Guid videoId,
        IEnumerable<VideoRendition> renditions,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        builder.AppendLine("#EXT-X-VERSION:3");

        foreach (var rendition in renditions.OrderBy(r => r.BandwidthBitsPerSecond))
        {
            builder.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={rendition.BandwidthBitsPerSecond}");
            builder.Append(CultureInfo.InvariantCulture, $",RESOLUTION={rendition.Width}x{rendition.Height}");
            builder.AppendLine(",CODECS=\"avc1.4d401f,mp4a.40.2\"");

            // Relative on purpose: the media endpoint serves this playlist from
            // /media/{publicId}/master.m3u8, so "480p/index.m3u8" resolves without rewriting.
            builder.AppendLine($"{rendition.Height}p/{MediaKeys.MediaPlaylistName}");
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        using var stream = new MemoryStream(bytes);

        await minio.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(MediaKeys.MasterPlaylist(videoId))
                .WithStreamData(stream)
                .WithObjectSize(bytes.Length)
                .WithContentType(MediaKeys.PlaylistContentType),
            cancellationToken);
    }

    /// <summary>Uploads an extracted poster frame and returns its object key.</summary>
    public async Task<string> UploadThumbnailAsync(string path, CancellationToken cancellationToken)
    {
        var objectName = $"{MediaKeys.ThumbnailPrefix}/{Guid.NewGuid():N}.jpg";
        await PutFileAsync(objectName, path, "image/jpeg", cancellationToken);
        return objectName;
    }

    private async Task PutFileAsync(string objectName, string path, string contentType, CancellationToken cancellationToken)
    {
        await minio.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectName)
                .WithFileName(path)
                .WithContentType(contentType),
            cancellationToken);
    }

    private static string ContentTypeFor(string fileName)
        => fileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? MediaKeys.PlaylistContentType
            : MediaKeys.SegmentContentType;
}
