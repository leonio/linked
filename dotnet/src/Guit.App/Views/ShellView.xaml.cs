using Guit.App.Services;
using Guit.Core.Services;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Guit.App.Views;

public sealed partial class ShellView : UserControl
{
    private readonly Navigator _navigator;

    public ShellView()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        _navigator = App.Services.GetRequiredService<Navigator>();
        InitializeComponent();
        ViewModel.LoadUserCommand.Execute(null);
    }

    public ShellViewModel ViewModel { get; }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        // The frame is created together with the NavigationView (x:Load), so hand it over here.
        _navigator.Frame = ContentFrame;
        Navigation.SelectedItem ??= Navigation.MenuItems[0];
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        => Show(args.IsSettingsSelected, args.SelectedItem as NavigationViewItem);

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // Clicking the section that is already selected raises no SelectionChanged. After opening a
        // conversation from the inbox, that click is how the member gets back to the list.
        if (ReferenceEquals(args.InvokedItemContainer, sender.SelectedItem))
        {
            Show(args.IsSettingsInvoked, args.InvokedItemContainer as NavigationViewItem);
        }
    }

    private void OnSectionAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Navigation is null while signed out (x:Load).
        int index = sender.Key - VirtualKey.Number1;
        if (Navigation is { } navigation && index >= 0 && index < navigation.MenuItems.Count)
        {
            if (ReferenceEquals(navigation.SelectedItem, navigation.MenuItems[index]))
            {
                Show(false, navigation.MenuItems[index] as NavigationViewItem);
            }
            else
            {
                navigation.SelectedItem = navigation.MenuItems[index];
            }

            args.Handled = true;
        }
    }

    private void Show(bool settings, NavigationViewItem? item)
    {
        Route route = settings
            ? Route.Settings
            : item?.Tag switch
            {
                "pulls" => Route.PullRequests,
                "runs" => Route.Runs,
                _ => Route.Inbox,
            };

        _navigator.Frame = ContentFrame;
        _navigator.Navigate(route);
    }
}
