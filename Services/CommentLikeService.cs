using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface ICommentLikeService
{
    Task SetVoteAsync(int commentId, string userId, VoteSense voteSense, CancellationToken cancellationToken);

    Task<Dictionary<VoteSense, int>> GetVoteTotalsAsync(int commentId, CancellationToken cancellationToken);
}

public class CommentLikeService(ApplicationDbContext context, IDistributedCache cache) : ICommentLikeService
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
    };

    public async Task SetVoteAsync(int commentId, string userId, VoteSense voteSense, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var commentExists = await context.VideoComments.AnyAsync(c => c.Id == commentId, cancellationToken);
        if (!commentExists)
        {
            throw new KeyNotFoundException($"Comment with ID {commentId} not found");
        }

        // Scoped to the comment as well as the user; filtering on the user alone would pick up
        // that user's vote on some other comment.
        var existingLike = await context.CommentLikes
            .FirstOrDefaultAsync(l => l.CommentId == commentId && l.UserId == userId, cancellationToken);

        if (existingLike is null)
        {
            if (voteSense == VoteSense.NONE)
            {
                return;
            }

            context.CommentLikes.Add(new CommentLike
            {
                CommentId = commentId,
                UserId = userId,
                VoteSense = voteSense,
                CreatedAt = DateTimeOffset.UtcNow,
                EditedAt = DateTimeOffset.UtcNow,
            });
        }
        else if (voteSense == VoteSense.NONE)
        {
            context.CommentLikes.Remove(existingLike);
        }
        else
        {
            existingLike.VoteSense = voteSense;
            existingLike.EditedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        // The totals below are cached, so they have to be dropped when a vote changes.
        await cache.RemoveAsync(CacheKey(commentId), cancellationToken);
    }

    public async Task<Dictionary<VoteSense, int>> GetVoteTotalsAsync(int commentId, CancellationToken cancellationToken)
    {
        var key = CacheKey(commentId);

        var cached = await cache.GetStringAsync(key, cancellationToken);
        if (cached is not null)
        {
            var deserialized = JsonSerializer.Deserialize<Dictionary<VoteSense, int>>(cached);
            if (deserialized is not null)
            {
                return deserialized;
            }
        }

        // Aggregate in the database rather than loading every like row into memory.
        var totals = await context.CommentLikes
            .AsNoTracking()
            .Where(l => l.CommentId == commentId)
            .GroupBy(l => l.VoteSense)
            .Select(group => new { VoteSense = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.VoteSense, x => x.Count, cancellationToken);

        await cache.SetStringAsync(key, JsonSerializer.Serialize(totals), CacheOptions, cancellationToken);

        return totals;
    }

    // While collisions between other object types in the cache are pretty much impossible,
    // having a prefix allows quick debugging of the cache.
    private static string CacheKey(int commentId) => $"CommentLike_{commentId}";
}
