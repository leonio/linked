# Phase 4 - Actions (the jobs)

Pick a repository, see its workflow runs, open a run to see its jobs, re-run a failed one.
These cards are shorter than the earlier ones on purpose: by now every shape has a finished example to copy.

| New thing | Copy from |
|---|---|
| API interface + DTOs + tests | `src/Linked.Api/Notifications/`, card 3.1 |
| List view model with paging | `InboxViewModel` |
| List page | `InboxPage` |
| Detail page that takes a string argument | `ConversationPage` |

GitHub wraps these lists in an object (`{ "total_count": 3, "workflow_runs": [...] }`), so each list method returns a
small wrapper record, not an array. Register the wrapper in `GitHubJsonContext`.

---

## 4.1 Repositories API: list my repositories

**Layer** Api - **Skills** `api-endpoint` - **Depends on** nothing
Look up: `python3 tools/ghapi.py op repos/list-for-authenticated-user`.

`Repos/IReposApi.ListMineAsync(string sort, int page, [AliasAs("per_page")] int perPage, ct)` -> `RepositorySummary[]`
(the record already exists in `Notifications/NotificationThread.cs`; move it to `src/Linked.Api/Repos/RepositorySummary.cs`
and fix the usings). Callers pass `sort: "pushed"`. JSON context: `RepositorySummary[]`.

**Tests**: path + query; mapping. **AotSmoke**: one check.

---

## 4.2 Actions API: list workflow runs, list jobs for a run

**Layer** Api - **Skills** `api-endpoint` - **Depends on** nothing
Look up: `actions/list-workflow-runs-for-repo`, `actions/list-jobs-for-workflow-run`, then `schema workflow-run` and `schema job`.

`Actions/IActionsApi`:
- `ListRunsAsync(owner, repo, int page, [AliasAs("per_page")] int perPage, ct)` -> `WorkflowRunList(int TotalCount, WorkflowRun[] WorkflowRuns)`
- `ListJobsAsync(owner, repo, long runId, int page, [AliasAs("per_page")] int perPage, ct)` -> `JobList(int TotalCount, Job[] Jobs)`
- `WorkflowRun(long Id, string DisplayTitle, int RunNumber, string Event, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string HtmlUrl, string? Name = null, string? Status = null, string? Conclusion = null, string? HeadBranch = null, GitHubUser? Actor = null)`
- `Job(long Id, string Name, string Status, DateTimeOffset StartedAt, string HtmlUrl, string? Conclusion = null, DateTimeOffset? CompletedAt = null)`

`status` and `conclusion` stay strings (see the type table in `api-endpoint`).

**Tests**: both methods; a run with null `conclusion` (still running). **AotSmoke**: one check each.

---

## 4.3 `RunsViewModel` and `RunsPage`

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml`, `performance` - **Depends on** 4.1, 4.2, 2.4

- Core `Models/RunItem`: title, `#number - event - branch`, actor, updated (`RelativeTime`), and a `StatusGlyph` + `StatusKind`
  (`success`, `failure`, `running`, `queued`, `cancelled`, `other`) computed with a `switch` on status/conclusion.
- Core `RunsViewModel(IReposApi, IActionsApi, INavigator, TimeProvider)`: `ObservableCollection<string> Repositories`
  (`full_name`, first page of 50, sorted by pushed), `[ObservableProperty] string? SelectedRepository` (changing it loads runs),
  `ObservableCollection<RunItem> Runs`, paging and states as in `InboxViewModel`, `OpenCommand` -> `Route.RunDetail` with argument `owner/repo@runId`.
- App `RunsPage`: header with a `ComboBox` of repositories (`SelectedItem` TwoWay) and a refresh button; `ListView` of runs.
  Add an "Actions" `NavigationViewItem` (`Icon="Play"`) to `ShellView` and map `Route.Runs`.
- Remember the last selected repository in memory only (a field on a singleton is not needed: register `RunsViewModel` as a singleton instead of transient).

**Tests**: repositories load; selecting one loads its runs with the right owner/repo; status mapping (6 cases); open navigates with the right argument.

---

## 4.4 `JobsViewModel` and run detail page

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml` - **Depends on** 4.3

- Core `Models/JobItem`: name, status kind/glyph (same mapping, share one static helper with `RunItem`), duration text
  (`2m 05s`, or `running` when not completed).
- Core `JobsViewModel(IActionsApi, ILauncher, TimeProvider)`: `LoadAsync(string argument)` parses `owner/repo@runId`
  (write `RunLink` like `SubjectLink`, with tests), loads the jobs (first 100, no paging needed), exposes
  `OpenInBrowserCommand(JobItem)` using the job's `HtmlUrl` (logs are viewed on github.com for now).
- App `RunDetailPage`: back button, title, list of jobs. Map `Route.RunDetail`.

**Tests**: `RunLink` parsing; duration formatting; load; open in browser.

---

## 4.5 Re-run a workflow

**Layer** Api + Core + App - **Skills** `api-endpoint`, `viewmodel`, `winui-xaml` - **Depends on** 4.4
Look up: `actions/re-run-workflow` (POST, reply 201 with an empty object).

- Api: `Task RerunAsync(owner, repo, long runId, ct)` on `[Post("/repos/{owner}/{repo}/actions/runs/{runId}/rerun")]`. No body.
- Core `JobsViewModel.[RelayCommand] RerunAsync`: call, then reload the jobs; errors through `ApiErrors`.
  A 403 here usually means the token lacks the `workflow` scope: if `ApiErrors.Describe` returns the "refused" text,
  that is the right message.
- App: a "Re-run all jobs" button in the run detail header.
- **Do not** change the OAuth scopes in `GitHubOptions` in this card. If re-run needs a scope the app does not ask for, report it.

**Tests**: Api (method, path, empty body); Core (success reloads, failure sets the error).
