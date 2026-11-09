using Triage.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Triage.App.Views;

public sealed partial class ShellView : UserControl
{
    public ShellView()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        ViewModel.LoadUserCommand.Execute(null);
    }

    public ShellViewModel ViewModel { get; }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
        => Navigation.SelectedItem ??= Navigation.MenuItems[0];

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Navigate by typeof, never by a type name string: typeof keeps the page visible to the trimmer.
        Type page = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "pulls" => typeof(PlaceholderPage),
            _ => typeof(PlaceholderPage),
        };

        ContentFrame.Navigate(page);
    }
}
