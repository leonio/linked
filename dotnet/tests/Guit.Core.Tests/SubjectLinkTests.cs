using Guit.Core.Models;

namespace Guit.Core.Tests;

public sealed class SubjectLinkTests
{
    [Test]
    public async Task TryParse_reads_an_issue_url()
    {
        bool ok = SubjectLink.TryParse("https://api.github.com/repos/octocat/hello-world/issues/12", out SubjectLink link);

        await Assert.That(ok).IsTrue();
        await Assert.That(link).IsEqualTo(new SubjectLink("octocat", "hello-world", 12, false));
    }

    [Test]
    public async Task TryParse_reads_a_pull_request_url()
    {
        bool ok = SubjectLink.TryParse("https://api.github.com/repos/octocat/hello/pulls/7", out SubjectLink link);

        await Assert.That(ok).IsTrue();
        await Assert.That(link.IsPullRequest).IsTrue();
        await Assert.That(link.ToWebUri()).IsEqualTo(new Uri("https://github.com/octocat/hello/pull/7"));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("https://api.github.com/repos/octocat/hello/releases/3")]
    [Arguments("https://api.github.com/repos/octocat/hello/issues/abc")]
    [Arguments("https://api.github.com/repos/octocat/hello/issues/12/comments")]
    [Arguments("https://example.com/repos/octocat/hello/issues/12")]
    public async Task TryParse_rejects_anything_else(string? url)
    {
        await Assert.That(SubjectLink.TryParse(url, out _)).IsFalse();
    }

    [Test]
    [Arguments("octocat", "hello.world", 12, false, "octocat/hello.world#12")]
    [Arguments("octocat", "hello", 7, true, "octocat/hello!7")]
    public async Task The_argument_round_trips(string owner, string repo, int number, bool isPull, string argument)
    {
        SubjectLink link = new(owner, repo, number, isPull);

        await Assert.That(link.ToArgument()).IsEqualTo(argument);
        await Assert.That(SubjectLink.TryParseArgument(argument, out SubjectLink parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(link);
    }

    [Test]
    [Arguments(null)]
    [Arguments("octocat/hello")]
    [Arguments("octocat#12")]
    [Arguments("/hello#12")]
    [Arguments("octocat/#12")]
    [Arguments("octocat/hello#x")]
    public async Task TryParseArgument_rejects_malformed_text(string? argument)
    {
        await Assert.That(SubjectLink.TryParseArgument(argument, out _)).IsFalse();
    }
}
