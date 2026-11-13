using System.Text.Json.Serialization;
using Guit.Api.Users;

namespace Guit.Api.Issues;

/// <summary>Schema: issue. Only the fields the app shows.</summary>
/// <param name="PullRequest">Present (not null) when this issue is a pull request.</param>
public sealed record Issue(
    long Id,
    int Number,
    string Title,
    string State,
    int Comments,
    DateTimeOffset CreatedAt,
    string HtmlUrl,
    GitHubUser? User = null,
    string? BodyText = null,
    string? BodyHtml = null,
    bool? Draft = null,
    IssuePullRequestRef? PullRequest = null,
    ReactionRollup? Reactions = null);

/// <summary>Schema: issue.pull_request (inline object).</summary>
public sealed record IssuePullRequestRef(string? HtmlUrl = null);

/// <summary>Schema: issue-comment.</summary>
/// <param name="User">Null when the author's account was deleted.</param>
public sealed record IssueComment(
    long Id,
    DateTimeOffset CreatedAt,
    string HtmlUrl,
    GitHubUser? User = null,
    string? BodyText = null,
    string? BodyHtml = null,
    string? AuthorAssociation = null,
    ReactionRollup? Reactions = null);

/// <summary>Request body of issues/create-comment.</summary>
/// <param name="Body">The comment text, in Markdown.</param>
public sealed record CreateCommentRequest(string Body);

/// <summary>Schema: reaction-rollup. The thumbs are named "+1" and "-1" in JSON.</summary>
public sealed record ReactionRollup(
    int TotalCount,
    [property: JsonPropertyName("+1")] int PlusOne,
    [property: JsonPropertyName("-1")] int MinusOne,
    int Laugh,
    int Confused,
    int Heart,
    int Hooray,
    int Eyes,
    int Rocket);
