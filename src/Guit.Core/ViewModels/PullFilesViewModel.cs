using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Pulls;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>The "Files changed" view of a pull request: every changed file and its diff as one flat list of rows.</summary>
public sealed partial class PullFilesViewModel(IPullsApi pulls, INavigator navigator, ILauncher launcher) : ObservableObject
{
    /// <summary>Files fetched per request (GitHub's maximum).</summary>
    private const int PageSize = 100;

    private SubjectLink _link;
    private int _page = 1;
    private int _fileCount;

    /// <summary>
    /// The rows. Replaced as a whole when the first page arrives: a large diff is thousands of rows,
    /// and adding them one at a time to a bound collection would crawl.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial ObservableCollection<DiffRow> Rows { get; private set; } = [];

    /// <summary>"owner/repo #12 · 5 files".</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    /// <summary>True while the last page of files was full, so there may be more files.</summary>
    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; private set; }

    public bool IsEmpty => !IsLoading && ErrorMessage is null && Rows.Count == 0;

    /// <summary>Loads the files of the pull request named by a navigation argument ("owner/repo!12").</summary>
    [RelayCommand]
    private async Task LoadAsync(string? argument, CancellationToken cancellationToken)
    {
        if (!SubjectLink.TryParseArgument(argument, out _link))
        {
            ErrorMessage = "This link is not valid.";
            return;
        }

        Subtitle = $"{_link.Owner}/{_link.Repo} #{_link.Number}";
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            PullFile[] files = await pulls.ListFilesAsync(_link.Owner, _link.Repo, _link.Number, 1, PageSize, cancellationToken);
            List<DiffRow> rows = [];
            foreach (PullFile file in files)
            {
                DiffRow.AppendFile(rows, file);
            }

            Rows = new ObservableCollection<DiffRow>(rows);
            _page = 1;
            _fileCount = files.Length;
            HasMore = files.Length == PageSize;
            UpdateSubtitle();
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

    /// <summary>Appends the next 100 files. Does nothing when there are no more, or a load is running.</summary>
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
            PullFile[] files = await pulls.ListFilesAsync(_link.Owner, _link.Repo, _link.Number, _page + 1, PageSize, cancellationToken);
            List<DiffRow> rows = [];
            foreach (PullFile file in files)
            {
                DiffRow.AppendFile(rows, file);
            }

            foreach (DiffRow row in rows)
            {
                Rows.Add(row);
            }

            _page++;
            _fileCount += files.Length;
            HasMore = files.Length == PageSize;
            UpdateSubtitle();
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

    [RelayCommand]
    private Task OpenInBrowserAsync()
        => _link.Owner is null ? Task.CompletedTask : launcher.OpenAsync(new Uri(_link.ToWebUri().AbsoluteUri + "/files"));

    [RelayCommand]
    private void GoBack() => navigator.GoBack();

    private void UpdateSubtitle()
        => Subtitle = $"{_link.Owner}/{_link.Repo} #{_link.Number} · {_fileCount}{(HasMore ? "+" : string.Empty)} {(_fileCount == 1 && !HasMore ? "file" : "files")}";
}
