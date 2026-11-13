using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Search;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>The member's open pull requests, most recently updated first. Uses the search API, so it never polls.</summary>
public sealed partial class PullRequestsViewModel(
    ISearchApi search,
    INavigator navigator,
    ILauncher launcher,
    TimeProvider time) : ObservableObject
{
    private const int PageSize = 50;

    private int _page = 1;
    private int _generation;

    public ObservableCollection<PullRequestItem> Items { get; } = [];

    /// <summary>Changing the scope reloads the list from the first page.</summary>
    [ObservableProperty]
    public partial PullRequestScope Scope { get; set; }

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

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Items.Count == 0;

    /// <summary>Replaces the list with the first page. A newer load supersedes one still running.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        int generation = ++_generation;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IssueSearchResult result = await SearchPageAsync(1, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            Items.Clear();
            Append(result.Items);
            _page = 1;
            HasMore = result.Items.Length == PageSize;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase ex)
        {
            if (generation == _generation)
            {
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
        if (!HasMore || IsLoading || IsLoadingMore)
        {
            return;
        }

        int generation = _generation;
        IsLoadingMore = true;
        ErrorMessage = null;
        try
        {
            IssueSearchResult result = await SearchPageAsync(_page + 1, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            Append(result.Items);
            _page++;
            HasMore = result.Items.Length == PageSize;
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

    /// <summary>Opens the pull request's conversation in the app (or on github.com if it cannot be addressed).</summary>
    [RelayCommand]
    private Task OpenAsync(PullRequestItem? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        if (item.Argument is null)
        {
            return launcher.OpenAsync(new Uri(item.WebUrl));
        }

        navigator.Navigate(Route.Conversation, item.Argument);
        return Task.CompletedTask;
    }

    partial void OnScopeChanged(PullRequestScope value) => LoadCommand.Execute(null);

    private Task<IssueSearchResult> SearchPageAsync(int page, CancellationToken cancellationToken)
        => search.SearchIssuesAsync(QueryFor(Scope), "updated", "desc", page, PageSize, cancellationToken);

    private static string QueryFor(PullRequestScope scope) => scope switch
    {
        PullRequestScope.Created => "is:open is:pr author:@me archived:false",
        PullRequestScope.Assigned => "is:open is:pr assignee:@me archived:false",
        _ => "is:open is:pr review-requested:@me archived:false",
    };

    private void Append(IssueSearchItem[] page)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (IssueSearchItem item in page)
        {
            Items.Add(PullRequestItem.From(item, now));
        }
    }
}
