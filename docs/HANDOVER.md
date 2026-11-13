# Handover

Written on 11 November 2026 for whoever picks this up next. Read `CLAUDE.md` first; this file is the state of play.

## What this repository is

**Guit**: a native Windows client for GitHub (inbox, conversations, pull request diffs, Actions).
The point is a performance experiment: how close can C# + WinUI 3 + Native AOT get to a native app?
A second implementation in Rust now sits beside it so the two can be measured side by side.

- Default branch: `main`. The owner has asked for small commits pushed straight to `main`.
- Commit messages carry **no trailers** (no `Co-Authored-By`, no `Claude-Session`).
- The GitHub repository is still named `leonio/linked`; only the projects were renamed to Guit.
- An old branch, `claude/optimistic-hypatia-tf9gpp`, still holds early commits with session links in
  their messages. The owner was told to delete it; nothing depends on it.

## The C# app: done and working

The owner has run it on Windows and confirmed sign-in, inbox, pull requests, conversations and Actions work.

| Area | State |
|---|---|
| Phases 0-4 of `docs/PLAN.md` | Complete |
| Phase 5 | Complete except 5.6 (MSIX: needs a signing certificate and a distribution decision), 5.7b (CsWinRT 3: still a preview) and 5.8 (the native comparison, replaced by the Rust app, phase 6) |
| Open feature cards | 3.11 browse repository code, 3.12 syntax colouring, 4.8 live log of a running job |
| Tests | 221 (TUnit), plus a Native AOT smoke test of the non-UI layers |
| CI | `.github/workflows/ci.yml`: Linux tests + AOT smoke; Windows x64 and ARM64 build, AOT publish, launch, start-up timing with a budget |
| Target | .NET 11, Windows App SDK 2.6.5, Refit 16.4, CommunityToolkit.Mvvm 8.4.2 |

Measured numbers and the method are in `docs/benchmarks.md` (x64 about 190-210 ms from process start to window on a CI runner).

