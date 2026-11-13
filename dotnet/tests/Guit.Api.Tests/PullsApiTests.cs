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

    [Test]
    public async Task ListFiles_sends_paging_and_maps_patches()
    {
        using RecordingHandler http = new("""
            [ { "sha": "abc", "filename": "src/a.cs", "status": "modified", "additions": 2, "deletions": 1, "changes": 3,
                "patch": "@@ -1,2 +1,3 @@\n line\n-old\n+new\n+more" },
              { "sha": "def", "filename": "docs/new.png", "status": "added", "additions": 0, "deletions": 0, "changes": 0 },
              { "sha": "ghi", "filename": "src/b.cs", "status": "renamed", "additions": 0, "deletions": 0, "changes": 0, "previous_filename": "src/old.cs" } ]
            """);

        PullFile[] files = await Stub.Client<IPullsApi>(http).ListFilesAsync("o", "r", 7, page: 2, perPage: 100, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/o/r/pulls/7/files?page=2&per_page=100");
        await Assert.That(files.Length).IsEqualTo(3);
        await Assert.That(files[0].Patch).IsEqualTo("@@ -1,2 +1,3 @@\n line\n-old\n+new\n+more");
        await Assert.That(files[0].Additions).IsEqualTo(2);
        await Assert.That(files[1].Patch).IsNull();
        await Assert.That(files[2].PreviousFilename).IsEqualTo("src/old.cs");
    }
}
