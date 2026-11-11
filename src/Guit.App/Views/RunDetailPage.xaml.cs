using Guit.Core.Models;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

/// <summary>One workflow run and its jobs. Not cached: each run is a new page with a new view model.</summary>
public sealed partial class RunDetailPage : Page
{
    public RunDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<JobsViewModel>();
        InitializeComponent();
    }

    public JobsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(e.Parameter as string);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
        => ViewModel.OpenJobCommand.Execute(e.ClickedItem as JobItem);
}
