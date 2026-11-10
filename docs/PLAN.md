# Plan

Guit is a native Windows client for GitHub: **sign in -> inbox (the feed) -> conversations (the messaging) -> Actions (the jobs)**.
Stack: C# on .NET 10, WinUI 3 (Windows App SDK 2.x), Native AOT, Refit 16 generated clients,
System.Text.Json source generation, CommunityToolkit.Mvvm source generators.

How to use this file: take the first unticked task whose dependencies are ticked, open its card in
`docs/tasks/`, load the `implement-task` skill, do the card, tick the box.

Markers: **[S]** needs a strong model (design judgement, or an unknown to investigate).
**[H]** needs a human (accounts, a Windows machine, a decision). Unmarked tasks are sized for a small model:
one layer, one or two files plus tests, a worked example to copy.

## Phase 0 - Foundation ([cards](tasks/phase-0-foundation.md))

- [x] 0.1 Solution, central build settings, warnings-as-errors, AOT analyzers on every project
- [x] 0.2 `Guit.Api` with generated-only Refit clients and a JSON source-generation context
- [x] 0.3 `Guit.Core` with MVVM Toolkit partial-property view models
- [x] 0.4 `Guit.App` WinUI 3 shell: unpackaged, self-contained, `PublishAot`
- [x] 0.5 Tests on TUnit; `Guit.AotSmoke` native smoke test
- [x] 0.6 CI: Linux tests + AOT smoke; Windows build + AOT publish + launch smoke test
- [x] 0.7 `tools/ghapi.py` OpenAPI lookup; skills in `.claude/skills`

## Phase 1 - Sign in ([cards](tasks/phase-1-sign-in.md))

- [x] 1.1 Device flow API (`IGitHubDeviceFlowApi`, `DeviceFlow.WaitForTokenAsync`)
- [x] 1.2 `Session`, `SignInViewModel`, `ShellViewModel`
- [x] 1.3 Sign-in view, Credential Locker token store, launcher
- [ ] 1.4 **[H]** Register the GitHub OAuth App, set the client id, sign in for real on Windows
- [x] 1.5 **[S]** Sign out automatically on HTTP 401 (adds `IUiDispatcher`)
- [x] 1.6 Friendly error messages (`ApiErrors.Describe`)

## Phase 2 - Inbox, the feed ([cards](tasks/phase-2-inbox.md))

- [x] 2.1 Notifications API: list, mark as read, mark as done
- [x] 2.2 `NotificationItem` model and `RelativeTime` helper
- [x] 2.3 `InboxViewModel`: first page, loading and error state
- [x] 2.4 Navigation service (`INavigator`, `Route`)
- [x] 2.5 `InboxPage`: virtualised list with an item template
- [x] 2.6 Paging: load more when the list nears its end
- [x] 2.7 Filters: Unread / All / Participating
- [ ] 2.8 Item actions: mark as read, mark as done
- [ ] 2.9 Refresh: button, F5, and a background poll
- [ ] 2.10 Open a notification (`SubjectLink` parser; conversation or browser)

## Phase 3 - Conversations, the messaging ([cards](tasks/phase-3-conversations.md))

- [ ] 3.1 Issues API: get issue, list comments
- [ ] 3.2 Issues API: create comment
- [ ] 3.3 `ConversationViewModel`: issue + comments, paging
- [ ] 3.4 `ConversationPage`: header, comment list
- [ ] 3.5 Reply box: send a comment
- [ ] 3.6 Reactions: show counts, add a reaction
- [ ] 3.7 Pull request header: Pulls API + state, branches, diff size
- [ ] 3.8 "Pull requests" page: search API + list
- [ ] 3.9 **[S]** Markdown rendering: evaluate options, pick one, implement

## Phase 4 - Actions, the jobs ([cards](tasks/phase-4-actions.md))

- [ ] 4.1 Repositories API: list my repositories
- [ ] 4.2 Actions API: list workflow runs, list jobs for a run
- [ ] 4.3 `RunsViewModel` and `RunsPage`
- [ ] 4.4 `JobsViewModel` and run detail page
- [ ] 4.5 Re-run a workflow

## Phase 5 - Speed and shipping ([cards](tasks/phase-5-speed-and-shipping.md))

- [ ] 5.1 **[S]** Start-up time measured in CI, with a budget that fails the build
- [ ] 5.2 **[S]** Conditional requests (ETag / `Last-Modified`, `X-Poll-Interval`) for polling
- [ ] 5.3 Settings page: theme, account, about
- [ ] 5.4 Keyboard and accessibility pass
- [ ] 5.5 ARM64 build in CI
- [ ] 5.6 **[S][H]** MSIX packaging and signing
- [ ] 5.7 **[S]** Move to .NET 11 and CsWinRT 3 when both are stable; compare size and start-up
- [ ] 5.8 **[S]** Head-to-head: the same inbox page in C++/WinRT, measured against this app

## Known limits (decided, not to be "fixed" by a task)

- GitHub's website "For you" feed is not in the API. The feed here is notifications (2.x); an activity
  page from `received_events` can be added later.
- Sign-in uses an **OAuth App** with the device flow, not a GitHub App: notification endpoints are not
  available to GitHub App tokens (`ghapi.py op` prints this per operation).
- Discussions and review-thread resolution exist only in the GraphQL API. Out of scope until Phase 3 is done.
- Comment text is shown as plain text (`body_text`) until 3.9 lands.
