using Guit.Core.Models;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

public sealed partial class PullRequestsPage : Page
{
    public PullRequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<PullRequestsViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public PullRequestsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Items.Count == 0)
        {
            ViewModel.LoadCommand.Execute(null);
        }
    }

    private void OnScopeSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0)
        {
            ViewModel.Scope = (PullRequestScope)index;
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemIndex >= ViewModel.Items.Count - 10)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
        => ViewModel.OpenCommand.Execute(e.ClickedItem as PullRequestItem);
}
