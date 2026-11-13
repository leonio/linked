using Guit.Api.Search;

namespace Guit.Api.Tests;

public sealed class SearchApiTests
{
    [Test]
    public async Task SearchIssues_sends_the_query_and_maps_the_items()
    {
        using RecordingHandler http = new("""
            { "total_count": 2, "incomplete_results": false,
              "items": [
                { "id": 70, "number": 7, "title": "Fix it", "state": "open", "comments": 2, "score": 1.0,
                  "updated_at": "2026-01-02T03:04:05Z", "html_url": "https://github.com/octocat/hello/pull/7",
                  "repository_url": "https://api.github.com/repos/octocat/hello",
                  "user": { "login": "hubot", "id": 2, "avatar_url": "https://avatars/2", "html_url": "https://github.com/hubot" },
                  "draft": true, "pull_request": { "html_url": "https://github.com/octocat/hello/pull/7" } },
                { "id": 71, "number": 8, "title": "Old", "state": "open", "comments": 0,
                  "updated_at": "2026-01-01T00:00:00Z", "html_url": "https://github.com/octocat/hello/pull/8",
                  "repository_url": "https://api.github.com/repos/octocat/hello", "user": null }
              ] }
            """);

        IssueSearchResult result = await Stub.Client<ISearchApi>(http).SearchIssuesAsync(
            "is:open is:pr review-requested:@me", "updated", "desc", page: 2, perPage: 50, CancellationToken.None);

        // Compare the decoded query: how ":" and "@" are escaped is the client's business, not the test's.
        await Assert.That(Uri.UnescapeDataString(http.Path!)).IsEqualTo(
            "/search/issues?q=is:open is:pr review-requested:@me&sort=updated&order=desc&page=2&per_page=50");
        await Assert.That(http.Path!.Contains(' ', StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.TotalCount).IsEqualTo(2);
        await Assert.That(result.Items.Length).IsEqualTo(2);
        await Assert.That(result.Items[0].RepositoryUrl).IsEqualTo("https://api.github.com/repos/octocat/hello");
        await Assert.That(result.Items[0].User!.Login).IsEqualTo("hubot");
        await Assert.That(result.Items[0].Draft is true).IsTrue();
        await Assert.That(result.Items[1].User).IsNull();
        await Assert.That(result.Items[1].Draft).IsNull();
    }
}