**Not yet seen running by anyone** (CI launches the app signed out, and the owner's last test predates them):
the settings page, Ctrl+1..3 and Esc shortcuts, step rows and the job log page, the "Files changed" diff page,
and the inbox kind filter. The owner then said "it all works locally", which probably covers these, but it was not itemised.

How to work on it: `CLAUDE.md` maps each layer to a skill in `.claude/skills/`; those skills contain the
rules and the compiler traps already hit. Without Windows you cannot build `dotnet/src/Guit.App`; push and read CI.

## The Rust app: builds, launches and is measured; nobody has used it signed in

The owner asked for the same app in Rust with **windows-rs** and **windows-reactor**, side by side in this
repository, plus manually started builds that publish versioned files of both apps for side-by-side testing.

### What exists

| Piece | State |
|---|---|
| `rust/guit-core` | GitHub client over a `Transport` trait; device flow; relative time; subject links; the inbox state model (paging, filters, kind filter with top-up, optimistic actions, change-free refresh); pull request and comment rows; Actions rows, durations and the log cleaner. No Windows dependency. 57 tests, run on Linux in CI. Also `rich` (comment HTML to styled blocks). |
| `rust/guit` | WinRT platform layer (`Windows.Web.Http`, credential locker, launcher, clipboard) and one reactor component with three sections. **Inbox**: Unread/All/Participating, kind box, mark as done, Load more, a refresh every minute. **Pull requests**: three scopes. **Conversations**: formatted comments, reactions, reply box, comment paging, and for pull requests a header (state, branches, diff size) and a Files changed view with coloured diffs. **Actions**: repository box, runs, a run's jobs and steps that follow a live run, re-run, job log. Files: `main.rs` (state, messages, update, sign-in and inbox views), `pulls.rs`, `actions.rs`, `platform.rs`. |
| CI (`ci.yml`, job `rust`) | Builds the release exe for x64 and ARM64, stages it with its runtime files (`tools/stage-rust.ps1`), launches it and times start-up with `tools/measure-startup.ps1`, the same script the C# job uses. Green. |
| Manual builds (`release.yml`) | Run from the Actions tab with a `version`. Builds both apps at one commit for x64 and ARM64 and uploads `guit-cs-<version>-<rid>.zip` and `guit-rs-<version>-<rid>.zip`, each with the app, `measure-startup.ps1` and a README. Ticking `publish_release` also creates a GitHub Release tagged `v<version>`. Run once with 0.1.0 (artifacts only): green. |
| Numbers | `docs/benchmarks.md`, section "C# and Rust, side by side". |

**Verified**: the sign-in view and inbox build link, start and show a window on Windows x64 and ARM64 in CI,
signed out. Everything type-checks for Windows.
**Not verified**: nobody has signed in with the Rust app, so none of its pages has been seen with data (plan
card 6.6). The UI code was written from the reactor samples. Expect layout and behaviour fixes on first real use.
The pull requests, conversation and Actions pages were pushed on 11 November 2026 while GitHub was not starting
CI runs for this repository (an Actions outage, and the account then ran out of build minutes; the owner said to
ignore CI for now). They type-check for Windows but have never been linked or launched, so check a CI run once
minutes are available before trusting them.
The `publish_release` path of `release.yml` has not been exercised either.

Known gaps against the C# app: reactor's rich text has no monospace, strikethrough or indent yet, so comments
approximate them with characters; lists load more by button instead of on scroll (reactor's ListView has no
"near the end" event); the inbox polls with If-Modified-Since and follows X-Poll-Interval, as the C# app does. Settings and shortcuts exist; the settings item sits at the end of the main
menu (as in the reactor gallery), not in the pane footer as in the C# app.

### Facts worth knowing

- windows-reactor is in `microsoft/windows-rs` (`crates/libs/reactor`). crates.io has **0.100.0**; master is
  **0.101.0** with a changed API. The workspace pins every windows-rs crate to **git revision `0cfb708`** of
  master because the samples at that revision match the API used (`crates/samples/reactor/`: `navigation`,
  `virtual`, `async-state`, `gallery`, `self_contained`). The API surface is listed in
  `crates/libs/reactor/public-api.txt`. Move to crates.io when 0.101 is published.
- In this version of the `windows` crate, Win32 features are named after headers (`combaseapi`, not
  `Win32_System_Com`) and `HSTRING` converts with `to_string_lossy()`.
- `windows-reactor-setup::as_self_contained()` in `rust/guit/build.rs` stages a private Windows App Runtime beside
  the exe. It needs Windows tools, so it only runs when the build host is Windows.
- A reactor app is framework-dependent by default, so a single-exe Rust build is possible against an installed
  Windows App Runtime. Both apps ship self-contained instead, for a fair comparison, which means a zip each.
- Both apps use the same OAuth App and the same Credential Locker entry (resource `Guit`, user `github`):
  signing in with one signs in both.
- On Linux: `rustup target add x86_64-pc-windows-msvc`, then `cargo check -p guit --target x86_64-pc-windows-msvc`
  type-checks the Windows code. The first run compiles the `windows` crate and takes several minutes.

### Next steps

Plan cards 6.6 and 6.10 in `docs/PLAN.md`: get the owner to try the Rust app signed in and fix what that
shows; then measure the signed-in scenarios on one machine. Add a `rust` skill under `.claude/skills/` once a second
page has proven the patterns. Repeat the ARM64 measurement: C# start-up there varied from 153 to 416 ms in one run.

## Environment notes for an agent in a cloud session

- The .NET SDK is not preinstalled: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 11.0 --install-dir /opt/dotnet`.
- `gh api repos/leonio/linked/actions/jobs/<id>/logs` works for reading CI logs; the start-up numbers are on the lines starting `Start-up (` and `Guit.exe:`.
- CI cancels an in-progress run on the same branch when a new push arrives, so wait for a run you care about before pushing again.
- `tools/ghapi.py` looks up GitHub API operations and schemas without loading the 14 MB description.
