using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Actions;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>One workflow run: a header and its jobs.</summary>
public sealed partial class JobsViewModel(
    IActionsApi actions,
    INavigator navigator,
    ILauncher launcher,
    IUiDispatcher dispatcher,
    TimeProvider time) : ObservableObject
{
    // One request: a run with more than 100 jobs shows its first 100.
    private const int JobCount = 100;

    private static readonly TimeSpan FollowPeriod = TimeSpan.FromSeconds(5);

    // After a re-run GitHub needs a moment before the run reads as queued; keep checking this many times.
    private const int TicksAfterRerun = 24;

    private RunLink _link;
    private ITimer? _followTimer;
    private int _forcedTicks;
    private string _signature = string.Empty;

    public ObservableCollection<JobItem> Jobs { get; } = [];

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>"owner/repo · CI #12 · push · main".</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    /// <summary>One of the <see cref="RunStatus"/> constants.</summary>
    [ObservableProperty]
    public partial string StatusKind { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusGlyph { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RerunCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RerunCommand))]
    public partial bool IsRerunning { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>A neutral status line, for example while waiting for a re-run to start. Null when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>True while the run is queued or in progress; the page then updates itself.</summary>
    public bool IsLive => StatusKind is RunStatus.Running or RunStatus.Queued;

    private bool CanRerun => !IsLoading && !IsRerunning && _link.Owner is not null;

    /// <summary>Loads the run named by a navigation argument ("owner/repo@123").</summary>
    [RelayCommand]
    private async Task LoadAsync(string? argument, CancellationToken cancellationToken)
    {
        if (!RunLink.TryParseArgument(argument, out _link))
        {
            ErrorMessage = "This link is not valid.";
            return;
        }

        await ReloadAsync(cancellationToken);
    }

    /// <summary>Fetches the run and its jobs again.</summary>
    [RelayCommand]
    private Task ReloadAsync(CancellationToken cancellationToken) => FetchAsync(quiet: false, cancellationToken);

    /// <summary>
    /// Loads the run and its jobs. A quiet fetch (the background follow) shows no spinner, keeps the
    /// rows when nothing changed, and does not report failures.
    /// </summary>
    private async Task FetchAsync(bool quiet, CancellationToken cancellationToken)
    {
        if (_link.Owner is null || IsLoading)
        {
            return;
        }

        if (!quiet)
        {
            IsLoading = true;
            ErrorMessage = null;
        }

        try
        {
            Task<WorkflowRun> runTask = actions.GetRunAsync(_link.Owner, _link.Repo, _link.RunId, cancellationToken);
            Task<JobList> jobsTask = actions.ListJobsAsync(_link.Owner, _link.Repo, _link.RunId, 1, JobCount, cancellationToken);
            await Task.WhenAll(runTask, jobsTask);
            WorkflowRun run = await runTask;
            JobList list = await jobsTask;

            RunItem summary = RunItem.From(run, time.GetUtcNow());
            Title = summary.Title;
            Subtitle = string.Create(CultureInfo.InvariantCulture, $"{_link.Owner}/{_link.Repo} \u00B7 {summary.Detail}");
            StatusKind = summary.StatusKind;
            StatusGlyph = summary.StatusGlyph;
            OnPropertyChanged(nameof(IsLive));
            if (IsLive)
            {
                Notice = null;
            }

            List<JobItem> rows = [];
            foreach (Job job in list.Jobs)
            {
                rows.Add(JobItem.From(job));
                foreach (JobStep step in job.Steps ?? [])
                {
                    rows.Add(JobItem.FromStep(job, step));
                }
            }

            // Rebuild the list only when something a row shows has changed, so a quiet follow does not flicker.
            string signature = Describe(rows);
            if (signature != _signature)
            {
                _signature = signature;
                Jobs.Clear();
                foreach (JobItem row in rows)
                {
                    Jobs.Add(row);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase ex)
        {
            if (!quiet)
            {
                ErrorMessage = ApiErrors.Describe(ex);
            }
        }
        finally
        {
            if (!quiet)
            {
                IsLoading = false;
            }
        }
    }

    private static string Describe(List<JobItem> rows)
    {
        System.Text.StringBuilder text = new();
        foreach (JobItem row in rows)
        {
            text.Append(row.JobId).Append(row.IsStep ? '>' : '#').Append(row.Name).Append('|').Append(row.StatusKind).Append('|').Append(row.Duration).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Starts following the run: while it is live the page refreshes every few seconds. Call when the page is shown.</summary>
    public void StartFollowing()
        => _followTimer ??= time.CreateTimer(
            static state => ((JobsViewModel)state!).OnFollowTick(),
            this,
            FollowPeriod,
            FollowPeriod);

    /// <summary>Stops following. Call when the page is left.</summary>
    public void StopFollowing()
    {
        _followTimer?.Dispose();
        _followTimer = null;
    }

    // Timer callbacks arrive on a thread-pool thread; view model state belongs to the UI thread.
    private void OnFollowTick() => dispatcher.Post(() => _ = FollowAsync());

    /// <summary>One follow step. Does nothing unless the run is live or a re-run was just requested.</summary>
    internal async Task FollowAsync()
    {
        if (!IsLive)
        {
            if (_forcedTicks <= 0)
            {
                return;
            }

            if (--_forcedTicks == 0)
            {
                Notice = null;
            }
        }

        await FetchAsync(quiet: true, CancellationToken.None);
    }

    /// <summary>Asks GitHub to run every job of this run again, then reloads to show the new attempt.</summary>
    [RelayCommand(CanExecute = nameof(CanRerun))]
    private async Task RerunAsync(CancellationToken cancellationToken)
    {
        IsRerunning = true;
        ErrorMessage = null;
        try
        {
            await actions.RerunAsync(_link.Owner, _link.Repo, _link.RunId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ApiExceptionBase ex)
        {
            ErrorMessage = ApiErrors.Describe(ex);
            return;
        }
        finally
        {
            IsRerunning = false;
        }

        // GitHub accepts the request before the run changes state, so say so and keep checking for a while.
        Notice = "Re-run requested. Waiting for GitHub to start it\u2026";
        _forcedTicks = TicksAfterRerun;
        await FetchAsync(quiet: false, cancellationToken);
    }

    /// <summary>Shows the log of a job (or of the job a step belongs to).</summary>
    [RelayCommand]
    private void OpenJob(JobItem? job)
    {
        if (job is not null && _link.Owner is not null)
        {
            navigator.Navigate(Route.JobLog, new JobLogLink(_link.Owner, _link.Repo, job.JobId, job.JobName).ToArgument());
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync()
        => _link.Owner is null ? Task.CompletedTask : launcher.OpenAsync(_link.ToWebUri());

    [RelayCommand]
    private void GoBack() => navigator.GoBack();
}
