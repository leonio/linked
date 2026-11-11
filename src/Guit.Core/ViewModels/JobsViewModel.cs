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
    TimeProvider time) : ObservableObject
{
    // One request: a run with more than 100 jobs shows its first 100.
    private const int JobCount = 100;

    private RunLink _link;

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

    /// <summary>Fetches the run and its jobs again, for example to follow a run in progress.</summary>
    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_link.Owner is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Task<WorkflowRun> runTask = actions.GetRunAsync(_link.Owner, _link.Repo, _link.RunId, cancellationToken);
            Task<JobList> jobsTask = actions.ListJobsAsync(_link.Owner, _link.Repo, _link.RunId, 1, JobCount, cancellationToken);
            await Task.WhenAll(runTask, jobsTask);
            WorkflowRun run = await runTask;
            JobList list = await jobsTask;

            RunItem summary = RunItem.From(run, time.GetUtcNow());
            Title = summary.Title;
            Subtitle = string.Create(CultureInfo.InvariantCulture, $"{_link.Owner}/{_link.Repo} · {summary.Detail}");
            StatusKind = summary.StatusKind;
            StatusGlyph = summary.StatusGlyph;

            Jobs.Clear();
            foreach (Job job in list.Jobs)
            {
                Jobs.Add(JobItem.From(job));
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

        await ReloadAsync(cancellationToken);
    }

    /// <summary>Job logs are read on github.com for now.</summary>
    [RelayCommand]
    private Task OpenJobAsync(JobItem? job)
        => job is null ? Task.CompletedTask : launcher.OpenAsync(new Uri(job.WebUrl));

    [RelayCommand]
    private Task OpenInBrowserAsync()
        => _link.Owner is null ? Task.CompletedTask : launcher.OpenAsync(_link.ToWebUri());

    [RelayCommand]
    private void GoBack() => navigator.GoBack();
}
