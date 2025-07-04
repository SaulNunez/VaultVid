namespace VideoHostingService.Utilities;

public interface IHumanTimeService
{
    string PrettyTimeDifference(DateTime since, DateTime now);
}

public class HumanTimeService() : IHumanTimeService
{
    public string PrettyTimeDifference(DateTime since, DateTime now)
    {
        var timespan = since - now;

        if (timespan.Seconds < 60)
        {
            return "less than a minute ago";
        }
        else if (timespan.Minutes < 60)
        {
            return "less than an hour ago";
        }
        else if (timespan.Minutes < 120)
        {
            return $"an hour ago";
        }
        else if (timespan.Hours < 24)
        {
            return $"{timespan.Hours} hours ago";
        }
        else if (timespan.Days < 365)
        {
            return $"{since.Date}/{since.Day}";
        }
        else if (timespan.Days < (365 * 2))
        {
            return "a year ago";
        }
        else
        {
            var yearsAgo = timespan.Days / 365;
            return $"{yearsAgo} years ago";
        }
    }
}