namespace VideoHostingService.Models;

public class Video
{
    public Guid Id { get; set; }

    /// <summary>Identifier used in URLs, so the primary key is never exposed.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public required string Title { get; set; }

    public required string Description { get; set; }

    /// <summary>Key of the video object in the storage bucket. Null until the upload completes.</summary>
    public string? ObjectName { get; set; }

    /// <summary>Key of the thumbnail object in the storage bucket. Null when none was supplied.</summary>
    public string? ThumbnailLocation { get; set; }

    /// <summary>Id of the <see cref="Microsoft.AspNetCore.Identity.IdentityUser"/> that uploaded this.</summary>
    public required string UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset EditedAt { get; set; }

    public List<VideoComment> Comments { get; set; } = [];

    public List<VideoLike> VideoLikes { get; set; } = [];
}
