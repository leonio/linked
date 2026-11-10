using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Guit.Api.Issues;
using Guit.Api.Pulls;
using Guit.Api.Reactions;
using Guit.Core.Models;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>One issue or pull request: a header, the opening post, and the comments oldest first.</summary>
public sealed partial class ConversationViewModel(
    IIssuesApi issues,
    IPullsApi pulls,
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

    /// <summary>"open" or "closed"; for pull requests also "merged" or "draft".</summary>
    [ObservableProperty]
    public partial string State { get; private set; } = string.Empty;

    /// <summary>"feature → main" for a pull request; empty otherwise or when the details could not be loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPullDetails))]
    public partial string Branches { get; private set; } = string.Empty;

    /// <summary>"+120 −8 in 5 files" for a pull request.</summary>
    [ObservableProperty]
    public partial string DiffSummary { get; private set; } = string.Empty;

    public bool HasPullDetails => Branches.Length > 0;

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
            Task<PullRequest?> pullTask = _link.IsPullRequest ? TryGetPullAsync(cancellationToken) : Task.FromResult<PullRequest?>(null);
            await Task.WhenAll(issueTask, commentsTask, pullTask);
            Issue issue = await issueTask;
            IssueComment[] page = await commentsTask;
            PullRequest? pull = await pullTask;

            Title = issue.Title;
            State = DescribeState(issue, pull);
            IsPullRequest = issue.PullRequest is not null;
            Branches = pull is null ? string.Empty : DescribeBranches(pull);
            DiffSummary = pull is null ? string.Empty : DescribeDiff(pull);
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

    // The header extras are optional: a failure here must not cost the member the whole conversation.
    private async Task<PullRequest?> TryGetPullAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await pulls.GetAsync(_link.Owner, _link.Repo, _link.Number, cancellationToken);
        }
        catch (ApiExceptionBase)
        {
            return null;
        }
    }

    private static string DescribeState(Issue issue, PullRequest? pull) => pull switch
    {
        { Merged: true } => "merged",
        { State: "closed" } => "closed",
        { Draft: true } => "draft",
        _ => issue.State,
    };

    private static string DescribeBranches(PullRequest pull)
    {
        // Across forks the bare branch name is ambiguous, so show "owner:branch" for the source.
        bool sameRepository = OwnerOf(pull.Head.Label) == OwnerOf(pull.Base.Label);
        return $"{(sameRepository ? pull.Head.Ref : pull.Head.Label)} \u2192 {pull.Base.Ref}";
    }

    private static string OwnerOf(string label)
    {
        int colon = label.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? string.Empty : label[..colon];
    }

    private static string DescribeDiff(PullRequest pull)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"+{pull.Additions} \u2212{pull.Deletions} in {pull.ChangedFiles} {(pull.ChangedFiles == 1 ? "file" : "files")}");

    private void Append(IssueComment[] page)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (IssueComment comment in page)
        {
            Comments.Add(CommentItem.From(comment, now));
        }
    }
}
