using System.Text.Json;
using Guit.Api.Reactions;

namespace Guit.Api.Tests;

public sealed class ReactionsApiTests
{
    [Test]
    public async Task CreateForIssueComment_posts_the_content()
    {
        using RecordingHandler http = new("""{"id":1,"content":"+1"}""");

        await Stub.Client<IReactionsApi>(http).CreateForIssueCommentAsync("o", "r", 4242424242L, new CreateReactionRequest(ReactionContent.PlusOne), CancellationToken.None);

        await Assert.That(http.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(http.Path).IsEqualTo("/repos/o/r/issues/comments/4242424242/reactions");

        // The serializer writes "+" as \u002B, which is the same JSON string; compare the decoded value.
        using JsonDocument body = JsonDocument.Parse(http.Body!);
        await Assert.That(body.RootElement.GetProperty("content").GetString()).IsEqualTo("+1");
    }

    [Test]
    public async Task CreateForIssue_posts_to_the_issue()
    {
        using RecordingHandler http = new("""{"id":2,"content":"rocket"}""");

        await Stub.Client<IReactionsApi>(http).CreateForIssueAsync("o", "r", 7, new CreateReactionRequest(ReactionContent.Rocket), CancellationToken.None);

        await Assert.That(http.Path).IsEqualTo("/repos/o/r/issues/7/reactions");
        await Assert.That(http.Body).IsEqualTo("""{"content":"rocket"}""");
    }
}
