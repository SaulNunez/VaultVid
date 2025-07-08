namespace VideoHostingService.Models;

public class CommentLike
{
    public int Id { get; set; }
    public VoteSense VoteSense { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EditedAt { get; set; }
    public VideoComment Comment { get; set; }
    public Guid UserId { get; set; }
}