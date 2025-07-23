using VideoHostingService.Components;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface IVideoCommentService
{
    Task AddComment(Guid videoId, CreateComment comment);
    Task Delete(Guid videoCommentId);
    IEnumerable<VideoComment> GetComments(Guid videoId, int size, int offset);
}

public class VideoCommentService(ApplicationDbContext context) : IVideoCommentService
{
    public IEnumerable<VideoComment> GetComments(Guid videoId, int size, int offset)
    {
        var existingVideo = context.Videos.Find(videoId) ?? throw new KeyNotFoundException($"Video with ID {videoId} not found");

        return existingVideo.Comments.Skip(offset).Take(size).ToList();
    }

    public async Task AddComment(Guid videoId, CreateComment comment)
    {
        var existingVideo = context.Videos.Find(videoId) ?? throw new KeyNotFoundException($"Video with ID {videoId} not found");

        var dbComment = new VideoComment
        {
            Text = comment.Comment,
            CreatedAt = DateTimeOffset.UtcNow
        };

        existingVideo.Comments.Add(dbComment);

        await context.SaveChangesAsync();
    }

    public async Task Delete(Guid videoCommentId)
    {
        var existingComment = context.VideoComments.Find(videoCommentId) ?? throw new KeyNotFoundException($"Comment with ID {videoCommentId} not found");

        context.VideoComments.Remove(existingComment);

        await context.SaveChangesAsync();
    }
}