# Phase 2 - Inbox (the feed)

The inbox is the member's GitHub notifications: one row per thread (an issue, pull request, release...)
with a title, repository, reason and time. Card 2.1 is done: `src/Guit.Api/Notifications/`.

Shape of the feature when the phase is finished:

```
Guit.Api    INotificationsApi                     (done)
Guit.Core   Models/NotificationItem               2.2
              Text/RelativeTime                     2.2
              ViewModels/InboxViewModel             2.3, 2.6, 2.7, 2.8, 2.9
              Services/INavigator, Route            2.4
              Models/SubjectLink                    2.10
Guit.App    Services/Navigator                    2.4
              Views/InboxPage.xaml                  2.5 and later cards
```

---

## 2.2 `NotificationItem` model and `RelativeTime` helper

**Layer** Core - **Skills** `viewmodel`, `aot-rules` - **Depends on** 2.1
**Copy** the item-model template in the `viewmodel` skill.

**Build**
- `src/Guit.Core/Text/RelativeTime.cs`: `public static string Format(DateTimeOffset then, DateTimeOffset now)`.

  | Age | Text |
  |---|---|
  | under 1 minute (or in the future) | `now` |
  | under 1 hour | `5m` |
  | under 1 day | `3h` |
  | under 7 days | `2d` |
  | same year | `12 Mar` |
  | otherwise | `12 Mar 2024` |

  Use `CultureInfo.InvariantCulture` for the date forms (`"d MMM"`, `"d MMM yyyy"`).
- `src/Guit.Core/Models/NotificationItem.cs`: `public sealed partial class NotificationItem : ObservableObject`.
  - Get-only: `string Id`, `string Title`, `string Repository` (`full_name`), `string Kind` (subject type),
    `string Reason`, `string Glyph`, `string AvatarUrl`, `string Updated`, `string? SubjectUrl`.
  - `[ObservableProperty] public partial bool IsUnread { get; set; }`
  - `public static NotificationItem From(NotificationThread thread, DateTimeOffset now)`.
  - `Reason`: replace `_` with a space (`review_requested` -> `review requested`).
  - `AvatarUrl`: the repository owner's `AvatarUrl` with `&s=64` appended if the URL contains `?`, otherwise `?s=64`.
  - `Updated`: `RelativeTime.Format(thread.UpdatedAt, now)`.
  - `Glyph`: a Segoe Fluent Icons character chosen by `Kind` with a `switch`:
    `"PullRequest"` -> `""`, `"Issue"` -> `""`, `"Release"` -> `""`, `"Discussion"` -> `""`,
    `"CheckSuite"` -> `""`, anything else -> `""`.

**Tests** (`RelativeTimeTests`, `NotificationItemTests`): one test per table row using fixed dates;
`From` maps every property; both avatar URL forms.

**Not in this card**: view models, XAML, localisation.

---

## 2.3 `InboxViewModel`: first page, loading and error state

**Layer** Core - **Skills** `viewmodel`, `aot-rules` - **Depends on** 2.2
**Copy** the page view model template in the `viewmodel` skill; tests like `SignInViewModelTests`.

**Build** `src/Guit.Core/ViewModels/InboxViewModel.cs`, constructor `(INotificationsApi notifications, TimeProvider time)`.
- `ObservableCollection<NotificationItem> Items { get; }`
- `[ObservableProperty]` `bool IsLoading`, `string? ErrorMessage`; computed `bool IsEmpty` = not loading, no error, `Items.Count == 0`
  (raise it with `OnPropertyChanged(nameof(IsEmpty))` at the end of `LoadAsync`, and via `[NotifyPropertyChangedFor]` on the two properties).
- `[RelayCommand] private async Task LoadAsync(CancellationToken)`: calls
  `ListAsync(all: false, participating: false, page: 1, perPage: 50, ...)`, clears `Items`, adds one item per thread
  using `time.GetUtcNow()`.
- Register: `services.AddTransient<InboxViewModel>();` in `AddGuitCore`.
- Add `FakeNotificationsApi` to `tests/Guit.Core.Tests/Fakes.cs`. It takes the pages to return (`NotificationThread[][]`),
  records the arguments of each `ListAsync` call and the ids passed to `MarkAsReadAsync` / `MarkAsDoneAsync`,
  and can be told to fail (`Failure.Status(...)`).

**Tests**: load fills `Items` in order; load twice does not duplicate; failure sets `ErrorMessage`, leaves `Items`
empty and `IsLoading` false; empty reply sets `IsEmpty`.
**AotSmoke**: resolve `InboxViewModel`, run `LoadCommand`, check one item with the canned title.

---

