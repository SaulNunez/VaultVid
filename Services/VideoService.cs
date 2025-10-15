using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using VideoHostingService.Components.Pages;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using Video = VideoHostingService.Models.Video;
using VideoHostingService.Utilities;

namespace VideoHostingService.Services;

public interface IVideoService
{
    Task<string> UploadThumbnail(Stream thumbnailFileStream, long thumbnailSize, string contentType, CancellationToken token);
    Task<string> UploadVideo(Stream videoFileStream, CancellationToken token, IProgress<ProgressReport> progress = null);
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

    public async Task<string> UploadThumbnail(Stream thumbnailFileStream,
    long thumbnailSize, string extensionFromSource, string contentType,
    CancellationToken token = null)
    {
        if(!ImageExtensions.IsValidImageHeader(thumbnailFileStream, extensionFromSource))
        {
            throw new InvalidFileException();
        }

        var sizes = configuration.GetSection("MaxUploadSizes").Get<MaxUploadSizes>();

        try
        {
            var beArgs = new BucketExistsArgs()
                .WithBucket("vaultvid");
            bool found = await minioClient.BucketExistsAsync(beArgs, token).ConfigureAwait(false);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs()
                    .WithBucket("vaultvid");
                await minioClient.MakeBucketAsync(mbArgs, token).ConfigureAwait(false);
            }

            var trustedFileName = Path.GetRandomFileName();
            var guid = Guid.NewGuid();
            var filename = Path.Join("thumbnails", guid);

            var args = new PutObjectArgs()
                .WithBucket("vaultvid")
                .WtihObject(filename)
                .WithStreamData(thumbnailFileStream)
                .WithObjectSize(thumbnailSize)
                .WithContentType(contentType);
            await minioClient.PutObjectAsync(args, token);

            var statObjectArgs = new StatObjectArgs()
                .WithBucket("vaultvid")
                .WithObject(filename);
            var objectStat = await minioClient.StatObjectAsync(statObjectArgs, token);

            return objectStat.ObjectName;
        }
        catch (MinioException e)
        {
            Console.WriteLine("File Upload Error: {0}", e.Message);
            throw;
        }
    }

    public async Task<string> UploadVideo(Stream videoFileStream, string extensionFromSource, string contentType,
    IProgress<ProgressReport> progress = null, CancellationToken token = null)
    {
        if (!VideoValidator.IsValidVideoHeader(thumbnailFileStream, extensionFromSource))
        {
            throw new InvalidFileException();
        }
        
        var sizes = configuration.GetSection("MaxUploadSizes").Get<MaxUploadSizes>();

        try
        {
            var beArgs = new BucketExistsArgs()
           .WithBucket("vaultvid");
            bool found = await minioClient.BucketExistsAsync(beArgs, token).ConfigureAwait(false);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs()
                    .WithBucket("vaultvid");
                await minioClient.MakeBucketAsync(mbArgs, token).ConfigureAwait(false);
            }

            var trustedFileName = Path.GetRandomFileName();
            var guid = Guid.NewGuid();
            var filename = Path.Join("videos", guid);

            var args = new PutObjectArgs()
                .WithBucket("vaultvid")
                .WtihObject(filename)
                .WithStreamData(thumbnailFileStream)
                .WithProgress(progress)
                .WithObjectSize(thumbnailSize)
                .WithContentType(contentType);
            await minioClient.PutObjectAsync(args, token);

            var statObjectArgs = new StatObjectArgs()
                .WithBucket("vaultvid")
                .WithObject(filename);
            var objectStat = await minioClient.StatObjectAsync(statObjectArgs, token);

            return objectStat.ObjectName;
        }
        catch (MinioException e)
        {
            Console.Error.WriteLine("File Upload Error: {0}", e.Message);
            throw;
        }
    }

    public async Task AddVideo(VideoUpload video, CancellationToken token)
    {
        if (video == null)
        {
            throw new ArgumentNullException(nameof(video), "Video information can't be null");
        }

        var videoDb = new Video
        {
            Title = video.Title,
            Description = video.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Videos.Add(videoDb);
        await context.SaveChangesAsync(token);
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