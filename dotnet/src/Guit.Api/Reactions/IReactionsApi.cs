using Refit;

namespace Guit.Api.Reactions;

/// <summary>OpenAPI tag: reactions.</summary>
[Headers("Authorization: Bearer")]
public interface IReactionsApi
{
    /// <summary>operationId: reactions/create-for-issue. Also used for the opening post of a pull request.</summary>
    [Post("/repos/{owner}/{repo}/issues/{issueNumber}/reactions")]
    Task CreateForIssueAsync(
        string owner,
        string repo,
        int issueNumber,
        [Body] CreateReactionRequest request,
        CancellationToken cancellationToken);

    /// <summary>operationId: reactions/create-for-issue-comment</summary>
    [Post("/repos/{owner}/{repo}/issues/comments/{commentId}/reactions")]
    Task CreateForIssueCommentAsync(
        string owner,
        string repo,
        long commentId,
        [Body] CreateReactionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Request body for creating a reaction.</summary>
/// <param name="Content">One of <see cref="ReactionContent.All"/>.</param>
public sealed record CreateReactionRequest(string Content);

/// <summary>The reaction names GitHub accepts, in the order GitHub shows them.</summary>
public static class ReactionContent
{
    public const string PlusOne = "+1";
    public const string MinusOne = "-1";
    public const string Laugh = "laugh";
    public const string Hooray = "hooray";
    public const string Confused = "confused";
    public const string Heart = "heart";
    public const string Rocket = "rocket";
    public const string Eyes = "eyes";

    public static IReadOnlyList<string> All { get; } = [PlusOne, MinusOne, Laugh, Hooray, Confused, Heart, Rocket, Eyes];
}
