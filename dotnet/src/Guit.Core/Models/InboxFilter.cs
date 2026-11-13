namespace Guit.Core.Models;

/// <summary>Which notifications the inbox asks for. The numeric values match the filter bar's item order.</summary>
public enum InboxFilter
{
    Unread = 0,
    All = 1,
    Participating = 2,
}

/// <summary>Which kinds of notification the inbox shows. The numeric values match the kind box's item order.</summary>
public enum InboxKind
{
    All = 0,
    PullRequests = 1,
    Issues = 2,
    Releases = 3,
    Discussions = 4,
    Checks = 5,
    Other = 6,
}
