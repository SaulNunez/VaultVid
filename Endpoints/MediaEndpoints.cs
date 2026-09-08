using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Minio;
using Minio.DataModel.Args;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using VideoHostingService.Services;

namespace VideoHostingService.Endpoints;

/// <summary>
/// Serves a video's HLS output from object storage.
/// </summary>
/// <remarks>
/// Presigned URLs cannot be used here: an HLS master playlist points at media playlists which
/// point at segments by relative URI, and the player fetches those itself, so there is nowhere to
/// inject a signature. Proxying through the app instead means the playlists can stay verbatim -
/// "360p/index.m3u8" resolves under /media/{publicId}/ on its own - and it keeps a single place to
/// add per-video authorisation later. Thumbnails are unaffected and still use presigned URLs.
/// </remarks>
public static class MediaEndpoints
{
    private const string CacheKeyPrefix = "media:videoid:";
    private static readonly TimeSpan LookupCacheDuration = TimeSpan.FromMinutes(10);

    public static void MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/media/{publicId:guid}/{**path}", HandleAsync)
            .AllowAnonymous()
            .WithName("Media");
    }

    private static async Task<IResult> HandleAsync(
        Guid publicId,
        string? path,
        HttpContext http,
        ApplicationDbContext context,
        RedisCacheService cache,
        IMinioClient minio,
        IOptions<ObjectStorageConfiguration> storageOptions,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!TryGetContentType(path, out var contentType, out var immutable))
        {
            return Results.NotFound();
        }

        var videoId = await ResolveVideoIdAsync(publicId, context, cache, logger, cancellationToken);
        if (videoId is null)
        {
            return Results.NotFound();
        }

        var bucket = storageOptions.Value.BucketName;
        var objectName = MediaKeys.HlsDirectory(videoId.Value) + path;

        Minio.DataModel.ObjectStat stat;
        try
        {
            stat = await minio.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(objectName),
                cancellationToken);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not stat media object {ObjectName}", objectName);
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        var (offset, length, isPartial) = ResolveRange(http.Request, stat.Size);
        if (offset is null && isPartial)
        {
            http.Response.Headers.ContentRange = $"bytes */{stat.Size}";
            return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        var response = http.Response;
        response.ContentType = contentType;
        response.Headers.AcceptRanges = "bytes";
        response.Headers.CacheControl = immutable
            // Segments are content-addressed by their position in an immutable VOD playlist.
            ? "public, max-age=31536000, immutable"
            // The master playlist is rewritten each time a higher rendition finishes.
            : "public, max-age=10";

        if (isPartial)
        {
            response.StatusCode = StatusCodes.Status206PartialContent;
            response.ContentLength = length;
            response.Headers.ContentRange = $"bytes {offset}-{offset + length - 1}/{stat.Size}";
        }
        else
        {
            response.ContentLength = stat.Size;
        }

        var getArgs = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithCallbackStream((stream, ct) => stream.CopyToAsync(response.Body, ct));

        if (isPartial)
        {
            getArgs = getArgs.WithOffsetAndLength(offset!.Value, length);
        }

        try
        {
            await minio.GetObjectAsync(getArgs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The player seeked away or the tab closed; the response is already partly written.
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not stream media object {ObjectName}", objectName);
        }

        return Results.Empty;
    }

    /// <summary>
    /// Only HLS artefacts are reachable through this route, which also keeps the source upload -
    /// stored under a different prefix - out of reach.
    /// </summary>
    private static bool TryGetContentType(string? path, out string contentType, out bool immutable)
    {
        contentType = string.Empty;
        immutable = false;

        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith('/')
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            contentType = MediaKeys.PlaylistContentType;
            return true;
        }

        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            contentType = MediaKeys.SegmentContentType;
            immutable = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// PublicId to Id, cached because every segment request repeats this lookup and the mapping
    /// never changes for a given video.
    /// </summary>
    private static async Task<Guid?> ResolveVideoIdAsync(
        Guid publicId,
        ApplicationDbContext context,
        RedisCacheService cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var key = CacheKeyPrefix + publicId.ToString("N");

        // The cache is an optimisation, not a dependency: if Redis is down, fall through to the
        // database rather than failing playback.
        try
        {
            if (cache.GetCacheData<Guid?>(key) is { } cached)
            {
                return cached;
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Media id cache read failed; falling back to the database");
        }

        var videoId = await context.Videos
            .AsNoTracking()
            .Where(v => v.PublicId == publicId)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (videoId is not null)
        {
            try
            {
                cache.SetCacheData(key, videoId, LookupCacheDuration);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Media id cache write failed");
            }
        }

        return videoId;
    }

    private static (long? Offset, long Length, bool IsPartial) ResolveRange(HttpRequest request, long size)
    {
        var header = request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return (null, size, false);
        }

        if (!RangeHeaderValue.TryParse(header, out var parsed) || parsed.Ranges.Count != 1)
        {
            // Multi-range requests are legal but not worth supporting for media segments.
            return (null, size, false);
        }

        var range = parsed.Ranges.First();
        long offset;
        long length;

        if (range.From is not null)
        {
            offset = range.From.Value;
            if (offset >= size)
            {
                return (null, 0, true);
            }

            length = (range.To ?? size - 1) - offset + 1;
        }
        else if (range.To is not null)
        {
            // A suffix range: the last N bytes.
            length = Math.Min(range.To.Value, size);
            offset = size - length;
        }
        else
        {
            return (null, size, false);
        }

        length = Math.Min(length, size - offset);
        return length <= 0 ? (null, 0, true) : (offset, length, true);
    }
}
