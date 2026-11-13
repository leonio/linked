using Guit.Core.Models;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

public sealed partial class RunsPage : Page
{
    public RunsPage()
    {
        ViewModel = App.Services.GetRequiredService<RunsViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public RunsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Does nothing after the first time; the view model keeps the repository list.
        ViewModel.LoadRepositoriesCommand.Execute(null);
    }

    private void OnRepositorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ComboBox)sender).SelectedItem is string repository)
        {
            ViewModel.SelectedRepository = repository;
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemIndex >= ViewModel.Runs.Count - 10)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
        => ViewModel.OpenCommand.Execute(e.ClickedItem as RunItem);
}
