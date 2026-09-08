namespace VideoHostingService.Models;

public class CommentLike
{
    public int Id { get; set; }

    public VoteSense VoteSense { get; set; }

    /// <summary>Id of the <see cref="Microsoft.AspNetCore.Identity.IdentityUser"/> that voted.</summary>
    public required string UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset EditedAt { get; set; }

    public int CommentId { get; set; }

    public VideoComment? Comment { get; set; }
}
