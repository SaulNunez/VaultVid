using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using VideoHostingService.Models;

namespace VideoHostingService.Services;

public interface IMediaUrlService
{
    /// <summary>
    /// Presigned, time-limited URL for an object in the media bucket, or null when there is no object.
    /// </summary>
    Task<string?> GetUrlAsync(string? objectName, CancellationToken cancellationToken = default);
}

public class MediaUrlService : IMediaUrlService
{
    private readonly ObjectStorageConfiguration configuration;
    private readonly IMinioClient? client;

    public MediaUrlService(IOptions<ObjectStorageConfiguration> options, ILogger<MediaUrlService> logger)
    {
        configuration = options.Value;

        if (string.IsNullOrWhiteSpace(configuration.Endpoint))
        {
            logger.LogError("Object storage is not configured; media URLs will not be generated.");
            return;
        }

        // Presigning is done against the endpoint the browser will use, which is not the same
        // host the server talks to in a container network. The signature covers the host, so it
        // cannot be rewritten after the fact and needs its own client.
        client = new MinioClient()
            .WithEndpoint(string.IsNullOrWhiteSpace(configuration.PublicEndpoint)
                ? configuration.Endpoint
                : configuration.PublicEndpoint)
            .WithCredentials(configuration.AccessKey, configuration.SecretKey)
            .WithSSL(configuration.UseSsl)
            .Build();
    }

    public async Task<string?> GetUrlAsync(string? objectName, CancellationToken cancellationToken = default)
    {
        if (client is null || string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var args = new PresignedGetObjectArgs()
            .WithBucket(configuration.BucketName)
            .WithObject(objectName)
            .WithExpiry(configuration.PresignedUrlExpirySeconds);

        return await client.PresignedGetObjectAsync(args);
    }
}
