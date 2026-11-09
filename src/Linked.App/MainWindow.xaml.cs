using Linked.App.Views;
using Linked.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Linked.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ViewModel.LoadUserCommand.Execute(null);
    }

    public ShellViewModel ViewModel { get; }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        NavigationView navigation = (NavigationView)sender;
        navigation.SelectedItem ??= navigation.MenuItems[0];
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Navigate by typeof, never by a type name string: typeof keeps the page visible to the trimmer.
        Type page = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "pulls" => typeof(PlaceholderPage),
            _ => typeof(PlaceholderPage),
        };

        // x:Load creates ContentFrame together with the NavigationView, so resolve it through the sender.
        if (sender.Content is Frame frame)
        {
            frame.Navigate(page);
        }
    }
}
