using CommunityToolkit.Mvvm.ComponentModel;
using Guit.Api.Notifications;
using Guit.Core.Text;

namespace Guit.Core.Models;

/// <summary>One inbox row. Display strings are computed once here so the list template needs no converters.</summary>
public sealed partial class NotificationItem : ObservableObject
{
    private NotificationItem(NotificationThread thread, DateTimeOffset now)
    {
        Id = thread.Id;
        Title = thread.Subject.Title;
        Repository = thread.Repository.FullName;
        Kind = thread.Subject.Type;
        Reason = thread.Reason.Replace('_', ' ');
        Glyph = GlyphFor(thread.Subject.Type);
        AvatarUrl = SmallAvatar(thread.Repository.Owner.AvatarUrl);
        Updated = RelativeTime.Format(thread.UpdatedAt, now);
        SubjectUrl = thread.Subject.Url;
        IsUnread = thread.Unread;
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>"owner/name".</summary>
    public string Repository { get; }

    /// <summary>The subject type: "Issue", "PullRequest", "Release", ...</summary>
    public string Kind { get; }

    public string Reason { get; }

    /// <summary>A Segoe Fluent Icons character for <see cref="Kind"/>.</summary>
    public string Glyph { get; }

    public string AvatarUrl { get; }

    public string Updated { get; }

    /// <summary>API URL of the issue or pull request, when there is one.</summary>
    public string? SubjectUrl { get; }

    [ObservableProperty]
    public partial bool IsUnread { get; set; }

    public static NotificationItem From(NotificationThread thread, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return new NotificationItem(thread, now);
    }

    /// <summary>Asks GitHub for a 64 px avatar instead of the full-size one.</summary>
    internal static string SmallAvatar(string url)
        => url.Length == 0 ? url : url + (url.Contains('?', StringComparison.Ordinal) ? "&s=64" : "?s=64");

    private static string GlyphFor(string kind) => kind switch
    {
        "PullRequest" => "",
        "Issue" => "",
        "Release" => "",
        "Discussion" => "",
        "CheckSuite" => "",
        _ => "",
    };
}
