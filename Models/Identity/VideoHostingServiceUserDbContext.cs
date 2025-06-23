using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VideoHostingService.Models.Identity;

public class VideoHostingServiceUserDbContext(
    DbContextOptions<VideoHostingServiceUserDbContext> options
    ) : IdentityDbContext(options)
{
    
}