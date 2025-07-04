using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VideoHostingService.Models.Identity;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public DbSet<Video> Videos { get; set; }
    public DbSet<VideoComment> VideoComments { get; set; }
    public DbSet<VideoLike> VideoLikes { get; set; }
    public DbSet<CommentLike> CommentLikes { get; set; }
    public DbSet <Playlist> Playlists { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
