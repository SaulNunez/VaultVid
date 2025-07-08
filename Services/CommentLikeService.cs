using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface ICommentLikeService
{
    Task AddOrUpdate(Guid commentId, Guid userId, CommentLike commentLike);
    Task Delete(Guid commentLikeId);
    Dictionary<VoteSense, int> GetVoteInformationOnComment(Guid commentId);
}

public class CommentLikeService(ApplicationDbContext context, RedisCacheService cacheService) : ICommentLikeService
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

    public Dictionary<VoteSense, int> GetVoteInformationOnComment(Guid commentId)
    {
        //While collisions between other object types in the cache with a GUID are pretty much impossible
        //Having a prefix allows quick debugging of the cache
        var idAsString = $"CommentLike_{commentId}";
        var votingInformation = cacheService.GetCacheData<Dictionary<VoteSense, int>>(idAsString);
        if (votingInformation == null)
        {
            var comment = context.VideoComments.Find(commentId) ?? throw new KeyNotFoundException($"Comment with ID {commentId} not found");

            var likeInformation = comment.CommentLikes
                .GroupBy(x => x.VoteSense)
                .Select(group => new { voteSense = group.Key, count = group.Count() });

            votingInformation = [];
            foreach (var item in likeInformation)
            {
                votingInformation.Add(item.voteSense, item.count);
            }

            cacheService.SetCacheData(idAsString, votingInformation, new TimeSpan(0, 30, 0));
        }

        return votingInformation;
    }
}