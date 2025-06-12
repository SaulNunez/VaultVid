namespace VideoHostingService.Models;

public class Video
{
    public int Id { get; set; }
    public string PublicId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Bucket { get; set; }
    public string Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}