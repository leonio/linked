# Phase 3 - Conversations (the messaging)

A conversation is one issue or pull request: a header, the opening post, and the comments, with a box to reply.
On GitHub every pull request is also an issue, so the **Issues API serves comments for both**.

All comment text is requested as plain text until card 3.9: add
`[Headers("Accept: application/vnd.github.full+json")]` to the methods in this phase so replies include `body_text`.

```
Guit.Api    Issues/IIssuesApi, Issue, IssueComment, CreateCommentRequest   3.1, 3.2
              Reactions/IReactionsApi                                        3.6
              Pulls/IPullsApi, PullRequest                                   3.7
              Search/ISearchApi                                              3.8
Guit.Core   Models/CommentItem, ViewModels/ConversationViewModel           3.3, 3.5, 3.6, 3.7
              ViewModels/PullRequestsViewModel                               3.8
Guit.App    Views/ConversationPage.xaml, Views/PullRequestsPage.xaml       3.4, 3.5, 3.8
```

---

## 3.1 Issues API: get issue, list comments

**Layer** Api - **Skills** `api-endpoint`, `aot-rules` - **Depends on** nothing
**Copy** `src/Guit.Api/Notifications/`.

Look up: `python3 tools/ghapi.py op issues/get` and `python3 tools/ghapi.py op issues/list-comments`.

**Build** `src/Guit.Api/Issues/`
- `IIssuesApi.GetAsync(string owner, string repo, int issueNumber, CancellationToken)` -> `Issue`
  on `[Get("/repos/{owner}/{repo}/issues/{issueNumber}")]`
- `IIssuesApi.ListCommentsAsync(string owner, string repo, int issueNumber, int page, [AliasAs("per_page")] int perPage, CancellationToken)` -> `IssueComment[]`
- `Issue(long Id, int Number, string Title, string State, int Comments, DateTimeOffset CreatedAt, string HtmlUrl, GitHubUser? User = null, string? BodyText = null, bool? Draft = null, IssuePullRequestRef? PullRequest = null, ReactionRollup? Reactions = null)`
- `IssuePullRequestRef(string? HtmlUrl = null)` - its presence (not null) means "this issue is a pull request".
- `IssueComment(long Id, DateTimeOffset CreatedAt, string HtmlUrl, GitHubUser? User = null, string? BodyText = null, string? AuthorAssociation = null, ReactionRollup? Reactions = null)`
- `ReactionRollup`: the JSON names are `+1` and `-1`, so use explicit names:
  `ReactionRollup(int TotalCount, [property: JsonPropertyName("+1")] int PlusOne, [property: JsonPropertyName("-1")] int MinusOne, int Laugh, int Confused, int Heart, int Hooray, int Eyes, int Rocket)`
- JSON context: `Issue`, `IssueComment[]`. DI: `AddGitHubApi<IIssuesApi>`.

**Tests**: both methods (path + query, nested user, null user, `+1` mapped, `pull_request` present vs absent).
**AotSmoke**: one check per method.

---

## 3.2 Issues API: create comment

**Layer** Api - **Skills** `api-endpoint`, `aot-rules` - **Depends on** 3.1

Look up: `python3 tools/ghapi.py op issues/create-comment`.

**Build**: `Task<IssueComment> CreateCommentAsync(string owner, string repo, int issueNumber, [Body] CreateCommentRequest request, CancellationToken)`
on `[Post(...)]`; `public sealed record CreateCommentRequest(string Body);`
JSON context: add `CreateCommentRequest` and `IssueComment` (single).

**Tests**: use `RecordingHandler`; assert method POST, path, and that the body is exactly `{"body":"hello"}`.
**AotSmoke**: one check (this is the first JSON request body in the app, so it matters).

---

## 3.3 `ConversationViewModel`: issue + comments, paging

**Layer** Core - **Skills** `viewmodel`, `aot-rules` - **Depends on** 3.1, 2.2, 2.10
**Copy** `InboxViewModel` (after 2.6).

**Build**
- `Models/CommentItem.cs` (`sealed partial class : ObservableObject`): get-only `long Id`, `string Author`, `string AvatarUrl`
  (with `s=64`), `string Body`, `string Posted` (`RelativeTime`), `string? Association` (lower-cased, null when `"NONE"`),
  `bool IsOpeningPost`. Factories `From(IssueComment, now)` and `FromIssue(Issue, now)` (the issue body is shown as the first item).
  A deleted user is null in the API: show author `ghost` and an empty avatar URL.
