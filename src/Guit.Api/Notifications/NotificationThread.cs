using Guit.Api.Repos;

namespace Guit.Api.Notifications;

/// <summary>Schema: thread.</summary>
public sealed record NotificationThread(
    string Id,
    bool Unread,
    string Reason,
    DateTimeOffset UpdatedAt,
    NotificationSubject Subject,
    RepositorySummary Repository,
    DateTimeOffset? LastReadAt = null);

/// <summary>Schema: thread.subject (inline object).</summary>
/// <param name="Title">The issue, pull request, release or discussion title.</param>
/// <param name="Type">"Issue", "PullRequest", "Release", "Discussion", "CheckSuite", "Commit", ...</param>
/// <param name="Url">API URL of the subject. Null for some types (for example CheckSuite).</param>
/// <param name="LatestCommentUrl">API URL of the newest comment, if any.</param>
public sealed record NotificationSubject(
    string Title,
    string Type,
    string? Url = null,
    string? LatestCommentUrl = null);
