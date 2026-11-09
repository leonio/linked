using Triage.Api.Notifications;
using Refit.Testing;

namespace Triage.Api.Tests;

public sealed class NotificationsApiTests
{
    private const string OneThread = """
        [{
          "id": "1", "unread": true, "reason": "review_requested",
          "updated_at": "2026-01-02T03:04:05Z", "last_read_at": null,
          "subject": { "title": "Fix it", "type": "PullRequest", "url": "https://api.github.com/repos/octocat/hello/pulls/7", "latest_comment_url": null },
          "repository": { "id": 9, "name": "hello", "full_name": "octocat/hello", "private": false, "html_url": "https://github.com/octocat/hello",
                          "owner": { "login": "octocat", "id": 583231, "avatar_url": "https://avatars/1", "html_url": "https://github.com/octocat" } }
        }]
        """;

    [Test]
    public async Task List_sends_the_query_and_maps_nested_objects()
    {
        using RecordingHandler http = new(OneThread);
        INotificationsApi api = Stub.Client<INotificationsApi>(http);

        NotificationThread[] threads = await api.ListAsync(all: true, participating: false, page: 2, perPage: 50, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/notifications?all=true&participating=false&page=2&per_page=50");
        await Assert.That(threads.Length).IsEqualTo(1);
        NotificationThread thread = threads[0];
        await Assert.That(thread.Unread).IsTrue();
        await Assert.That(thread.UpdatedAt).IsEqualTo(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        await Assert.That(thread.LastReadAt).IsNull();
        await Assert.That(thread.Subject.Type).IsEqualTo("PullRequest");
        await Assert.That(thread.Repository.FullName).IsEqualTo("octocat/hello");
        await Assert.That(thread.Repository.Owner.Login).IsEqualTo("octocat");
    }

    [Test]
    public async Task MarkAsRead_patches_the_thread()
    {
        using StubHttp http = new()
        {
            { Route.Patch("/notifications/threads/{id}"), Reply.Status(System.Net.HttpStatusCode.ResetContent) },
        };

        await Stub.Client<INotificationsApi>(http).MarkAsReadAsync("42", CancellationToken.None);

        await Assert.That(http.Requests[0].RequestUri!.AbsolutePath).IsEqualTo("/notifications/threads/42");
        http.VerifyAllCalled();
    }

    [Test]
    public async Task MarkAsDone_deletes_the_thread()
    {
        using StubHttp http = new()
        {
            { Route.Delete("/notifications/threads/{id}"), Reply.Status(System.Net.HttpStatusCode.NoContent) },
        };

        await Stub.Client<INotificationsApi>(http).MarkAsDoneAsync("42", CancellationToken.None);

        http.VerifyAllCalled();
    }
}
