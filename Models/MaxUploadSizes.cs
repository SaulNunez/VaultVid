namespace VideoHostingService.Models;

public class MaxUploadSizes
{
    public const string SectionName = "MaxUploadSizes";

    // long, not int: the configured default (2 GiB) overflows Int32 and would fail to bind.
    public long MaxVideoSize { get; set; } = 2L * 1024 * 1024 * 1024;

    public long MaxThumbnailSize { get; set; } = 10L * 1024 * 1024;
}
