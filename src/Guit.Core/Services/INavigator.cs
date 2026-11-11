namespace Guit.Core.Services;

/// <summary>The places the app can show. The app maps each one to a page.</summary>
public enum Route
{
    Inbox,
    PullRequests,
    Conversation,
    Runs,
    RunDetail,
    JobLog,
    PullFiles,
    Settings,
}

/// <summary>Lets view models ask for navigation without knowing about pages. Implemented by the app.</summary>
public interface INavigator
{
    bool CanGoBack { get; }

    /// <summary>Shows a route.</summary>
    /// <param name="route">Where to go.</param>
    /// <param name="argument">Optional argument, for example "owner/repo#12". Strings only, never objects.</param>
    void Navigate(Route route, string? argument = null);

    void GoBack();
}
