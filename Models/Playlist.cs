namespace VideoHostingService.Models;

public class Playlist
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    /// <summary>Id of the <see cref="Microsoft.AspNetCore.Identity.IdentityUser"/> that owns this playlist.</summary>
    public required string UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset EditedAt { get; set; }

    // Was a public field, which EF Core does not map as a navigation.
    public List<Video> Videos { get; set; } = [];
}
