using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface ICommentLikeService
{
    Task AddOrUpdate(Guid commentId, Guid userId, CommentLike commentLike);
    Task Delete(Guid commentLikeId);
}

public class CommentLikeService(ApplicationDbContext context) : ICommentLikeService
{
    public async Task AddOrUpdate(Guid commentId, Guid userId, CommentLike commentLike)
    {
        var comment = context.VideoComments.Find(commentId) ?? throw new KeyNotFoundException($"Comment with ID {commentId} not found");

        var existingLike = context.CommentLikes.Where(x => x.UserId == userId).FirstOrDefault();

        if (existingLike == null)
        {
            comment.CommentLikes.Add(commentLike);
        }
        else
        {
            existingLike.VoteSense = commentLike.VoteSense;
            existingLike.EditedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync();
    }

    public async Task Delete(Guid commentLikeId)
    {
        var commentLike = context.CommentLikes.Find(commentLikeId) ?? throw new KeyNotFoundException($"Video like with ID {commentLikeId} not found");
        context.CommentLikes.Remove(commentLike);
        await context.SaveChangesAsync();
    }
}