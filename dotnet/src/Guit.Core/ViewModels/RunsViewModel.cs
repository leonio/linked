using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Actions;
using Guit.Api.Repos;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>
/// Workflow runs of one repository, newest first. Registered as a singleton so the chosen
/// repository is remembered while the app is open.
/// </summary>
public sealed partial class RunsViewModel(
    IReposApi repos,
    IActionsApi actions,
    INavigator navigator,
    TimeProvider time) : ObservableObject
{
    private const int RepositoryCount = 50;
    private const int PageSize = 30;

    private int _page = 1;
    private int _generation;

    /// <summary>"owner/name" of the member's most recently pushed repositories.</summary>
    public ObservableCollection<string> Repositories { get; } = [];

    public ObservableCollection<RunItem> Runs { get; } = [];

    /// <summary>Choosing a repository loads its runs.</summary>
    [ObservableProperty]
    public partial string? SelectedRepository { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; private set; }

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Runs.Count == 0;

    /// <summary>Fills the repository list once and selects the first entry, which loads its runs.</summary>
    [RelayCommand]
    private async Task LoadRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (Repositories.Count > 0)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            RepositorySummary[] mine = await repos.ListMineAsync("pushed", 1, RepositoryCount, cancellationToken);
            foreach (RepositorySummary repository in mine)
            {
                Repositories.Add(repository.FullName);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase ex)
        {
            ErrorMessage = ApiErrors.Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }

        if (SelectedRepository is null && Repositories.Count > 0)
        {
            SelectedRepository = Repositories[0];
        }
    }

    /// <summary>Replaces the list with the first page of runs for the selected repository.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        int generation = ++_generation;
        if (!RunLink.TryCreate(SelectedRepository, 1, out RunLink repository))
        {
            Runs.Clear();
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            WorkflowRunList list = await actions.ListRunsAsync(repository.Owner, repository.Repo, 1, PageSize, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            Runs.Clear();
            Append(list.WorkflowRuns);
            _page = 1;
            HasMore = list.WorkflowRuns.Length == PageSize;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase ex)
        {
            if (generation == _generation)
            {
                Runs.Clear();
                ErrorMessage = ApiErrors.Describe(ex);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (!HasMore || IsLoading || IsLoadingMore || !RunLink.TryCreate(SelectedRepository, 1, out RunLink repository))
        {
            return;
        }

        int generation = _generation;
        IsLoadingMore = true;
        ErrorMessage = null;
        try
        {
            WorkflowRunList list = await actions.ListRunsAsync(repository.Owner, repository.Repo, _page + 1, PageSize, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            Append(list.WorkflowRuns);
            _page++;
            HasMore = list.WorkflowRuns.Length == PageSize;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase ex)
        {
            ErrorMessage = ApiErrors.Describe(ex);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>Shows the jobs of a run.</summary>
    [RelayCommand]
    private void Open(RunItem? item)
    {
        if (item is not null && RunLink.TryCreate(SelectedRepository, item.Id, out RunLink link))
        {
            navigator.Navigate(Route.RunDetail, link.ToArgument());
        }
    }

    partial void OnSelectedRepositoryChanged(string? value) => LoadCommand.Execute(null);

    private void Append(WorkflowRun[] page)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (WorkflowRun run in page)
        {
            Runs.Add(RunItem.From(run, now));
        }
    }
}
