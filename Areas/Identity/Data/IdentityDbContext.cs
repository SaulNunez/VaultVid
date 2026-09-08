using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VideoHostingService.Models.Identity;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Video> Videos { get; set; }
    public DbSet<VideoComment> VideoComments { get; set; }
    public DbSet<VideoLike> VideoLikes { get; set; }
    public DbSet<CommentLike> CommentLikes { get; set; }
    public DbSet <Playlist> Playlists { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Video>(video =>
        {
            // PublicId is what appears in URLs, so lookups by it need to be indexed and unique.
            video.HasIndex(v => v.PublicId).IsUnique();
            video.HasIndex(v => v.CreatedAt);
            video.HasIndex(v => v.UserId);

            video.HasMany(v => v.Comments)
                .WithOne(c => c.Video!)
                .HasForeignKey(c => c.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            video.HasMany(v => v.VideoLikes)
                .WithOne(l => l.Video!)
                .HasForeignKey(l => l.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VideoComment>(comment =>
        {
            comment.HasIndex(c => c.UserId);

            comment.HasMany(c => c.CommentLikes)
                .WithOne(l => l.Comment!)
                .HasForeignKey(l => l.CommentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // One vote per user per video / per comment.
        builder.Entity<VideoLike>()
            .HasIndex(l => new { l.VideoId, l.UserId })
            .IsUnique();

        builder.Entity<CommentLike>()
            .HasIndex(l => new { l.CommentId, l.UserId })
            .IsUnique();

        builder.Entity<Playlist>()
            .HasIndex(p => p.UserId);
    }
}
