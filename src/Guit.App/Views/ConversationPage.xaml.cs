using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

/// <summary>One issue or pull request. Not cached: each conversation is a new page with a new view model.</summary>
public sealed partial class ConversationPage : Page
{
    public ConversationPage()
    {
        ViewModel = App.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();
    }

    public ConversationViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Going back to the inbox and forward again reuses nothing, so only a fresh page loads.
        if (ViewModel.Comments.Count == 0)
        {
            ViewModel.LoadCommand.Execute(e.Parameter as string);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemIndex >= ViewModel.Comments.Count - 10)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }
}
