using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using VideoHostingService.Models;

namespace VideoHostingService.Services;

public interface IVideoUploadService
{
    Task VideoUploadAsync(string title, string description, string extension,
        string contentType, Stream fileContentStream);
}

public class VideoUploadService(IMinioClient minioClient,
    VideoServiceContext videoServiceContext) : IVideoUploadService
{
    public static readonly string bucketName = "videos";

    public async Task VideoUploadAsync(string title, string description, string extension,
        string contentType, Stream fileContentStream)
    {
        await using var transaction = await videoServiceContext.Database.BeginTransactionAsync();
        var videoId = Guid.NewGuid();

        var video = new Video
        {
            Title = title,
            Description = description,
            PublicId = videoId
        };

        var objectName = $"{videoId}/original.{extension}";

        try
        {
            // Make a bucket on the server, if not already present.
            var beArgs = new BucketExistsArgs()
                .WithBucket(bucketName);
            bool found = await minioClient.BucketExistsAsync(beArgs).ConfigureAwait(false);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs()
                    .WithBucket(bucketName);
                await minioClient.MakeBucketAsync(mbArgs).ConfigureAwait(false);
            }
            // Upload a file to bucket.
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithStreamData(fileContentStream)
                .WithContentType(contentType);
            var objectResponse = await minioClient.PutObjectAsync(putObjectArgs).ConfigureAwait(false);

            var statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName);
            var objectStat = await minioClient.StatObjectAsync(statObjectArgs);

            video.ObjectName = objectStat.ObjectName;

            await transaction.CommitAsync();
        }
        catch (MinioException e)
        {
            Console.WriteLine("File Upload Error: {0}", e.Message);
            throw;
        }
    }
}