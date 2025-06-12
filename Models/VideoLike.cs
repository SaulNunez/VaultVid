namespace VideoHostingService.Models;

public class VideoLike
{
    public int Id { get; set; }
    public int VoteSense { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EditedBy { get; set; }
    public Video Video { get; set; }
}