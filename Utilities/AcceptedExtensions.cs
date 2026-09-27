namespace VideoHostingService.Utilities;

public static class AcceptedExtensions
{
    public static readonly string[] PermittedVideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".webm", ".mpeg", ".mpg", ".m4v", ".ogv", ".xvid"];

    public static readonly string[] PermittedImageExtensions =
        [".webp", ".jpeg", ".jpg", ".png", ".gif", ".bmp"];

    /// <summary>Value for an <c>accept</c> attribute on a file input.</summary>
    public static readonly string VideoAcceptAttribute = string.Join(',', PermittedVideoExtensions);

    public static readonly string ImageAcceptAttribute = string.Join(',', PermittedImageExtensions);
}