## 2.4 Navigation service

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml`, `aot-rules` - **Depends on** nothing
**Copy** `ILauncher` (Core interface) and `Services/Launcher.cs` (App implementation).

View models must be able to ask for navigation without knowing about pages.

**Build**
- Core `Services/INavigator.cs`:
  ```csharp
  public enum Route { Inbox, PullRequests, Conversation, Runs, RunDetail, Settings }

  public interface INavigator
  {
      /// <param name="argument">Optional string argument (for example "owner/repo#12"). Strings only.</param>
      void Navigate(Route route, string? argument = null);
      bool CanGoBack { get; }
      void GoBack();
  }
  ```
- App `Services/Navigator.cs`: holds a `Frame? Frame { get; set; }` (set by `ShellView` when the frame loads).
  `Navigate` maps `Route` to a page with a `switch` returning `typeof(...)` (every route maps to `PlaceholderPage`
  for now except those that exist) and calls `Frame.Navigate(type, argument)`.
  Register as a singleton for both `Navigator` and `INavigator`.
- `ShellView.xaml.cs`: replace the `switch` on `Tag` with calls to the navigator
  (`"inbox"` -> `Route.Inbox`, `"pulls"` -> `Route.PullRequests`); set `navigator.Frame = ContentFrame` in `OnNavigationLoaded`.
- `FakeNavigator` in `Fakes.cs` recording `(Route, string?)` calls. `NullNavigator` in AotSmoke.

**Rules**: the argument is a `string` or null, never an object (objects passed through `Frame.Navigate` are a
known AOT and state-restore problem). Always `typeof(Page)`, never a type name.

**Tests**: none in Core beyond the fake compiling. On Windows: app builds; clicking both items shows the placeholder.

---

## 2.5 `InboxPage`: virtualised list with an item template

**Layer** App - **Skills** `winui-xaml`, `performance`, `aot-rules` - **Depends on** 2.3, 2.4
**Copy** the page skeleton and list template in the `winui-xaml` skill.

**Build** `src/Guit.App/Views/InboxPage.xaml` + `.xaml.cs`; map `Route.Inbox` to it in `Navigator`.
- Code-behind: `ViewModel = App.Services.GetRequiredService<InboxViewModel>()` in the constructor,
  `NavigationCacheMode = NavigationCacheMode.Required`, and `OnNavigatedTo` runs `ViewModel.LoadCommand` only when
  `Items.Count == 0`.
- Layout: a `Grid` with a header row (`TextBlock` "Inbox", `TitleTextBlockStyle`, margin `24,16`) and the list row.
- `ListView`: `ItemsSource="{x:Bind ViewModel.Items}"`, `SelectionMode="None"`, `IsItemClickEnabled="True"`.
- Item template (`x:DataType="models:NotificationItem"`), a `Grid` with 3 columns (`Auto, *, Auto`), height about 56:
  - column 0: `FontIcon` `Glyph="{x:Bind Glyph}"`, 16 px, `Foreground` accent when unread
    (use `Opacity="{x:Bind xaml:Bind.UnreadOpacity(IsUnread), Mode=OneWay}"`; add that function to `Bind.cs`: unread 1.0, read 0.55, apply it to the whole row `Grid`).
  - column 1: two rows. `Title` (`BodyStrongTextBlockStyle`, `TextTrimming="CharacterEllipsis"`, one line);
    below it `Repository` + ` - ` + `Reason` as two `Run`s in one `TextBlock` (`CaptionTextBlockStyle`,
    `Foreground="{ThemeResource TextFillColorSecondaryBrush}"`).
  - column 2: `Updated` (`CaptionTextBlockStyle`, secondary brush), and an `ImageIcon`/`Image` 20x20 for the avatar with `x:Phase="1"`.
- States, outside the list in the same grid cell:
  `ProgressRing` (`IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}"`), an `InfoBar` for `ErrorMessage`
  (same pattern as `SignInView.xaml`), and a centred "You're all caught up" `TextBlock` shown with
  `Bind.VisibleIf(ViewModel.IsEmpty)`.

**Verify**: this card cannot be verified off Windows. Push and read CI; say so in the report.

**Not in this card**: clicking an item (2.10), paging (2.6), filters (2.7), swipe/context actions (2.8).

---

## 2.6 Paging: load more when the list nears its end

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml`, `performance` - **Depends on** 2.5

**Build**
- `InboxViewModel`: private `int _page`; `[ObservableProperty] bool HasMore`; `[ObservableProperty] bool IsLoadingMore`.
  `LoadAsync` sets `_page = 1` and `HasMore = page.Length == 50`.
  `[RelayCommand] LoadMoreAsync`: returns immediately if `!HasMore || IsLoading || IsLoadingMore`; otherwise requests
  `_page + 1`, appends, increments `_page` only on success, sets `HasMore = page.Length == 50`.
  Put the page size in one `private const int PageSize = 50`.
- `InboxPage`: handle `ListView.ContainerContentChanging`; when `args.ItemIndex >= ViewModel.Items.Count - 10`
  call `ViewModel.LoadMoreCommand.Execute(null)`. Add a small `ProgressBar IsIndeterminate` under the list bound to `IsLoadingMore`.

**Tests**: second page appends in order; a short page sets `HasMore` false and further calls do not hit the API;
a failed load-more keeps existing items and keeps `_page` (next call asks for the same page); no call while a load is running.

---

## 2.7 Filters: Unread / All / Participating

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml` - **Depends on** 2.6

