using VideoHostingService.Models;

namespace VideoHostingService.Models;

public class VideoComment
{
    public int Id { get; set; }
    public string Text { get; set; }
    public string? VideoPosition { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Video Video { get; set; }

    public List<CommentLike> CommentLikes { get; set; }
}