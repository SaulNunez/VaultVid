namespace VideoHostingService.Models;

public class VideoLike
{
    public int Id { get; set; }
    public int VoteSense { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EditedAt { get; set; }
    public Video Video { get; set; }
}