- `ViewModels/ConversationViewModel(IIssuesApi issues, TimeProvider time)`:
  `[ObservableProperty]` `string Title`, `string Subtitle` (`owner/repo #12`), `string State`, `bool IsPullRequest`,
  `bool IsLoading`, `string? ErrorMessage`, `bool HasMore`; `ObservableCollection<CommentItem> Comments`.
  - `[RelayCommand] LoadAsync(string argument)`: `SubjectLink.TryParseArgument`; on failure set `ErrorMessage = "This link is not valid."`.
    Fetch the issue and the first page of comments **concurrently** (`Task.WhenAll`), then fill the state:
    opening post first, then comments oldest first.
  - `[RelayCommand] LoadMoreAsync`: same rules as inbox paging (page size 50).
- Register transient. Add `FakeIssuesApi` to `Fakes.cs`.

**Tests**: load fills header and comments in order; bad argument; API failure; paging; null user -> `ghost`.
**AotSmoke**: one check.

---

## 3.4 `ConversationPage`: header, comment list

**Layer** App - **Skills** `winui-xaml`, `performance` - **Depends on** 3.3
**Copy** `InboxPage`.

**Build** `Views/ConversationPage.xaml`; map `Route.Conversation`.
- `InboxViewModel.OpenAsync` currently opens github.com in the browser. Change it here: when `SubjectLink.TryParse`
  succeeds, call `navigator.Navigate(Route.Conversation, link.ToArgument())` instead (inject `INavigator`); keep the
  browser fallback for other kinds. Update the two `Open_...` tests.
- `OnNavigatedTo`: `ViewModel.LoadCommand.Execute(e.Parameter as string)`. No navigation cache (each conversation is a new page).
- Header: back button (`INavigator.GoBack`; show it by setting `NavigationView.IsBackButtonVisible` / handling `BackRequested` in `ShellView`),
  `Title` (`SubtitleTextBlockStyle`, wraps to 2 lines), `Subtitle` + `State` in caption style.
- `ListView` of `CommentItem`, `SelectionMode="None"`. Item template: a card
  (`Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"`, `BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"`,
  `CornerRadius="{ThemeResource ControlCornerRadius}"`, padding 12): row 1 = avatar 24 px (`x:Phase="1"`), `Author` (strong),
  `Association` (caption, secondary), `Posted` (caption, secondary, right aligned); row 2 = `Body` in a `TextBlock`
  with `TextWrapping="Wrap"` and `IsTextSelectionEnabled="True"`.
- Load more via `ContainerContentChanging`, as in 2.6. Loading, error and empty states as in `InboxPage`.

**Performance note**: long comments make tall items; that is fine, the list still virtualises. Do not put the list in a `ScrollViewer`.

---

