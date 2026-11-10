using Guit.Core.Text;

namespace Guit.Core.Tests;

public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(-30, "now")]
    [Arguments(59, "now")]
    [Arguments(5 * 60, "5m")]
    [Arguments(3 * 3600, "3h")]
    [Arguments(2 * 86400, "2d")]
    [Arguments(6 * 86400 + 86399, "6d")]
    public async Task Format_uses_the_largest_unit_under_a_week(int secondsAgo, string expected)
    {
        await Assert.That(RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now)).IsEqualTo(expected);
    }

    [Test]
    public async Task Format_shows_day_and_month_within_the_same_year()
    {
        await Assert.That(RelativeTime.Format(new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero), Now)).IsEqualTo("12 Mar");
    }

    [Test]
    public async Task Format_adds_the_year_for_older_dates()
    {
        await Assert.That(RelativeTime.Format(new DateTimeOffset(2024, 3, 12, 0, 0, 0, TimeSpan.Zero), Now)).IsEqualTo("12 Mar 2024");
    }
}
