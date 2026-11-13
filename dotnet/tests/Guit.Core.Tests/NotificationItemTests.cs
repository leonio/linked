using Guit.Core.Models;

namespace Guit.Core.Tests;

public sealed class NotificationItemTests
{
    [Test]
    public async Task From_maps_every_display_value()
    {
        NotificationItem item = NotificationItem.From(Sample.Thread("42"), Sample.Now);

        await Assert.That(item.Id).IsEqualTo("42");
        await Assert.That(item.Title).IsEqualTo("Fix it");
        await Assert.That(item.Repository).IsEqualTo("octocat/hello");
        await Assert.That(item.Kind).IsEqualTo("PullRequest");
        await Assert.That(item.Reason).IsEqualTo("review requested");
        await Assert.That(item.Glyph).IsEqualTo("");
        await Assert.That(item.Updated).IsEqualTo("3h");
        await Assert.That(item.SubjectUrl).IsEqualTo("https://api.github.com/repos/octocat/hello/pulls/7");
        await Assert.That(item.IsUnread).IsTrue();
    }

    [Test]
    [Arguments("https://avatars/u/1?v=4", "https://avatars/u/1?v=4&s=64")]
    [Arguments("https://avatars/u/1", "https://avatars/u/1?s=64")]
    [Arguments("", "")]
    public async Task SmallAvatar_appends_the_size_parameter(string url, string expected)
    {
        await Assert.That(NotificationItem.SmallAvatar(url)).IsEqualTo(expected);
    }

    [Test]
    public async Task An_unknown_kind_gets_the_fallback_glyph()
    {
        NotificationItem item = NotificationItem.From(Sample.Thread("1", type: "Commit", url: null), Sample.Now);

        await Assert.That(item.Glyph).IsEqualTo("");
        await Assert.That(item.SubjectUrl).IsNull();
    }
}
