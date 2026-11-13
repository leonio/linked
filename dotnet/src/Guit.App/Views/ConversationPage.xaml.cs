using System.Collections.Specialized;
using Guit.Core.Models;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

/// <summary>One issue or pull request. Not cached: each conversation is a new page with a new view model.</summary>
public sealed partial class ConversationPage : Page
{
    public ConversationPage()
    {
        ViewModel = App.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();
        ViewModel.Comments.CollectionChanged += OnCommentsChanged;
    }

    public ConversationViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Going back to the inbox and forward again reuses nothing, so only a fresh page loads.
        if (ViewModel.Comments.Count == 0)
        {
            ViewModel.LoadCommand.Execute(e.Parameter as string);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemIndex >= ViewModel.Comments.Count - 10)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Bring the member's own new comment into view. Pages loaded while scrolling must not move the list.
        if (e.Action == NotifyCollectionChangedAction.Add && ViewModel.IsSending && ViewModel.Comments.Count > 0)
        {
            List.ScrollIntoView(ViewModel.Comments[^1]);
        }
    }

    // x:Bind inside a DataTemplate cannot reach the page's view model, so reactions come through here.
    // ReactAsync reports its own failures through ErrorMessage, which is what makes async void safe here.
    private async void OnReactClick(object sender, RoutedEventArgs e)
    {
        MenuFlyoutItem choice = (MenuFlyoutItem)sender;
        if (choice.CommandParameter is CommentItem item && choice.Tag is string content)
        {
            await ViewModel.ReactAsync(item, content);
        }
    }
}
