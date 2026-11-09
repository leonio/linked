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

        // Select after InitializeComponent so ContentFrame exists when SelectionChanged fires.
        Navigation.SelectedItem = Navigation.MenuItems[0];
        ViewModel.InitializeCommand.Execute(null);
    }

    public ShellViewModel ViewModel { get; }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Navigate by type token, never by string name: typeof keeps the page visible to the trimmer.
        Type page = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "messaging" => typeof(PlaceholderPage),
            _ => typeof(PlaceholderPage),
        };

        ContentFrame.Navigate(page);
    }
}
