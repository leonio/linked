using System.Net;
using Guit.Api.Notifications;
using Refit;
using Refit.Testing;

namespace Guit.Api.Tests;

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
            { Route.Patch("/notifications/threads/{id}"), Reply.Status(HttpStatusCode.ResetContent) },
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
            { Route.Delete("/notifications/threads/{id}"), Reply.Status(HttpStatusCode.NoContent) },
        };

        await Stub.Client<INotificationsApi>(http).MarkAsDoneAsync("42", CancellationToken.None);

        http.VerifyAllCalled();
    }

    [Test]
    public async Task Poll_without_a_date_is_unconditional_and_exposes_the_polling_headers()
    {
        using RecordingHandler http = new(OneThread) { ReplyHeaders = ["X-Poll-Interval: 90", "Last-Modified: Thu, 01 Jan 2026 10:00:00 GMT"] };
        INotificationsApi api = Stub.Client<INotificationsApi>(http);

        using ApiResponse<NotificationThread[]> reply = await api.PollAsync(all: false, participating: false, page: 1, perPage: 50, ifModifiedSince: null, CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/notifications?all=false&participating=false&page=1&per_page=50");
        await Assert.That(http.IfModifiedSince).IsNull();
        await Assert.That(reply.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(reply.Content!.Length).IsEqualTo(1);
        await Assert.That(string.Join(",", reply.Headers!.GetValues("X-Poll-Interval"))).IsEqualTo("90");
        await Assert.That(reply.ContentHeaders!.LastModified).IsEqualTo(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Poll_with_a_date_sends_it_and_reports_304_without_throwing()
    {
        using RecordingHandler http = new("[]") { Status = HttpStatusCode.NotModified, ReplyHeaders = ["X-Poll-Interval: 60"] };
        INotificationsApi api = Stub.Client<INotificationsApi>(http);

        using ApiResponse<NotificationThread[]> reply = await api.PollAsync(false, false, 1, 50, "Thu, 01 Jan 2026 10:00:00 GMT", CancellationToken.None);

        await Assert.That(http.IfModifiedSince).IsEqualTo("Thu, 01 Jan 2026 10:00:00 GMT");
        await Assert.That(reply.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That(reply.Content).IsNull();
    }
}
