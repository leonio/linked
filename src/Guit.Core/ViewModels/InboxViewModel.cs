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

    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>True while the last page that arrived was full, so there may be another.</summary>
    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    /// <summary>True when a load finished without error and there is nothing to show.</summary>
    public bool IsEmpty => !IsLoading && ErrorMessage is null && Items.Count == 0;

    /// <summary>Replaces the list with the first page.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            NotificationThread[] page = await notifications.ListAsync(all: false, participating: false, page: 1, PageSize, cancellationToken);
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
            ErrorMessage = ApiErrors.Describe(ex);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
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

        IsLoadingMore = true;
        ErrorMessage = null;
        try
        {
            NotificationThread[] page = await notifications.ListAsync(all: false, participating: false, _page + 1, PageSize, cancellationToken);
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

    private void Append(NotificationThread[] page)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (NotificationThread thread in page)
        {
            Items.Add(NotificationItem.From(thread, now));
        }
    }
}
