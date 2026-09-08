using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using VideoHostingService.Utilities;

namespace VideoHostingService.Services;

public interface IVideoService
{
    /// <summary>
    /// Uploads the supplied files to object storage and, only if that succeeds, saves the video
    /// row pointing at them. Returns the new video's <see cref="Video.PublicId"/>.
    /// </summary>
    Task<Guid> CreateVideoAsync(VideoUpload upload, string userId, IProgress<UploadProgress>? progress, CancellationToken cancellationToken);

    Task<Video?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Video>> GetRecentAsync(int size, CancellationToken cancellationToken);

    /// <summary>Returns false when the video does not exist or is not owned by <paramref name="userId"/>.</summary>
    Task<bool> DeleteAsync(Guid publicId, string userId, CancellationToken cancellationToken);

    Task<bool> EditAsync(Guid publicId, string userId, string title, string description, CancellationToken cancellationToken);
}

public class VideoService(
    ApplicationDbContext context,
    IMinioClient minioClient,
    IOptions<MaxUploadSizes> uploadSizes,
    IOptions<ObjectStorageConfiguration> storageOptions,
    ILogger<VideoService> logger) : IVideoService
{
    private const string VideoPrefix = "videos";
    private const string ThumbnailPrefix = "thumbnails";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly MaxUploadSizes sizes = uploadSizes.Value;
    private readonly string bucket = storageOptions.Value.BucketName;

    public async Task<Guid> CreateVideoAsync(VideoUpload upload, string userId, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (upload.VideoFile is null)
        {
            throw new InvalidFileException("No video file was supplied.");
        }

        // Upload first: a row that points at objects which failed to upload is worse than an
        // object with no row, and the objects are rolled back below if the save fails.
        var videoObject = await UploadAsync(
            upload.VideoFile,
            VideoPrefix,
            sizes.MaxVideoSize,
            AcceptedExtensions.PermittedVideoExtensions,
            VideoValidator.HeaderSize,
            (header, ext) => VideoValidator.IsValidVideoHeader(header, ext),
            progress,
            cancellationToken);

        string? thumbnailObject = null;
        try
        {
            if (upload.Thumbnail is not null)
            {
                thumbnailObject = await UploadAsync(
                    upload.Thumbnail,
                    ThumbnailPrefix,
                    sizes.MaxThumbnailSize,
                    AcceptedExtensions.PermittedImageExtensions,
                    ImageValidator.HeaderSize,
                    (header, ext) => ImageValidator.IsValidImageHeader(header, ext),
                    progress: null,
                    cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var video = new Video
            {
                Title = upload.Title,
                Description = upload.Description,
                UserId = userId,
                ObjectName = videoObject,
                ThumbnailLocation = thumbnailObject,
                CreatedAt = now,
                EditedAt = now,
            };

            context.Videos.Add(video);
            await context.SaveChangesAsync(cancellationToken);

            return video.PublicId;
        }
        catch
        {
            // Don't leave objects behind for a video that was never recorded.
            await TryRemoveObjectAsync(videoObject);
            await TryRemoveObjectAsync(thumbnailObject);
            throw;
        }
    }

    public Task<Video?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken)
        => context.Videos
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.PublicId == publicId, cancellationToken);

    public async Task<IReadOnlyList<Video>> GetRecentAsync(int size, CancellationToken cancellationToken)
        => await context.Videos
            .AsNoTracking()
            .OrderByDescending(v => v.CreatedAt)
            .Take(size)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid publicId, string userId, CancellationToken cancellationToken)
    {
        var video = await context.Videos.FirstOrDefaultAsync(v => v.PublicId == publicId, cancellationToken);
        if (video is null || !string.Equals(video.UserId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        context.Videos.Remove(video);
        await context.SaveChangesAsync(cancellationToken);

        await TryRemoveObjectAsync(video.ObjectName);
        await TryRemoveObjectAsync(video.ThumbnailLocation);

        return true;
    }

    public async Task<bool> EditAsync(Guid publicId, string userId, string title, string description, CancellationToken cancellationToken)
    {
        var video = await context.Videos.FirstOrDefaultAsync(v => v.PublicId == publicId, cancellationToken);
        if (video is null || !string.Equals(video.UserId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        video.Title = title;
        video.Description = description;
        video.EditedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string> UploadAsync(
        IBrowserFile file,
        string prefix,
        long maxSize,
        string[] permittedExtensions,
        int headerSize,
        Func<byte[], string, bool> isValidHeader,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!permittedExtensions.Contains(extension))
        {
            throw new InvalidFileException($"'{extension}' is not an accepted file type.");
        }

        if (file.Size > maxSize)
        {
            throw new InvalidFileException($"File is larger than the {maxSize / (1024 * 1024)} MB limit.");
        }

        // Ownership passes to the wrapper streams below, which dispose it.
        var source = file.OpenReadStream(maxSize, cancellationToken);

        // OpenReadStream is forward-only, so the header has to be buffered and replayed rather
        // than seeked back over.
        var header = new byte[headerSize];
        var headerLength = await source.ReadAtLeastAsync(header, headerSize, throwOnEndOfStream: false, cancellationToken);
        if (headerLength < headerSize)
        {
            Array.Resize(ref header, headerLength);
        }

        if (!isValidHeader(header, extension))
        {
            throw new InvalidFileException("The file contents don't match its extension.");
        }

        Stream body = new PrefixedStream(header, source);
        if (progress is not null)
        {
            body = new ProgressStream(body, file.Size, progress);
        }

        await using (body)
        {
            await EnsureBucketAsync(cancellationToken);

            var objectName = $"{prefix}/{Guid.NewGuid():N}{extension}";

            // The browser-supplied content type is not trusted; derive it from the extension that
            // the magic bytes above were checked against.
            if (!ContentTypes.TryGetContentType(objectName, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            var args = new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectName)
                .WithStreamData(body)
                .WithObjectSize(file.Size)
                .WithContentType(contentType);

            await minioClient.PutObjectAsync(args, cancellationToken);

            return objectName;
        }
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var exists = await minioClient
            .BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            await minioClient
                .MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task TryRemoveObjectAsync(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        try
        {
            // Deliberately not cancellable: this is cleanup and often runs while unwinding.
            await minioClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(bucket).WithObject(objectName),
                CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not remove orphaned object {ObjectName}", objectName);
        }
    }
}
