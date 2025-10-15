namespace VideoHostingService.Utilities;

public static class AcceptedExtensions
{
    public static readonly string[] permittedVideoExtensions = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".mpeg", ".mpg", ".m4v", ".ogv", ".xvid"];
    private static readonly string[] permittedImageExtensions = [".webp", ".jpeg", ".jpg", ".png", ".gif", ".bmp"];
}