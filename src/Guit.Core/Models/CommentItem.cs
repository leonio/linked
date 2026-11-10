using CommunityToolkit.Mvvm.ComponentModel;
using Guit.Api.Issues;
using Guit.Api.Users;
using Guit.Core.Text;

namespace Guit.Core.Models;

/// <summary>One post in a conversation: the opening post of the issue, or a comment.</summary>
public sealed partial class CommentItem : ObservableObject
{
    private CommentItem(long id, bool isOpeningPost, GitHubUser? user, string? body, string? association, DateTimeOffset createdAt, DateTimeOffset now)
    {
        Id = id;
        IsOpeningPost = isOpeningPost;

        // A deleted account comes back as null; GitHub shows those posts as written by "ghost".
        Author = user?.Login ?? "ghost";
        AvatarUrl = user is null ? string.Empty : NotificationItem.SmallAvatar(user.AvatarUrl);
        Body = body ?? string.Empty;
        Association = association is null or "NONE" ? null : association.ToLowerInvariant().Replace('_', ' ');
        Posted = RelativeTime.Format(createdAt, now);
    }

    /// <summary>The comment id, or the issue id for the opening post.</summary>
    public long Id { get; }

    public bool IsOpeningPost { get; }

    public string Author { get; }

    public string AvatarUrl { get; }

    public string Body { get; }

    public string Posted { get; }

    /// <summary>"member", "owner", "contributor"... or null when the author has no special relation to the repository.</summary>
    public string? Association { get; }

    public static CommentItem From(IssueComment comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return new CommentItem(comment.Id, false, comment.User, comment.BodyText, comment.AuthorAssociation, comment.CreatedAt, now);
    }

    public static CommentItem FromIssue(Issue issue, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new CommentItem(issue.Id, true, issue.User, issue.BodyText, null, issue.CreatedAt, now);
    }
}
