using Guit.App.Services;
using Guit.Core.Services;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    {
        Route route = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "pulls" => Route.PullRequests,
            "runs" => Route.Runs,
            _ => Route.Inbox,
        };

        _navigator.Frame = ContentFrame;
        _navigator.Navigate(route);
    }
}
