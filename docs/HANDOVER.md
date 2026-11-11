# Handover

Written on 11 November 2026 for whoever picks this up next. Read `CLAUDE.md` first; this file is the state of play.

## What this repository is

**Guit**: a native Windows client for GitHub (inbox, conversations, pull request diffs, Actions).
The point is a performance experiment: how close can C# + WinUI 3 + Native AOT get to a native app?
A second implementation in Rust has just been started so the two can be measured side by side.

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
| Phase 5 | Complete except 5.6 (MSIX: needs a signing certificate and a distribution decision), 5.7b (CsWinRT 3: still a preview) and 5.8 (the native comparison, now being answered with Rust) |
| Open feature cards | 3.11 browse repository code, 3.12 syntax colouring, 4.8 live log of a running job |
| Tests | 221 (TUnit), plus a Native AOT smoke test of the non-UI layers |
| CI | `.github/workflows/ci.yml`: Linux tests + AOT smoke; Windows x64 and ARM64 build, AOT publish, launch, start-up timing with a budget |
| Target | .NET 11, Windows App SDK 2.6.5, Refit 16.4, CommunityToolkit.Mvvm 8.4.2 |

Measured numbers and the method are in `docs/benchmarks.md` (x64 about 190-210 ms from process start to window on a CI runner).

**Not yet seen running by anyone** (CI launches the app signed out, and the owner's last test predates them):
the settings page, Ctrl+1..3 and Esc shortcuts, step rows and the job log page, the "Files changed" diff page,
and the inbox kind filter. The owner then said "it all works locally", which probably covers these, but it was not itemised.

How to work on it: `CLAUDE.md` maps each layer to a skill in `.claude/skills/`; those skills contain the
rules and the compiler traps already hit. Without Windows you cannot build `src/Guit.App`; push and read CI.

## The Rust app: just started (this is the live work)

The owner's request, verbatim in intent:

1. Build the same app in Rust with **windows-rs** and **windows-reactor**, side by side in this repository,
   so C# AOT and Rust can be compared.
2. Builds that can be **started manually** and publish **versioned** downloadable files (a single file, or a
   zip where needed) for both apps, so they can be tested and measured side by side.

### Facts established

- windows-reactor is merged in `microsoft/windows-rs` (`crates/libs/reactor`) and published: crates.io has
  **0.100.0**; the repository's master is **0.101.0** with a changed API (for example `context.message(...)`
  on master versus `context.forward()` in 0.100.0).
- The workspace pins every windows-rs crate to **git revision `0cfb708`** of master, because the samples
  at that revision (`crates/samples/reactor/`: `navigation`, `virtual`, `async-state`, `gallery`,
  `self_contained`, `startup_perf`) match the API being used. A read-only clone for reference can be made with
  `git clone --depth 1 https://github.com/microsoft/windows-rs`. The full API surface is listed in
  `crates/libs/reactor/public-api.txt`.
