namespace VideoHostingService.Models;

public class CommentLike
{
    public int Id { get; set; }
    public int VoteSense { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EditedBy { get; set; }
    public VideoComment Comment { get; set; }
}