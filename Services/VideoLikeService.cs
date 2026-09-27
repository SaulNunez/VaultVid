using Microsoft.EntityFrameworkCore;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Services;

public interface IVideoLikeService
{
    Task SetVoteAsync(Guid videoPublicId, string userId, VoteSense voteSense, CancellationToken cancellationToken);

    Task<VoteSense> GetVoteAsync(Guid videoPublicId, string userId, CancellationToken cancellationToken);

    Task<Dictionary<VoteSense, int>> GetVoteTotalsAsync(Guid videoPublicId, CancellationToken cancellationToken);
}

public class VideoLikeService(ApplicationDbContext context) : IVideoLikeService
{
    public async Task SetVoteAsync(Guid videoPublicId, string userId, VoteSense voteSense, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var videoId = await context.Videos
            .Where(v => v.PublicId == videoPublicId)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (videoId == Guid.Empty)
        {
            throw new KeyNotFoundException($"Video with ID {videoPublicId} not found");
        }

        // The existing vote has to be scoped to this video as well as this user, otherwise any
        // one of the user's votes elsewhere would be picked up and overwritten.
        var existingLike = await context.VideoLikes
            .FirstOrDefaultAsync(l => l.VideoId == videoId && l.UserId == userId, cancellationToken);

        if (existingLike is null)
        {
            if (voteSense == VoteSense.NONE)
            {
                return;
            }

            context.VideoLikes.Add(new VideoLike
            {
                VideoId = videoId,
                UserId = userId,
                VoteSense = voteSense,
                CreatedAt = DateTimeOffset.UtcNow,
                EditedAt = DateTimeOffset.UtcNow,
            });
        }
        else if (voteSense == VoteSense.NONE)
        {
            context.VideoLikes.Remove(existingLike);
        }
        else
        {
            existingLike.VoteSense = voteSense;
            existingLike.EditedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<VoteSense> GetVoteAsync(Guid videoPublicId, string userId, CancellationToken cancellationToken)
        => await context.VideoLikes
            .AsNoTracking()
            .Where(l => l.Video!.PublicId == videoPublicId && l.UserId == userId)
            .Select(l => l.VoteSense)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Dictionary<VoteSense, int>> GetVoteTotalsAsync(Guid videoPublicId, CancellationToken cancellationToken)
        => await context.VideoLikes
            .AsNoTracking()
            .Where(l => l.Video!.PublicId == videoPublicId)
            .GroupBy(l => l.VoteSense)
            .Select(group => new { VoteSense = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.VoteSense, x => x.Count, cancellationToken);
}
