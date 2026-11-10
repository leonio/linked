using Guit.Core.Models;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Guit.App.Views;

public sealed partial class InboxPage : Page
{
    public InboxPage()
    {
        ViewModel = App.Services.GetRequiredService<InboxViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Poll only while the page is on screen. Unloaded also covers signing out.
        Loaded += (_, _) => ViewModel.StartPolling();
        Unloaded += (_, _) => ViewModel.StopPolling();
    }

    public InboxViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Items.Count == 0)
        {
            ViewModel.LoadCommand.Execute(null);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // Ask for the next page shortly before the last row comes into view.
        if (args.ItemIndex >= ViewModel.Items.Count - 10)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void OnFilterSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0)
        {
            ViewModel.Filter = (InboxFilter)index;
        }
    }

    // x:Bind inside a DataTemplate cannot reach the page's view model, so row actions come through here.
    private void OnMarkAsReadClick(object sender, RoutedEventArgs e)
        => ViewModel.MarkAsReadCommand.Execute(((MenuFlyoutItem)sender).CommandParameter as NotificationItem);

    private void OnMarkAsDoneClick(object sender, RoutedEventArgs e)
        => ViewModel.MarkAsDoneCommand.Execute(((MenuFlyoutItem)sender).CommandParameter as NotificationItem);

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete
            && FocusManager.GetFocusedElement(XamlRoot) is ListViewItem container
            && List.ItemFromContainer(container) is NotificationItem item)
        {
            ViewModel.MarkAsDoneCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
        => ViewModel.OpenCommand.Execute(e.ClickedItem as NotificationItem);
}
