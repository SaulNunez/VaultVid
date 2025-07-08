namespace VideoHostingService.Models;

public class Video
{
    public Guid Id { get; set; }
    public Guid PublicId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string ObjectName { get; set; }
    public string ThumbnailLocation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EditedAt { get; set; }

    public List<VideoComment> Comments { get; set; } 

    public List<VideoLike> VideoLikes { get; set; }
}