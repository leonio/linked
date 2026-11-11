using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Notifications;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>The inbox: the member's notification threads, newest first.</summary>
public sealed partial class InboxViewModel(
    INotificationsApi notifications,
    INavigator navigator,
    ILauncher launcher,
    IUiDispatcher dispatcher,
    TimeProvider time) : ObservableObject
{
    private const int PageSize = 50;

    private static readonly TimeSpan MinimumPollPeriod = TimeSpan.FromSeconds(60);

    // Rows the list is meant to hold before the member has to scroll; a kind filter tops up to this.
    private const int ComfortableRowCount = 15;
    private const int MaxTopUpPages = 4;

    // Everything loaded so far. Items is the part of it that matches Kind.
    private readonly List<NotificationItem> _all = [];

    private int _page = 1;
    private int _generation;
    private ITimer? _pollTimer;
    private TimeSpan _pollPeriod = MinimumPollPeriod;

    // Last-Modified of the newest poll reply for the current filter; sent back as If-Modified-Since.
    private string? _lastModified;

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

    /// <summary>
    /// Shows only one kind of notification. GitHub cannot filter by kind, so this filters what has been
    /// loaded and fetches a few more pages when too little is left to fill the list.
    /// </summary>
    [ObservableProperty]
    public partial InboxKind Kind { get; set; }

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
        _lastModified = null;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            NotificationThread[] page = await ListPageAsync(1, cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            _all.Clear();
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

        if (generation == _generation)
        {
            await TopUpAsync(cancellationToken);
        }
    }

    /// <summary>
    /// With a kind filter on, one page may hold few matching rows. Fetch more pages (a bounded number)
    /// until the list is comfortably full, so the member is not left with a near-empty list that could scroll.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task TopUpAsync(CancellationToken cancellationToken)
    {
        for (int i = 0; i < MaxTopUpPages && HasMore && Items.Count < ComfortableRowCount && ErrorMessage is null; i++)
        {
            int before = _page;
            await LoadMoreAsync(cancellationToken);
            if (_page == before)
            {
                // Nothing was fetched (another load is running, or it failed): do not spin.
                return;
            }
        }
    }

    /// <summary>
    /// Fetches the first page again and replaces the list only if something changed,
    /// so an unchanged inbox keeps its rows and scroll position. Shows no spinner.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsLoading || IsLoadingMore)
        {
            return;
        }

        int generation = _generation;
        try
        {
            // A conditional request: when nothing changed GitHub answers 304, which is free against the rate limit.
            using ApiResponse<NotificationThread[]> reply = await notifications.PollAsync(
                all: Filter == InboxFilter.All,
                participating: Filter == InboxFilter.Participating,
                1,
                PageSize,
                _lastModified,
                cancellationToken);
            if (generation != _generation)
            {
                return;
            }

            FollowPollInterval(reply);
            if (reply.StatusCode != HttpStatusCode.OK || reply.Content is not { } page)
            {
                // 304 Not Modified, or a failure: a background refresh that fails says nothing.
                return;
            }

            _lastModified = reply.ContentHeaders?.LastModified?.ToString("R", CultureInfo.InvariantCulture) ?? _lastModified;
            if (IsSameAsFirstPage(page))
            {
                return;
            }

            ErrorMessage = null;
            _all.Clear();
            Items.Clear();
            Append(page);
            _page = 1;
            HasMore = page.Length == PageSize;
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase)
        {
            // A background refresh that fails says nothing; the next one, or a manual load, will report it.
        }
    }

    /// <summary>Starts refreshing in the background. Call when the page is shown.</summary>
    public void StartPolling()
        => _pollTimer ??= time.CreateTimer(
            static state => ((InboxViewModel)state!).OnPollTick(),
            this,
            _pollPeriod,
            _pollPeriod);

    /// <summary>Stops the background refresh. Call when the page is left.</summary>
    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    /// <summary>GitHub says how often it wants to be polled (X-Poll-Interval, in seconds). Never poll faster than that.</summary>
    private void FollowPollInterval(ApiResponse<NotificationThread[]> reply)
    {
        if (reply.Headers is { } headers
            && headers.TryGetValues("X-Poll-Interval", out IEnumerable<string>? values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            TimeSpan wanted = TimeSpan.FromSeconds(Math.Max(seconds, MinimumPollPeriod.TotalSeconds));
            if (wanted != _pollPeriod)
            {
                _pollPeriod = wanted;
                _pollTimer?.Change(wanted, wanted);
            }
        }
    }

    /// <summary>The current time between background refreshes.</summary>
    internal TimeSpan PollPeriod => _pollPeriod;

    // Timer callbacks arrive on a thread-pool thread; view model state belongs to the UI thread.
    private void OnPollTick() => dispatcher.Post(() => RefreshCommand.Execute(null));

    private bool IsSameAsFirstPage(NotificationThread[] page)
    {
        if (page.Length > _all.Count || (_all.Count <= PageSize && _all.Count != page.Length))
        {
            return false;
        }

        for (int i = 0; i < page.Length; i++)
        {
            if (_all[i].Id != page[i].Id || _all[i].IsUnread != page[i].Unread)
            {
                return false;
            }
        }

        return true;
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

    /// <summary>Marks a thread as read. The row updates at once and is put back if GitHub refuses.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task MarkAsReadAsync(NotificationItem? item, CancellationToken cancellationToken)
    {
        if (item is not { IsUnread: true })
        {
            return;
        }

        item.IsUnread = false;
        try
        {
            await notifications.MarkAsReadAsync(item.Id, cancellationToken);
            if (Filter == InboxFilter.Unread)
            {
                _all.Remove(item);
                Items.Remove(item);
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
        catch (OperationCanceledException)
        {
            item.IsUnread = true;
        }
        catch (ApiExceptionBase ex)
        {
            item.IsUnread = true;
            ErrorMessage = ApiErrors.Describe(ex);
        }
    }

    /// <summary>Removes a thread from the inbox. The row goes at once and returns to its place if GitHub refuses.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task MarkAsDoneAsync(NotificationItem? item, CancellationToken cancellationToken)
    {
        int index = item is null ? -1 : Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        int indexInAll = _all.IndexOf(item!);
        _all.Remove(item!);
        Items.RemoveAt(index);
        OnPropertyChanged(nameof(IsEmpty));
        try
        {
            await notifications.MarkAsDoneAsync(item!.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Restore(index, indexInAll, item!);
        }
        catch (ApiExceptionBase ex)
        {
            Restore(index, indexInAll, item!);
            ErrorMessage = ApiErrors.Describe(ex);
        }
    }

    /// <summary>
    /// Opens a row and marks it as read. Issues and pull requests open in the app;
    /// other kinds (releases, check suites...) open the repository on github.com.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenAsync(NotificationItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (SubjectLink.TryParse(item.SubjectUrl, out SubjectLink link))
        {
            navigator.Navigate(Route.Conversation, link.ToArgument());
        }
        else
        {
            await launcher.OpenAsync(new Uri("https://github.com/" + item.Repository));
        }

        await MarkAsReadAsync(item, cancellationToken);
    }

    private void Restore(int index, int indexInAll, NotificationItem item)
    {
        _all.Insert(Math.Clamp(indexInAll, 0, _all.Count), item);
        Items.Insert(Math.Min(index, Items.Count), item);
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnFilterChanged(InboxFilter value) => LoadCommand.Execute(null);

    partial void OnKindChanged(InboxKind value)
    {
        Items.Clear();
        foreach (NotificationItem item in _all)
        {
            if (Matches(item))
            {
                Items.Add(item);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        TopUpCommand.Execute(null);
    }

    private bool Matches(NotificationItem item) => Kind switch
    {
        InboxKind.PullRequests => item.Kind == "PullRequest",
        InboxKind.Issues => item.Kind == "Issue",
        InboxKind.Releases => item.Kind == "Release",
        InboxKind.Discussions => item.Kind == "Discussion",
        InboxKind.Checks => item.Kind is "CheckSuite" or "WorkflowRun",
        InboxKind.Other => item.Kind is not ("PullRequest" or "Issue" or "Release" or "Discussion" or "CheckSuite" or "WorkflowRun"),
        _ => true,
    };

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
            NotificationItem item = NotificationItem.From(thread, now);
            _all.Add(item);
            if (Matches(item))
            {
                Items.Add(item);
            }
        }
    }
}
