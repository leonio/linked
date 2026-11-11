using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Actions;
using Guit.Core.Models;
using Guit.Core.Services;
using Guit.Core.Text;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>The log of one job, as lines for a virtualised list.</summary>
public sealed partial class JobLogViewModel(IActionsApi actions, INavigator navigator, ILauncher launcher) : ObservableObject
{
    /// <summary>Logs can run to hundreds of thousands of lines; the end is where failures are.</summary>
    public const int MaxLines = 5000;

    private JobLogLink _link;

    /// <summary>The job name.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>"owner/repo".</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    /// <summary>The cleaned log lines. Replaced as a whole when a log arrives (adding 5,000 rows one by one would crawl).</summary>
    [ObservableProperty]
    public partial ObservableCollection<string> Lines { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Says when only the end of a long log is shown. Null otherwise.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Loads the log named by a navigation argument (see <see cref="JobLogLink"/>).</summary>
    [RelayCommand]
    private async Task LoadAsync(string? argument, CancellationToken cancellationToken)
    {
        if (!JobLogLink.TryParseArgument(argument, out _link))
        {
            ErrorMessage = "This link is not valid.";
            return;
        }

        Title = _link.JobName;
        Subtitle = $"{_link.Owner}/{_link.Repo}";
        await ReloadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_link.Owner is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        Notice = null;
        try
        {
            string raw = await actions.GetJobLogsAsync(_link.Owner, _link.Repo, _link.JobId, cancellationToken);
            string[] lines = ActionsLog.ToLines(raw, MaxLines, out bool truncated);
            Lines = new ObservableCollection<string>(lines);
            Notice = truncated ? $"Showing the last {MaxLines:N0} lines. Open on GitHub for the full log." : null;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // GitHub only publishes a job's log once the job has finished (and deletes it after the retention period).
            ErrorMessage = "No log yet. GitHub publishes a job's log when the job finishes.";
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

    [RelayCommand]
    private Task OpenInBrowserAsync()
        => _link.Owner is null
            ? Task.CompletedTask
            : launcher.OpenAsync(new Uri($"https://github.com/{_link.Owner}/{_link.Repo}/actions"));

    [RelayCommand]
    private void GoBack() => navigator.GoBack();
}
