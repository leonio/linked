using System.ComponentModel;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guit.App.Views;

/// <summary>The log of one job. Not cached: each log is a new page with a new view model.</summary>
public sealed partial class JobLogPage : Page
{
    public JobLogPage()
    {
        ViewModel = App.Services.GetRequiredService<JobLogViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public JobLogViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(e.Parameter as string);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A log is read from its end: that is where a failure is reported.
        if (e.PropertyName == nameof(JobLogViewModel.Lines) && ViewModel.Lines.Count > 0)
        {
            string last = ViewModel.Lines[^1];
            DispatcherQueue.TryEnqueue(() => List.ScrollIntoView(last));
        }
    }
}
