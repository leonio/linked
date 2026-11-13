using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

/// <summary>The changed files of one pull request. Not cached: each one is a new page with a new view model.</summary>
public sealed partial class PullFilesPage : Page
{
    public PullFilesPage()
    {
        ViewModel = App.Services.GetRequiredService<PullFilesViewModel>();
        InitializeComponent();
    }

    public PullFilesViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Rows.Count == 0)
        {
            ViewModel.LoadCommand.Execute(e.Parameter as string);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // Pull requests with more than 100 files load the next 100 as the end comes near.
        if (args.ItemIndex >= ViewModel.Rows.Count - 40)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }
}
