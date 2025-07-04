using Microsoft.EntityFrameworkCore;
using VideoHostingService.Components.Pages;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using Video = VideoHostingService.Models.Video;

namespace VideoHostingService.Services;

public interface IVideoService
{
    Task AddVideo(Video video);
    Video GetVideoById(Guid id);
    Task DeleteVideo(Guid id);
    Task<Video> EditVideo(Guid id, Video video);
    Task<List<Video>> GetVideos(int size);
}

public class VideoService(ApplicationDbContext context) : IVideoService
{
    public async Task AddVideo(Video video)
    {
        if (video == null)
        {
            throw new ArgumentNullException(nameof(video), "Video information can't be null");
        }

        video.CreatedAt = DateTimeOffset.UtcNow;
        context.Videos.Add(video);
        await context.SaveChangesAsync();
    }

    public Video GetVideoById(Guid id)
    {
        return context.Videos.Find(id) ?? throw new KeyNotFoundException($"Video with ID {id} not found");
    }

    public async Task DeleteVideo(Guid id)
    {
        var video = context.Videos.Find(id) ?? throw new KeyNotFoundException("Video with ID {id} not found.");
        context.Videos.Remove(video);

        await context.SaveChangesAsync();
    }

    public async Task<Video> EditVideo(Guid id, Video video)
    {
        var existingVideo = context.Videos.Find(id) ?? throw new KeyNotFoundException("Video with ID {id} not found.");
        existingVideo.Title = video.Title;
        existingVideo.Description = video.Description;
        existingVideo.Tags = video.Tags;

        await context.SaveChangesAsync();

        return existingVideo;
    }

    public async Task<List<Video>> GetVideos(int size)
    {
        return await context.Videos
            .OrderByDescending(v => v.CreatedAt)
            .Take(size)
            .ToListAsync();
    }
}