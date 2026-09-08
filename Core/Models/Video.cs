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

    /// <summary>Where this video is in the transcoding pipeline; it is only playable when Ready.</summary>
    public VideoStatus Status { get; set; } = VideoStatus.Pending;

    /// <summary>Dimensions of the uploaded source, as reported by ffprobe. Null until probed.</summary>
    public int? SourceWidth { get; set; }

    public int? SourceHeight { get; set; }

    public double? DurationSeconds { get; set; }

    /// <summary>
    /// Key of the HLS master playlist, set once the required renditions exist. Null while the
    /// video is still processing.
    /// </summary>
    public string? MasterPlaylistObjectName { get; set; }

    /// <summary>Why transcoding failed, shown to the uploader. Null unless <see cref="Status"/> is Failed.</summary>
    public string? ProcessingError { get; set; }

    /// <summary>When a worker last claimed this video, used to re-queue jobs abandoned mid-flight.</summary>
    public DateTimeOffset? ProcessingStartedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset EditedAt { get; set; }

    public List<VideoComment> Comments { get; set; } = [];

    public List<VideoLike> VideoLikes { get; set; } = [];

    /// <summary>Completed transcode ladder rungs, growing while the video plays.</summary>
    public List<VideoRendition> Renditions { get; set; } = [];
}
