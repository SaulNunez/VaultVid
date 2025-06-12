namespace VideoHostingService.Models;

public class Playlist
{
    public int Id { get; set; }
    public List<Video> Videos = [];
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EditedAt { get; set; }
}