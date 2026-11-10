using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Issues;
using Guit.Api.Reactions;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>One issue or pull request: a header, the opening post, and the comments oldest first.</summary>
public sealed partial class ConversationViewModel(
    IIssuesApi issues,
    IReactionsApi reactions,
    INavigator navigator,
    ILauncher launcher,
    TimeProvider time) : ObservableObject
{
    private const int PageSize = 50;

    private SubjectLink _link;
    private int _page = 1;

    public ObservableCollection<CommentItem> Comments { get; } = [];

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>"owner/repo #12".</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    /// <summary>"open" or "closed" (pull requests refine this later).</summary>
    [ObservableProperty]
    public partial string State { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPullRequest { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>The reply being written. Bound two-way to the reply box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; private set; }

    private bool CanSend => !IsSending && !IsLoading && _link.Owner is not null && !string.IsNullOrWhiteSpace(Draft);

    /// <summary>Loads the conversation named by a navigation argument ("owner/repo#12" or "owner/repo!12").</summary>
    [RelayCommand]
    private async Task LoadAsync(string? argument, CancellationToken cancellationToken)
    {
        if (!SubjectLink.TryParseArgument(argument, out _link))
        {
            ErrorMessage = "This link is not valid.";
            return;
        }

        IsPullRequest = _link.IsPullRequest;
        Subtitle = $"{_link.Owner}/{_link.Repo} #{_link.Number}";
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // The two requests do not depend on each other, so send them together.
            Task<Issue> issueTask = issues.GetAsync(_link.Owner, _link.Repo, _link.Number, cancellationToken);
            Task<IssueComment[]> commentsTask = issues.ListCommentsAsync(_link.Owner, _link.Repo, _link.Number, 1, PageSize, cancellationToken);
            await Task.WhenAll(issueTask, commentsTask);
            Issue issue = await issueTask;
            IssueComment[] page = await commentsTask;

            Title = issue.Title;
            State = issue.State;
            IsPullRequest = issue.PullRequest is not null;
            Comments.Clear();
            Comments.Add(CommentItem.FromIssue(issue, time.GetUtcNow()));
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
        }
    }

    /// <summary>Appends the next page of comments. Does nothing when there is no more, or a load is running.</summary>
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
            IssueComment[] page = await issues.ListCommentsAsync(_link.Owner, _link.Repo, _link.Number, _page + 1, PageSize, cancellationToken);
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

    /// <summary>Posts <see cref="Draft"/> as a comment. The draft is kept if GitHub refuses it.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        IsSending = true;
        ErrorMessage = null;
        try
        {
            IssueComment created = await issues.CreateCommentAsync(
                _link.Owner, _link.Repo, _link.Number, new CreateCommentRequest(Draft.Trim()), cancellationToken);
            Comments.Add(CommentItem.From(created, time.GetUtcNow()));
            Draft = string.Empty;
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
            IsSending = false;
        }
    }

    /// <summary>
    /// Adds the member's reaction to a post. The count goes up at once and comes back down if GitHub refuses.
    /// (If the member had already reacted this way GitHub changes nothing, and the next load corrects the count.)
    /// </summary>
    /// <param name="item">The post to react to.</param>
    /// <param name="content">One of <see cref="ReactionContent.All"/>.</param>
    public async Task ReactAsync(CommentItem item, string content)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.AdjustReaction(content, +1);
        try
        {
            CreateReactionRequest request = new(content);
            await (item.IsOpeningPost
                ? reactions.CreateForIssueAsync(_link.Owner, _link.Repo, _link.Number, request, CancellationToken.None)
                : reactions.CreateForIssueCommentAsync(_link.Owner, _link.Repo, item.Id, request, CancellationToken.None));
        }
        catch (ApiExceptionBase ex)
        {
            item.AdjustReaction(content, -1);
            ErrorMessage = ApiErrors.Describe(ex);
        }
    }

    [RelayCommand]
    private void GoBack() => navigator.GoBack();

    /// <summary>Shows this conversation on github.com.</summary>
    [RelayCommand]
    private Task OpenInBrowserAsync()
        => _link.Owner is null ? Task.CompletedTask : launcher.OpenAsync(_link.ToWebUri());

    private void Append(IssueComment[] page)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (IssueComment comment in page)
        {
            Comments.Add(CommentItem.From(comment, now));
        }
    }
}
