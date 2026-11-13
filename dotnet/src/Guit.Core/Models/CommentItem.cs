using CommunityToolkit.Mvvm.ComponentModel;
using System.Text;
using Guit.Api.Issues;
using Guit.Api.Reactions;
using Guit.Api.Users;
using Guit.Core.Text;

namespace Guit.Core.Models;

/// <summary>One post in a conversation: the opening post of the issue, or a comment.</summary>
public sealed partial class CommentItem : ObservableObject
{
    // Same order as ReactionContent.All.
    private static readonly string[] Emoji = ["\U0001F44D", "\U0001F44E", "\U0001F604", "\U0001F389", "\U0001F615", "\u2764\uFE0F", "\U0001F680", "\U0001F440"];

    private readonly int[] _reactionCounts = new int[8];

    private CommentItem(long id, bool isOpeningPost, GitHubUser? user, string? body, string? bodyHtml, string? association, ReactionRollup? reactions, DateTimeOffset createdAt, DateTimeOffset now)
    {
        if (reactions is not null)
        {
            _reactionCounts = [reactions.PlusOne, reactions.MinusOne, reactions.Laugh, reactions.Hooray, reactions.Confused, reactions.Heart, reactions.Rocket, reactions.Eyes];
        }

        ReactionSummary = BuildSummary();
        Id = id;
        IsOpeningPost = isOpeningPost;

        // A deleted account comes back as null; GitHub shows those posts as written by "ghost".
        Author = user?.Login ?? "ghost";
        AvatarUrl = user is null ? string.Empty : NotificationItem.SmallAvatar(user.AvatarUrl);
        Body = body ?? string.Empty;

        // Parse once, here, so the list only walks ready-made blocks while scrolling.
        Content = string.IsNullOrEmpty(bodyHtml) ? RichDocument.FromPlainText(body) : HtmlRichText.Parse(bodyHtml);
        Association = association is null or "NONE" ? null : association.ToLowerInvariant().Replace('_', ' ');
        Posted = RelativeTime.Format(createdAt, now);
    }

    /// <summary>The comment id, or the issue id for the opening post.</summary>
    public long Id { get; }

    public bool IsOpeningPost { get; }

    public string Author { get; }

    public string AvatarUrl { get; }

    /// <summary>The post as plain text.</summary>
    public string Body { get; }

    /// <summary>The post with its formatting, ready to draw.</summary>
    public RichDocument Content { get; }

    public string Posted { get; }

    /// <summary>"member", "owner", "contributor"... or null when the author has no special relation to the repository.</summary>
    public string? Association { get; }

    /// <summary>The non-zero reaction counts as one line, for example "👍 3  ❤️ 1". Empty when there are none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReactions))]
    public partial string ReactionSummary { get; private set; }

    public bool HasReactions => ReactionSummary.Length > 0;

    /// <summary>Changes one reaction count by <paramref name="delta"/> and rebuilds the summary. Unknown names are ignored.</summary>
    public void AdjustReaction(string content, int delta)
    {
        for (int i = 0; i < ReactionContent.All.Count; i++)
        {
            if (ReactionContent.All[i] == content)
            {
                _reactionCounts[i] = Math.Max(0, _reactionCounts[i] + delta);
                ReactionSummary = BuildSummary();
                return;
            }
        }
    }

    private string BuildSummary()
    {
        StringBuilder? summary = null;
        for (int i = 0; i < _reactionCounts.Length; i++)
        {
            if (_reactionCounts[i] > 0)
            {
                summary ??= new StringBuilder();
                if (summary.Length > 0)
                {
                    summary.Append("  ");
                }

                summary.Append(Emoji[i]).Append(' ').Append(_reactionCounts[i]);
            }
        }

        return summary?.ToString() ?? string.Empty;
    }

    /// <summary>What a screen reader announces for this row (a ListView reads an item through ToString).</summary>
    public override string ToString()
        => $"{(IsOpeningPost ? "Opening post" : "Comment")} by {Author}, {Posted}. {Body}";

    public static CommentItem From(IssueComment comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return new CommentItem(comment.Id, false, comment.User, comment.BodyText, comment.BodyHtml, comment.AuthorAssociation, comment.Reactions, comment.CreatedAt, now);
    }

    public static CommentItem FromIssue(Issue issue, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new CommentItem(issue.Id, true, issue.User, issue.BodyText, issue.BodyHtml, null, issue.Reactions, issue.CreatedAt, now);
    }
}