- `windows-reactor-setup::as_self_contained()` in `build.rs` stages a private Windows App Runtime beside the
  exe (the equivalent of the C# app's `WindowsAppSDKSelfContained`). It needs Windows `curl.exe`/`tar.exe`,
  so `rust/guit/build.rs` only calls it when the build host is Windows.
- Reactor apps are framework-dependent by default, so a single-exe Rust build is possible if the Windows App
  Runtime is installed. The C# app cannot be a single file while self-contained. For a fair comparison both
  should ship self-contained, which means **a zip each**.
- Rust 1.95+ is required. On Linux, `rustup target add x86_64-pc-windows-msvc` then
  `cargo check -p guit --target x86_64-pc-windows-msvc` type-checks the Windows code without a linker.

### What is in `rust/`

| Path | State |
|---|---|
| `rust/Cargo.toml` | Workspace, pinned git dependencies, release profile (opt-level 3, fat LTO, 1 codegen unit, panic=abort, stripped). |
| `rust/guit-core/` | **Done and tested: 20 tests pass on Linux** (`cargo test -p guit-core`). Mirrors `Guit.Api` + `Guit.Core`: GitHub client over a `Transport` trait (device flow, user, notifications, mark read), device-flow polling, relative time, subject links, inbox rows. No Windows dependency. |
| `rust/guit/src/platform.rs` | **Written, never compiled.** WinRT HTTP transport (`Windows.Web.Http`), credential locker, launcher, clipboard. |
| `rust/guit/src/main.rs` | **Written, never compiled.** One reactor component: sign-in view (device flow), then a shell with the inbox (Unread/All, refresh, click opens the browser and marks read, sign out). |
| `rust/guit/build.rs`, `Cargo.toml` | Written, never built. |

The type-check of `rust/guit` was interrupted before it produced output, so **expect compile errors** in
`main.rs` and `platform.rs`. Likely spots: the name of the blocking wait on WinRT async operations (`.join()`
was assumed; older versions call it `.get()`), iterating the reply headers map, `windows` crate feature names,
builder methods guessed from memory (`font_weight(FontWeight::SEMI_BOLD)`, `text_trimming`, `max_width`,
`column_spacing`, `Symbol::Mail`, `WindowBackdrop::Mica`), and whether layout modifiers such as `.grid_row(1)`
can be called on an already-converted `View` (the `error_bar` helper returns `View`).

Design choices already made, so the two apps stay comparable:

- Both use the **same OAuth App** (client id in `guit-core/src/lib.rs` and `src/Guit.App/AppConfig.cs`) and the
  **same Credential Locker entry** (resource `Guit`, user `github`), so signing in with one signs in both.
- Rust HTTP goes through WinRT `HttpClient`, not a Rust TLS stack, to keep the binary small and Microsoft-only.
- Background work uses reactor's `spawn_background`; `platform::initialize()` calls `CoIncrementMTAUsage` so
  those threads can use WinRT.
- Dependencies: windows-rs crates plus `serde`/`serde_json`. Same policy as the C# side: nothing small or obscure.

### Next steps, in order

1. Get `rust/guit` to type-check: `cd rust && cargo check -p guit --target x86_64-pc-windows-msvc`.
   The first run downloads and compiles the `windows` crate and takes several minutes.
2. Add Rust to `.github/workflows/ci.yml`: `cargo test -p guit-core` on Linux; on Windows (x64, and ARM64 if it
   builds) `cargo build --release -p guit`, then the **same** start-up timing loop the C# job uses. Move that
   PowerShell loop into a script (for example `tools/measure-startup.ps1 -Exe <path> -BudgetMs <n>`) so both
   apps, and the owner locally, measure identically. Report both in the job summary.
3. Add the manual build the owner asked for: a `workflow_dispatch` workflow (it must live on `main` to be
   startable) with a `version` input, producing `guit-cs-<version>-win-x64.zip` and
   `guit-rs-<version>-win-x64.zip` (and ARM64), each containing the app folder plus the measuring script.
   Upload them as artifacts; optionally create a GitHub Release when an input says so (`permissions: contents: write`).
   Stamp the version into the C# build with `-p:Version=` and into the Rust build from the same input.
   Drop `.pdb` files from the C# zip.
4. Record the first side-by-side numbers in `docs/benchmarks.md`, with the caveat that the Rust app has far
   fewer features, so only like-for-like scenarios (signed-out start, inbox list) are comparable.
5. Add a `rust` skill under `.claude/skills/` once the patterns are proven, and add `rust/` to the layout in `CLAUDE.md`.
6. Grow the Rust app toward parity in the C# order: pull requests, conversations, Actions.

### Things to tell the owner

- Whether to stay pinned to windows-rs master or wait for 0.101 on crates.io is their call; pinning was chosen to move now.
- A fair size comparison needs both self-contained; a single-file Rust exe is possible only against an installed runtime.
- Nothing in `rust/guit` has been built or run. Do not describe it as working until CI has launched it.

## Environment notes for an agent in a cloud session

- The .NET SDK is not preinstalled: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 11.0 --install-dir /opt/dotnet`.
- `gh api repos/leonio/linked/actions/jobs/<id>/logs` works for reading CI logs; the start-up numbers are on the lines starting `Start-up (` and `Guit.exe:`.
- CI cancels an in-progress run on the same branch when a new push arrives, so wait for a run you care about before pushing again.
- `tools/ghapi.py` looks up GitHub API operations and schemas without loading the 14 MB description.
