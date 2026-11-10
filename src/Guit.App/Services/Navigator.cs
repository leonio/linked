using Guit.App.Views;
using Guit.Core.Services;
using Microsoft.UI.Xaml.Controls;

namespace Guit.App.Services;

/// <summary>Maps routes to pages and drives the shell's content frame.</summary>
internal sealed class Navigator : INavigator
{
    /// <summary>Set by ShellView once its frame exists; cleared when the member signs out.</summary>
    public Frame? Frame { get; set; }

    public bool CanGoBack => Frame is { CanGoBack: true };

    public void Navigate(Route route, string? argument = null)
    {
        // typeof keeps each page visible to the trimmer. Never navigate by type name.
        Type page = route switch
        {
            Route.Inbox => typeof(InboxPage),
            _ => typeof(PlaceholderPage),
        };

        Frame?.Navigate(page, argument);
    }

    public void GoBack()
    {
        if (Frame is { CanGoBack: true } frame)
        {
            frame.GoBack();
        }
    }
}
