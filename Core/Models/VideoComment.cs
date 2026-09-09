namespace VideoHostingService.Models;

public class VideoComment
{
    public int Id { get; set; }

    public required string Text { get; set; }

    public string? VideoPosition { get; set; }

    /// <summary>Id of the <see cref="Microsoft.AspNetCore.Identity.IdentityUser"/> that wrote this.</summary>
    public required string UserId { get; set; }

    /// <summary>Denormalised display name, so rendering a comment list needs no join against Identity.</summary>
    public required string UserName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid VideoId { get; set; }

    public Video? Video { get; set; }

    public List<CommentLike> CommentLikes { get; set; } = [];
}