**Build**
- Core `Models/InboxFilter.cs`: `public enum InboxFilter { Unread, All, Participating }`.
- `InboxViewModel`: `[ObservableProperty] public partial InboxFilter Filter { get; set; }`; implement
  `partial void OnFilterChanged(InboxFilter value)` to run `LoadCommand`. Map to the API:
  Unread -> `all: false, participating: false`; All -> `all: true, participating: false`;
  Participating -> `all: false, participating: true`. Both `LoadAsync` and `LoadMoreAsync` use the mapping.
- `InboxPage`: a `SelectorBar` in the header with three `SelectorBarItem`s; in `SelectionChanged` set
  `ViewModel.Filter` from the selected item's index (cast the index to `InboxFilter`).

**Tests**: each filter sends the right arguments (assert on `FakeNotificationsApi` recorded calls);
changing the filter replaces the items and resets paging.

---

## 2.8 Item actions: mark as read, mark as done

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml` - **Depends on** 2.5

**Build**
- `InboxViewModel`:
  - `[RelayCommand] MarkAsReadAsync(NotificationItem item)`: set `item.IsUnread = false` first (optimistic), call the API;
    on `ApiExceptionBase` set it back to `true` and set `ErrorMessage`. If `Filter == Unread`, remove the item from `Items` on success.
  - `[RelayCommand] MarkAsDoneAsync(NotificationItem item)`: remove from `Items` first, remembering its index; on failure insert it back at that index and set `ErrorMessage`.
- `InboxPage` item template: a `SwipeControl` is not needed. Add to the row `Grid` a `ContextFlyout` `MenuFlyout` with
  "Mark as read" and "Mark as done", each with `Click` handlers in code-behind that read
  `((FrameworkElement)sender).DataContext as NotificationItem` and execute the command.
  (Inside a `DataTemplate`, `x:Bind` cannot reach the page's `ViewModel`; a code-behind `Click` handler is the simple, AOT-safe way.)
  Also handle `Delete` key on the list -> mark the focused item as done.

**Tests**: success and failure paths of both commands, including the item being restored at its original index.

---

## 2.9 Refresh: button, F5, and a background poll

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml`, `performance` - **Depends on** 2.6

**Build**
- `InboxViewModel`: `[RelayCommand] RefreshAsync` = `LoadAsync` but **without clearing first**: fetch page 1, then
  if the ids and unread flags equal the current first N items do nothing; otherwise replace the contents.
  (Keeps scroll position stable when nothing changed.)
- Polling: `public void StartPolling()` / `StopPolling()` using `time.CreateTimer(...)` with a 60 second period that
  posts `RefreshCommand.Execute(null)`. The timer callback runs on a thread-pool thread, so it must go through
  `IUiDispatcher.Post` (from card 1.5). Skip a tick when `IsLoading` is true.
- `InboxPage`: a refresh `AppBarButton` in the header (`Icon="Refresh"`); a `KeyboardAccelerator Key="F5"`;
  `OnNavigatedTo` -> `StartPolling()`, `OnNavigatedFrom` -> `StopPolling()`.

**Depends on** also 1.5 for `IUiDispatcher`. If 1.5 is not done, do everything except polling and say so.

**Tests**: refresh with identical data leaves the same item instances in `Items`; refresh with a new first item
replaces contents; polling test with `Microsoft.Extensions.Time.Testing.FakeTimeProvider` is **not** required
(that package is not approved) - test `RefreshAsync` only.

---

## 2.10 Open a notification

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml` - **Depends on** 2.4, 2.5

**Build**
- Core `Models/SubjectLink.cs`:
  ```csharp
  public readonly record struct SubjectLink(string Owner, string Repo, int Number, bool IsPullRequest)
  {
      public static bool TryParse(string? apiUrl, out SubjectLink link);   // https://api.github.com/repos/{owner}/{repo}/(issues|pulls)/{number}
      public static bool TryParseArgument(string? argument, out SubjectLink link);   // "owner/repo#12" or "owner/repo!12" for a pull request
      public string ToArgument();
      public Uri ToWebUri();   // https://github.com/{owner}/{repo}/(issues|pull)/{number}
  }
  ```
  Parse with `ReadOnlySpan<char>` / `Split('/')`; no `Regex`.
- `InboxViewModel(…, INavigator navigator, ILauncher launcher)`: `[RelayCommand] OpenAsync(NotificationItem item)`:
  if `SubjectLink.TryParse(item.SubjectUrl, ...)` -> `navigator.Navigate(Route.Conversation, link.ToArgument())`;
  otherwise open `https://github.com/{item.Repository}` with the launcher. Then run `MarkAsReadCommand` if the item is unread.
- `InboxPage`: `ItemClick` handler -> `ViewModel.OpenCommand.Execute((NotificationItem)e.ClickedItem)`.

**As built**: `OpenAsync` navigates to `Route.Conversation` for issues and pull requests (since card 3.4).

**Tests**: `TryParse` for an issue URL, a pull URL, null, a release URL (false), garbage (false); round trip
`ToArgument` -> `TryParseArgument`; `OpenAsync` navigates for issues/pulls and launches the browser for other kinds.
