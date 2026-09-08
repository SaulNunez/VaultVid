using Microsoft.EntityFrameworkCore;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface IVideoCommentService
{
    Task<IReadOnlyList<VideoComment>> GetCommentsAsync(Guid videoPublicId, int size, int offset, CancellationToken cancellationToken);

    Task<VideoComment> AddCommentAsync(Guid videoPublicId, CreateComment comment, string userId, string userName, CancellationToken cancellationToken);

    /// <summary>Returns false when the comment does not exist or is not owned by <paramref name="userId"/>.</summary>
    Task<bool> DeleteAsync(int commentId, string userId, CancellationToken cancellationToken);
}

public class VideoCommentService(ApplicationDbContext context) : IVideoCommentService
{
    public async Task<IReadOnlyList<VideoComment>> GetCommentsAsync(Guid videoPublicId, int size, int offset, CancellationToken cancellationToken)
        => await context.VideoComments
            .AsNoTracking()
            .Where(c => c.Video!.PublicId == videoPublicId)
            .OrderByDescending(c => c.CreatedAt)
            .Skip(offset)
            .Take(size)
            .ToListAsync(cancellationToken);

    public async Task<VideoComment> AddCommentAsync(Guid videoPublicId, CreateComment comment, string userId, string userName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Only the key is needed, so this avoids loading the whole video and its collections.
        var videoId = await context.Videos
            .Where(v => v.PublicId == videoPublicId)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (videoId == Guid.Empty)
        {
            throw new KeyNotFoundException($"Video with ID {videoPublicId} not found");
        }

        var dbComment = new VideoComment
        {
            VideoId = videoId,
            Text = comment.Comment,
            UserId = userId,
            UserName = userName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.VideoComments.Add(dbComment);
        await context.SaveChangesAsync(cancellationToken);

        return dbComment;
    }

    public async Task<bool> DeleteAsync(int commentId, string userId, CancellationToken cancellationToken)
    {
        var existingComment = await context.VideoComments.FindAsync([commentId], cancellationToken);
        if (existingComment is null || !string.Equals(existingComment.UserId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        context.VideoComments.Remove(existingComment);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
