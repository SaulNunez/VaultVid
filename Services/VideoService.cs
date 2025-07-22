using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using VideoHostingService.Components.Pages;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using Video = VideoHostingService.Models.Video;

namespace VideoHostingService.Services;

public interface IVideoService
{
    Task AddVideo(VideoUpload video, CancellationToken token);
    Video GetVideoById(Guid id);
    Task DeleteVideo(Guid id);
    Task<Video> EditVideo(Guid id, Video video);
    Task<List<Video>> GetVideos(int size);
}

public class VideoService(ApplicationDbContext context, IMinioClient minioClient, IConfiguration configuration) : IVideoService
{
    public static readonly string videoBucketName = "videos";
    public static readonly string thumbnailBucketName = "thumbnails";

    public async Task AddVideo(VideoUpload video, CancellationToken token)
    {
        if (video == null)
        {
            throw new ArgumentNullException(nameof(video), "Video information can't be null");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var videoDb = new Video
        {
            Title = video.Title,
            Description = video.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Videos.Add(videoDb);
        await context.SaveChangesAsync(token);

        var sizes = configuration.GetSection("MaxUploadSizes").Get<MaxUploadSizes>();

        try
        {
            var videoExtension = Path.GetExtension(video.VideoFile.Name);
            var videoObjectName = $"{videoDb.Id}/original.{videoExtension}";

            // Make a bucket on the server, if not already present.
            var beArgs = new BucketExistsArgs()
                .WithBucket(videoBucketName);
            bool found = await minioClient.BucketExistsAsync(beArgs, token).ConfigureAwait(false);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs()
                    .WithBucket(videoBucketName);
                await minioClient.MakeBucketAsync(mbArgs, token).ConfigureAwait(false);
            }
            var videoStream = video.VideoFile.OpenReadStream(sizes.MaxVideoSize, cancellationToken: token);
            // Upload a file to bucket.
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(videoBucketName)
                .WithObject(videoObjectName)
                .WithStreamData(videoStream)
                .WithContentType(video.VideoFile.ContentType);
            var objectResponse = await minioClient.PutObjectAsync(putObjectArgs, token).ConfigureAwait(false);

            var statObjectArgs = new StatObjectArgs()
                .WithBucket(videoBucketName)
                .WithObject(videoObjectName);
            var objectStat = await minioClient.StatObjectAsync(statObjectArgs, token);

            videoDb.ObjectName = objectStat.ObjectName;
        }
        catch (MinioException e)
        {
            Console.WriteLine("File Upload Error: {0}", e.Message);
            throw;
        }

        try
        {
            var thumbnailExtension = Path.GetExtension(video.Thumbnail.Name);
            var thumbnailObjectName = $"{videoDb.Id}/original.{thumbnailExtension}";

            // Make a bucket on the server, if not already present.
            var beArgs = new BucketExistsArgs()
                .WithBucket(thumbnailBucketName);
            bool found = await minioClient.BucketExistsAsync(beArgs, token).ConfigureAwait(false);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs()
                    .WithBucket(thumbnailBucketName);
                await minioClient.MakeBucketAsync(mbArgs, token).ConfigureAwait(false);
            }
            
            var thumbnailStream = video.Thumbnail.OpenReadStream(sizes.MaxThumbnailSize, cancellationToken: token);
            // Upload a file to bucket.
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(thumbnailBucketName)
                .WithObject(thumbnailObjectName)
                .WithStreamData(thumbnailStream)
                .WithContentType(video.Thumbnail.ContentType);
            var objectResponse = await minioClient.PutObjectAsync(putObjectArgs, token).ConfigureAwait(false);

            var statObjectArgs = new StatObjectArgs()
                .WithBucket(thumbnailBucketName)
                .WithObject(thumbnailObjectName);
            var objectStat = await minioClient.StatObjectAsync(statObjectArgs, token);

            videoDb.ObjectName = objectStat.ObjectName;
        }
        catch (MinioException e)
        {
            Console.WriteLine("File Upload Error: {0}", e.Message);
            throw;
        }

        await transaction.CommitAsync(token);
    }

    public Video GetVideoById(Guid id)
    {
        return context.Videos.Find(id) ?? throw new KeyNotFoundException($"Video with ID {id} not found");
    }

    public async Task DeleteVideo(Guid id)
    {
        var video = context.Videos.Find(id) ?? throw new KeyNotFoundException("Video with ID {id} not found.");
        context.Videos.Remove(video);

        await context.SaveChangesAsync();
    }

    public async Task<Video> EditVideo(Guid id, Video video)
    {
        var existingVideo = context.Videos.Find(id) ?? throw new KeyNotFoundException("Video with ID {id} not found.");
        existingVideo.Title = video.Title;
        existingVideo.Description = video.Description;

        await context.SaveChangesAsync();

        return existingVideo;
    }

    public async Task<List<Video>> GetVideos(int size)
    {
        return await context.Videos
            .OrderByDescending(v => v.CreatedAt)
            .Take(size)
            .ToListAsync();
    }
}