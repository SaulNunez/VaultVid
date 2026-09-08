namespace VideoHostingService.Models;

public class ObjectStorageConfiguration
{
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// Endpoint the server talks to, e.g. the "minio" service name inside the compose network.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint presigned URLs are generated against. This one is handed to browsers, so it has
    /// to be reachable from outside the container network. Falls back to <see cref="Endpoint"/>,
    /// which is only correct for single-host setups where both resolve to the same address.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    public bool UseSsl { get; set; }

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string BucketName { get; set; } = "vaultvid";

    /// <summary>How long a presigned media URL stays valid, in seconds.</summary>
    public int PresignedUrlExpirySeconds { get; set; } = 60 * 60;
}
