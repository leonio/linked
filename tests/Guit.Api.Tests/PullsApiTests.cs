using Guit.Api.Pulls;

namespace Guit.Api.Tests;

public sealed class PullsApiTests
{
    [Test]
    public async Task Get_maps_state_branches_and_diff_size()
    {
        using RecordingHandler http = new("""
            { "number": 7, "state": "closed", "merged": true, "draft": false, "mergeable": null, "title": "Fix it",
              "additions": 120, "deletions": 8, "changed_files": 5,
              "head": { "ref": "feature", "label": "contributor:feature", "sha": "abc" },
              "base": { "ref": "main", "label": "octocat:main", "sha": "def" } }
            """);

        PullRequest pull = await Stub.Client<IPullsApi>(http).GetAsync("octocat", "hello", 7, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/octocat/hello/pulls/7");
        await Assert.That(pull.State).IsEqualTo("closed");
        await Assert.That(pull.Merged).IsTrue();
        await Assert.That(pull.Draft is false).IsTrue();
        await Assert.That(pull.Mergeable).IsNull();
        await Assert.That(pull.Additions).IsEqualTo(120);
        await Assert.That(pull.Deletions).IsEqualTo(8);
        await Assert.That(pull.ChangedFiles).IsEqualTo(5);
        await Assert.That(pull.Head.Label).IsEqualTo("contributor:feature");
        await Assert.That(pull.Base.Ref).IsEqualTo("main");
    }
}