## 3.5 Reply box: send a comment

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml` - **Depends on** 3.2, 3.4

**Build**
- `ConversationViewModel`: `[ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] public partial string Draft { get; set; }`,
  `[ObservableProperty] bool IsSending` (also notifies `SendCommand`).
  `[RelayCommand(CanExecute = nameof(CanSend))] SendAsync`: `CanSend` = draft not whitespace and not sending.
  On success append `CommentItem.From(reply, now)` and clear `Draft`. On failure keep `Draft` and set `ErrorMessage`.
- `ConversationPage`: bottom row with a `TextBox` (`AcceptsReturn="True"`, `TextWrapping="Wrap"`, `MaxHeight="160"`,
  `PlaceholderText="Write a comment"`, `Text="{x:Bind ViewModel.Draft, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"`)
  and a "Comment" accent `Button` bound to `SendCommand`. `Ctrl+Enter` sends (`KeyboardAccelerator`).
  After a successful send scroll the list to the last item (`ListView.ScrollIntoView`).

**Tests**: `CanSend` rules; success appends and clears; failure keeps the draft; the request carries the draft text.

---

## 3.6 Reactions: show counts, add a reaction

**Layer** Api + Core + App - **Skills** `api-endpoint`, `viewmodel`, `winui-xaml` - **Depends on** 3.4

Look up: `reactions/create-for-issue-comment`, `reactions/create-for-issue`.

**Build**
- Api `Reactions/IReactionsApi`: `CreateForIssueCommentAsync(owner, repo, long commentId, [Body] CreateReactionRequest, ct)` and
  `CreateForIssueAsync(owner, repo, int issueNumber, [Body] CreateReactionRequest, ct)`, both returning `Task` (ignore the reply body:
  declare the return type as `Task`). `CreateReactionRequest(string Content)`; content is one of
  `+1 -1 laugh confused heart hooray rocket eyes`.
- Core `CommentItem`: `[ObservableProperty] string ReactionSummary` built from the rollup, only non-zero counts, e.g. `👍 3  ❤️ 1  🚀 2`
  (empty string when there are none). `ConversationViewModel.[RelayCommand] ReactAsync((CommentItem item, string content))` is awkward
  for XAML, so expose `ReactAsync(CommentItem item, string content)` as a normal public method and call it from code-behind.
  Optimistic: bump the count, rebuild the summary; revert on failure.
- App: under each comment body show `ReactionSummary` (collapsed when empty) and a small "Add reaction" `Button` with a `MenuFlyout`
  of the eight reactions; `Click` handlers in code-behind call `ViewModel.ReactAsync`.

**Tests**: Api (path, body); Core (summary text, optimistic update, revert).

---

## 3.7 Pull request header

**Layer** Api + Core + App - **Skills** `api-endpoint`, `viewmodel`, `winui-xaml` - **Depends on** 3.4

Look up: `python3 tools/ghapi.py op pulls/get`, then `python3 tools/ghapi.py schema pull-request`.

**Build**
- Api `Pulls/IPullsApi.GetAsync(owner, repo, int pullNumber, ct)` -> `PullRequest(int Number, string State, bool Merged, int Additions, int Deletions, int ChangedFiles, PullBranch Head, PullBranch Base, bool? Draft = null, bool? Mergeable = null)`;
  `PullBranch(string Ref, string Label)`. Note `base` is a C# keyword: the record parameter is `Base`, which is fine.
- Core `ConversationViewModel`: when `IsPullRequest`, fetch the pull request alongside the comments and set
  `[ObservableProperty]` `string Branches` (`feature -> main`), `string DiffSummary` (`+120 -8 in 5 files`),
  and `State` = `merged` / `draft` / `open` / `closed` (in that priority).
- App: show `Branches` and `DiffSummary` in the header when `IsPullRequest`; colour the state with an `InfoBadge`-style
  pill using theme brushes (`SystemFillColorSuccessBrush` open, `SystemFillColorAttentionBrush` merged,
  `SystemFillColorCriticalBrush` closed, `SystemFillColorNeutralBrush` draft) chosen by a `Bind` function.

**Tests**: Api mapping; Core state priority (4 cases) and summary strings. A failed pull request fetch must not
fail the whole page: keep the issue data and leave the extra fields empty.

---

## 3.8 "Pull requests" page

**Layer** Api + Core + App - **Skills** `api-endpoint`, `viewmodel`, `winui-xaml`, `performance` - **Depends on** 2.5, 2.10

Look up: `python3 tools/ghapi.py op search/issues-and-pull-requests`.

**Build**
- Api `Search/ISearchApi.SearchIssuesAsync(string q, string sort, string order, int page, [AliasAs("per_page")] int perPage, ct)` ->
  `IssueSearchResult(int TotalCount, bool IncompleteResults, IssueSearchItem[] Items)`;
  `IssueSearchItem(long Id, int Number, string Title, string State, int Comments, DateTimeOffset UpdatedAt, string HtmlUrl, string RepositoryUrl, GitHubUser? User = null, bool? Draft = null)`.
  `repository_url` looks like `https://api.github.com/repos/{owner}/{repo}`: derive `owner/repo` from it in Core.
- Core `PullRequestsViewModel`: a `Scope` enum (`ReviewRequested`, `Created`, `Assigned`) mapped to the queries
  `is:open is:pr review-requested:@me archived:false`, `is:open is:pr author:@me archived:false`, `is:open is:pr assignee:@me archived:false`,
  `sort: "updated", order: "desc"`. Items are a `PullRequestItem` model (title, `owner/repo #n`, author, updated, draft flag).
  Same loading/paging/error shape as `InboxViewModel`. `OpenCommand` navigates to `Route.Conversation`.
- App `PullRequestsPage`: same structure as `InboxPage` with a `SelectorBar` for the scope; map `Route.PullRequests`.

The search API allows 30 requests per minute. Do not poll this page.

**Tests**: Api (query string exactly, including the encoded `q`); Core (each scope's query, paging, open).

---

## 3.9 [S] Markdown rendering

**Layer** all - **Skills** all - **Depends on** 3.4

Comments are Markdown. Until this card they are plain text. This is a design task; do not hand it to a small model.

Decide between, and record the decision at the top of this card:

1. **Render GitHub's HTML ourselves**: request `body_html`, convert the limited tag set GitHub emits
   (p, a, strong, em, code, pre, ul/ol/li, blockquote, h1-h6, img, table, task lists) into `RichTextBlock` blocks.
   No dependency, full control over speed; the work is the converter and its tests.
2. **`CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock`**: parse `body` on the client. Check first: is it
   out of Labs/preview, does it and its parser (Markdig) publish clean under Native AOT, and what does it add to the exe size.
3. **WebView2**: rejected in advance. One browser process per conversation defeats the point of this project.

Acceptance: the AOT publish has no new warnings; exe size change is reported; scrolling a 200-comment
conversation stays smooth; links open in the browser; code blocks use a monospace font and scroll horizontally.
