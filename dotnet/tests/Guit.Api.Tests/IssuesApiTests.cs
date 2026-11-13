using Guit.Api.Issues;

namespace Guit.Api.Tests;

public sealed class IssuesApiTests
{
    private const string IssueJson = """
        {
          "id": 70, "number": 7, "title": "Fix it", "state": "open", "comments": 3, "locked": false,
          "created_at": "2026-01-01T00:00:00Z", "html_url": "https://github.com/octocat/hello/pull/7",
          "user": { "login": "octocat", "id": 583231, "avatar_url": "https://avatars/1", "html_url": "https://github.com/octocat" },
          "body": "Please **review**", "body_text": "Please review", "body_html": "<p>Please <strong>review</strong></p>",
          "draft": true,
          "pull_request": { "url": "https://api.github.com/repos/octocat/hello/pulls/7", "html_url": "https://github.com/octocat/hello/pull/7" },
          "reactions": { "total_count": 3, "+1": 2, "-1": 0, "laugh": 0, "confused": 0, "heart": 1, "hooray": 0, "eyes": 0, "rocket": 0 }
        }
        """;

    [Test]
    public async Task Get_asks_for_the_full_media_type_and_maps_the_issue()
    {
        using RecordingHandler http = new(IssueJson);
        IIssuesApi api = Stub.Client<IIssuesApi>(http);

        Issue issue = await api.GetAsync("octocat", "hello", 7, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/octocat/hello/issues/7");
        await Assert.That(http.Accept).IsEqualTo("application/vnd.github.full+json");
        await Assert.That(issue.Number).IsEqualTo(7);
        await Assert.That(issue.User!.Login).IsEqualTo("octocat");
        await Assert.That(issue.BodyText).IsEqualTo("Please review");
        await Assert.That(issue.BodyHtml).IsEqualTo("<p>Please <strong>review</strong></p>");
        await Assert.That(issue.Draft is true).IsTrue();
        await Assert.That(issue.PullRequest).IsNotNull();
        await Assert.That(issue.Reactions!.PlusOne).IsEqualTo(2);
        await Assert.That(issue.Reactions.Heart).IsEqualTo(1);
    }

    [Test]
    public async Task Get_leaves_PullRequest_null_for_a_plain_issue()
    {
        using RecordingHandler http = new("""{"id":1,"number":2,"title":"Bug","state":"closed","comments":0,"created_at":"2026-01-01T00:00:00Z","html_url":"https://github.com/o/r/issues/2","user":null}""");

        Issue issue = await Stub.Client<IIssuesApi>(http).GetAsync("o", "r", 2, CancellationToken.None);

        await Assert.That(issue.PullRequest).IsNull();
        await Assert.That(issue.User).IsNull();
        await Assert.That(issue.BodyText).IsNull();
    }

    [Test]
    public async Task ListComments_sends_paging_and_maps_comments()
    {
        using RecordingHandler http = new("""
            [{ "id": 11, "created_at": "2026-01-02T03:04:05Z", "html_url": "https://github.com/o/r/issues/2#issuecomment-11",
               "user": { "login": "hubot", "id": 2, "avatar_url": "https://avatars/2", "html_url": "https://github.com/hubot" },
               "body_text": "Looks good", "author_association": "MEMBER", "node_id": "x" },
             { "id": 12, "created_at": "2026-01-03T00:00:00Z", "html_url": "https://github.com/o/r/issues/2#issuecomment-12", "user": null }]
            """);

        IssueComment[] comments = await Stub.Client<IIssuesApi>(http).ListCommentsAsync("o", "r", 2, page: 3, perPage: 50, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/o/r/issues/2/comments?page=3&per_page=50");
        await Assert.That(comments.Length).IsEqualTo(2);
        await Assert.That(comments[0].User!.Login).IsEqualTo("hubot");
        await Assert.That(comments[0].AuthorAssociation).IsEqualTo("MEMBER");
        await Assert.That(comments[0].CreatedAt).IsEqualTo(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        await Assert.That(comments[1].User).IsNull();
        await Assert.That(comments[1].BodyText).IsNull();
    }

    [Test]
    public async Task CreateComment_posts_the_body_as_json()
    {
        using RecordingHandler http = new("""{"id":13,"created_at":"2026-01-04T00:00:00Z","html_url":"https://github.com/o/r/issues/2#issuecomment-13","user":{"login":"me","id":3,"avatar_url":"https://avatars/3","html_url":"https://github.com/me"},"body_text":"hello"}""");

        IssueComment created = await Stub.Client<IIssuesApi>(http).CreateCommentAsync("o", "r", 2, new CreateCommentRequest("hello"), CancellationToken.None);

        await Assert.That(http.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(http.Path).IsEqualTo("/repos/o/r/issues/2/comments");
        await Assert.That(http.Body).IsEqualTo("""{"body":"hello"}""");
        await Assert.That(created.Id).IsEqualTo(13L);
        await Assert.That(created.User!.Login).IsEqualTo("me");
    }
}
