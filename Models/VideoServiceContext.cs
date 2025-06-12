using Microsoft.EntityFrameworkCore;

namespace VideoHostingService.Models;

public class VideoServiceContext(DbContextOptions<VideoServiceContext> options) : DbContext(options)
{
    public DbSet<Video> Videos { get; set; }
    public DbSet<VideoComment> VideoComments { get; set; }
    public DbSet<VideoLike> VideoLikes { get; set; }
    public DbSet<CommentLike> CommentLikes { get; set; }
    public DbSet <Playlist> Playlists { get; set; }
}