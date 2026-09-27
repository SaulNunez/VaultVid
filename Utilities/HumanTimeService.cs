namespace VideoHostingService.Utilities;

public interface IHumanTimeService
{
    /// <summary>Renders how long ago <paramref name="since"/> was, relative to <paramref name="now"/>.</summary>
    string PrettyTimeDifference(DateTimeOffset since, DateTimeOffset now);
}

public class HumanTimeService : IHumanTimeService
{
    public string PrettyTimeDifference(DateTimeOffset since, DateTimeOffset now)
    {
        // now - since, not since - now: the latter is negative for everything in the past,
        // which made every timestamp read as "less than a minute ago".
        var elapsed = now - since;

        if (elapsed < TimeSpan.Zero)
        {
            return "just now";
        }

        // Total*, not the component properties: TimeSpan.Minutes wraps at 60.
        if (elapsed.TotalSeconds < 60)
        {
            return "less than a minute ago";
        }

        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed.TotalHours < 24)
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        if (elapsed.TotalDays < 30)
        {
            var days = (int)elapsed.TotalDays;
            return days == 1 ? "a day ago" : $"{days} days ago";
        }

        if (elapsed.TotalDays < 365)
        {
            var months = (int)(elapsed.TotalDays / 30);
            return months == 1 ? "a month ago" : $"{months} months ago";
        }

        var years = (int)(elapsed.TotalDays / 365);
        return years == 1 ? "a year ago" : $"{years} years ago";
    }
}
