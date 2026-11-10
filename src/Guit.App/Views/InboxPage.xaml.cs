using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

public sealed partial class InboxPage : Page
{
    public InboxPage()
    {
        ViewModel = App.Services.GetRequiredService<InboxViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
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
}
