using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Notifications;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>The inbox: the member's notification threads, newest first.</summary>
public sealed partial class InboxViewModel(INotificationsApi notifications, TimeProvider time) : ObservableObject
{
    private const int PageSize = 50;

    private int _page = 1;
    private int _generation;

    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Changing the filter reloads the list from the first page.</summary>
    [ObservableProperty]
    public partial InboxFilter Filter { get; set; }

    /// <summary>True while the last page that arrived was full, so there may be another.</summary>
    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    /// <summary>True when a load finished without error and there is nothing to show.</summary>
    public bool IsEmpty => !IsLoading && ErrorMessage is null && Items.Count == 0;

    /// <summary>
    /// Replaces the list with the first page. A newer load (for example after a filter change)
    /// supersedes one that is still running: the older reply is discarded.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        int generation = ++_generation;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            NotificationThread[] page = await ListPageAsync(1, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            Items.Clear();
            Append(page);
            _page = 1;
            HasMore = page.Length == PageSize;
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

    /// <summary>Appends the next page. Does nothing when there is no more, or a load is already running.</summary>
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
            NotificationThread[] page = await ListPageAsync(_page + 1, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            Append(page);
            _page++;
            HasMore = page.Length == PageSize;
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

    partial void OnFilterChanged(InboxFilter value) => LoadCommand.Execute(null);

    private Task<NotificationThread[]> ListPageAsync(int page, CancellationToken cancellationToken)
        => notifications.ListAsync(
            all: Filter == InboxFilter.All,
            participating: Filter == InboxFilter.Participating,
            page,
            PageSize,
            cancellationToken);

    private void Append(NotificationThread[] page)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (NotificationThread thread in page)
        {
            Items.Add(NotificationItem.From(thread, now));
        }
    }
}
