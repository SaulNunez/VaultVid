using Microsoft.EntityFrameworkCore;
using VideoHostingService.Components.Pages;
using VideoHostingService.Models;
using Video = VideoHostingService.Models.Video;

namespace VideoHostingService.Services;

public interface IVideoService
{
    Task<Video?> GetVideo(string id);
    Task<List<Video>> GetVideos();
}

public class VideoService(VideoServiceContext context) : IVideoService
{
    public async Task<List<Video>> GetVideos()
    {
        return await context.Videos
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<Video?> GetVideo(string id)
    {
        return await context.Videos.Where(v => v.PublicId == id).FirstOrDefaultAsync();
    }
}