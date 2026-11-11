# Plan

Guit is a native Windows client for GitHub: **sign in -> inbox (the feed) -> conversations (the messaging) -> Actions (the jobs)**.
Stack: C# on .NET 11, WinUI 3 (Windows App SDK 2.x), Native AOT, Refit 16 generated clients,
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
- [x] 1.4 **[H]** Register the GitHub OAuth App, set the client id, sign in for real on Windows
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
- [x] 2.8 Item actions: mark as read, mark as done
- [x] 2.9 Refresh: button, F5, and a background poll
- [x] 2.10 Open a notification (`SubjectLink` parser; conversation or browser)
- [x] 2.11 Filter by kind of notification (client-side, with bounded top-up paging)

## Phase 3 - Conversations, the messaging ([cards](tasks/phase-3-conversations.md))

- [x] 3.1 Issues API: get issue, list comments
- [x] 3.2 Issues API: create comment
- [x] 3.3 `ConversationViewModel`: issue + comments, paging
- [x] 3.4 `ConversationPage`: header, comment list
- [x] 3.5 Reply box: send a comment
- [x] 3.6 Reactions: show counts, add a reaction
- [x] 3.7 Pull request header: Pulls API + state, branches, diff size
- [x] 3.8 "Pull requests" page: search API + list
- [x] 3.9 **[S]** Markdown rendering: evaluate options, pick one, implement
- [x] 3.10 "Files changed": a pull request's files and coloured diffs (`DiffRow`, `PullFilesViewModel`, `PullFilesPage`)
- [ ] 3.11 **[S]** Browse repository code: file tree and a read-only file viewer (contents API)
- [ ] 3.12 **[S]** Syntax colouring for diffs and the file viewer

## Phase 4 - Actions, the jobs ([cards](tasks/phase-4-actions.md))

- [x] 4.1 Repositories API: list my repositories
- [x] 4.2 Actions API: list workflow runs, list jobs for a run
- [x] 4.3 `RunsViewModel` and `RunsPage`
- [x] 4.4 `JobsViewModel` and run detail page
- [x] 4.5 Re-run a workflow
- [x] 4.6 Run detail: step rows under each job, self-updating while a run is live or was just re-run
- [x] 4.7 Job log page (`ActionsLog`, `JobLogViewModel`, `JobLogPage`): last 5,000 lines, cleaned
- [ ] 4.8 Live log while a job is still running (GitHub only publishes a log when the job ends; needs another source)

## Phase 5 - Speed and shipping ([cards](tasks/phase-5-speed-and-shipping.md))

- [x] 5.1 **[S]** Start-up time measured in CI, with a budget that fails the build
- [x] 5.2 **[S]** Conditional requests (ETag / `Last-Modified`, `X-Poll-Interval`) for polling
- [x] 5.3 Settings page: theme, account, about
- [x] 5.4 Keyboard and accessibility pass
- [x] 5.5 ARM64 build in CI
- [ ] 5.6 **[S][H]** MSIX packaging and signing (blocked: needs a signing certificate and a decision on distribution)
- [x] 5.7a Move to .NET 11 (numbers in `docs/benchmarks.md`)
- [ ] 5.7b **[S]** Move to CsWinRT 3 when it is stable (still a preview in November 2026)
- [ ] 5.8 **[S][H]** Head-to-head: the same inbox page in C++/WinRT, measured against this app (needs a Windows machine with Visual Studio; the C# half is in `docs/benchmarks.md`)

## Phase 6 - The Rust app, side by side (state and next steps in `HANDOVER.md`)

The same app on windows-rs and windows-reactor, to compare with the C# app. It replaces card 5.8.

- [x] 6.1 Cargo workspace; `guit-core` (GitHub client, device flow, time, links, inbox rows) with tests
- [x] 6.2 `guit`: WinRT platform layer, sign-in, shell and inbox on windows-reactor
- [x] 6.3 CI: core tests on Linux; build, stage, launch and time on Windows x64 and ARM64
- [x] 6.4 One measuring script for both apps (`tools/measure-startup.ps1`)
- [x] 6.5 Manually started workflow publishing versioned zips of both apps (`release.yml`)
- [ ] 6.6 **[H]** Run the Rust app signed in on a real machine; nobody has seen its inbox yet
- [x] 6.7 Inbox parity: paging ("Load more"), filters, kind filter, mark as done, background refresh (unconditional for now)
- [x] 6.8 Pull requests page; conversations as plain text with a reply box (first 100 comments)
- [ ] 6.8b Conversation parity: formatted comments, reactions, pull request header, Files changed, comment paging
- [ ] 6.9 Actions pages
- [ ] 6.10 **[S]** Measure the signed-in scenarios on one machine: time to a filled inbox, memory, scrolling

## Known limits (decided, not to be "fixed" by a task)

- GitHub's website "For you" feed is not in the API. The feed here is notifications (2.x); an activity
  page from `received_events` can be added later.
- Sign-in uses an **OAuth App** with the device flow, not a GitHub App: notification endpoints are not
  available to GitHub App tokens (`ghapi.py op` prints this per operation).
- Discussions and review-thread resolution exist only in the GraphQL API. Out of scope until Phase 3 is done.
- Comment formatting is a first version: code blocks wrap, tables show one row per line, images are links.
