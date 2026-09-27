namespace VideoHostingService.Models;

/// <summary>
/// Progress of a single upload to object storage. <paramref name="TotalBytes"/> is 0 when the
/// source stream length is unknown, in which case <see cref="Percentage"/> stays 0.
/// </summary>
public readonly record struct UploadProgress(long BytesTransferred, long TotalBytes)
{
    public int Percentage => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesTransferred * 100 / TotalBytes, 0, 100);
}
