using Guit.Api.Search;
using Guit.Core.Text;

namespace Guit.Core.Models;

/// <summary>Which of the member's open pull requests to list. The numeric values match the scope bar's item order.</summary>
public enum PullRequestScope
{
    ReviewRequested = 0,
    Created = 1,
    Assigned = 2,
}

/// <summary>One row of the pull requests page. Nothing changes after creation, so it is a plain immutable class.</summary>
public sealed partial class PullRequestItem
{
    private const string RepositoryPrefix = "https://api.github.com/repos/";

    private PullRequestItem(IssueSearchItem item, DateTimeOffset now)
    {
        string repository = item.RepositoryUrl.StartsWith(RepositoryPrefix, StringComparison.Ordinal)
            ? item.RepositoryUrl[RepositoryPrefix.Length..]
            : item.RepositoryUrl;

        Title = item.Title;
        Reference = $"{repository} #{item.Number}";
        Author = item.User?.Login ?? "ghost";
        AvatarUrl = item.User is null ? string.Empty : NotificationItem.SmallAvatar(item.User.AvatarUrl);
        Updated = RelativeTime.Format(item.UpdatedAt, now);
        IsDraft = item.Draft is true;
        Glyph = IsDraft ? "" : "";
        Argument = SubjectLink.TryParse($"{item.RepositoryUrl}/pulls/{item.Number}", out SubjectLink link) ? link.ToArgument() : null;
        WebUrl = item.HtmlUrl;
    }

    public string Title { get; }

    /// <summary>"owner/repo #12".</summary>
    public string Reference { get; }

    public string Author { get; }

    public string AvatarUrl { get; }

    public string Updated { get; }

    public bool IsDraft { get; }

    public string Glyph { get; }

    /// <summary>Navigation argument for the conversation page; null if the repository URL could not be understood.</summary>
    public string? Argument { get; }

    public string WebUrl { get; }

    public static PullRequestItem From(IssueSearchItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new PullRequestItem(item, now);
    }
}
