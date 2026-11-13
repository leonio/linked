using System.Globalization;

namespace Guit.Core.Text;

public static class RelativeTime
{
    /// <summary>Short age text for list rows: "now", "5m", "3h", "2d", "12 Mar", "12 Mar 2024".</summary>
    public static string Format(DateTimeOffset then, DateTimeOffset now)
    {
        TimeSpan age = now - then;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes}m");
        }

        if (age < TimeSpan.FromDays(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours}h");
        }

        if (age < TimeSpan.FromDays(7))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays}d");
        }

        return then.ToString(then.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
