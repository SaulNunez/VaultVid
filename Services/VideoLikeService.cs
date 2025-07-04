using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface IVideoLikeService
{
    Task AddOrUpdate(Guid videoId, Guid userId, VideoLike videoLike);
    Task Delete(Guid videoLikeId);
}

public class VideoLikeService(ApplicationDbContext context) : IVideoLikeService
{
    public async Task AddOrUpdate(Guid videoId, Guid userId, VideoLike videoLike)
    {
        var video = context.Videos.Find(videoId) ?? throw new KeyNotFoundException($"Video with ID {videoId} not found");

        var existingLike = context.VideoLikes.Where(x => x.UserId == userId).FirstOrDefault();

        if (existingLike == null)
        {
            video.VideoLikes.Add(videoLike);
        }
        else
        {
            existingLike.VoteSense = videoLike.VoteSense;
            existingLike.EditedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync();
    }

    public async Task Delete(Guid videoLikeId)
    {
        var videoLike = context.VideoLikes.Find(videoLikeId) ?? throw new KeyNotFoundException($"Video like with ID {videoLikeId} not found");
        context.VideoLikes.Remove(videoLike);
        await context.SaveChangesAsync();
    }
}