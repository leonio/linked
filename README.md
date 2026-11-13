# Guit

A native Windows client for GitHub - inbox, conversations, Actions. The experiment: how close can C# and
Microsoft's current tooling get to a C++/WinRT-style native app? To measure that, the same app is being built twice,
side by side in this repository:

| | C# app (`dotnet/`) | Rust app (`rust/`) |
|---|---|---|
| UI | WinUI 3, `x:Bind` compiled bindings | windows-reactor |
| Runtime | .NET 11 **Native AOT**, no reflection | native code, windows-rs |
| HTTP / JSON | Refit 16 (generated only), System.Text.Json source generation | `Windows.Web.Http` behind a `Transport` trait |
| Windows App SDK | 2.x, unpackaged, self-contained | self-contained Windows App Runtime |
| Sign-in | GitHub device flow: no client secret in the app (both apps share one sign-in) | |

## Status

- **C# app:** the main one. Sign-in, inbox, pull requests, conversations (with diffs and reactions) and Actions
  work on Windows; the owner has run it. The roadmap is [docs/PLAN.md](docs/PLAN.md).
- **Rust app:** follows the C# app feature by feature. It builds, launches and is timed in CI on x64 and ARM64, but
  nobody has used it signed in yet, so expect layout and behaviour fixes. State of play: [docs/HANDOVER.md](docs/HANDOVER.md).

## Downloads

Every green build of `main` replaces the **[latest](../../releases/tag/latest)** pre-release with four
self-contained zips (nothing to install) and their SHA-256 sums: the C# and the Rust app, each for x64 and ARM64.
Each zip includes `measure-startup.ps1` and `measure-inbox.ps1`. Versioned builds of both apps at one commit come from
the manually started "Side-by-side builds" workflow.

A single-file `.exe` is not possible: a WinUI 3 / Windows App SDK self-contained app needs its runtime files beside the exe,
and Native AOT does not support single-file publish. The Rust app has the same runtime files.

## Try it out

You do not need to register anything or build anything. Both apps sign in with GitHub's
[device flow](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow)
through one public OAuth App, so there is no client secret on your machine.

1. **Download** the zip for your machine from the [latest release](../../releases/tag/latest): `win-x64` for most PCs,
   `win-arm64` for Arm PCs (Copilot+ PCs, Surface Pro X). Pick the C# app, the Rust app, or both. Check it against
   `SHA256SUMS.txt` if you like. Needs Windows 10 version 2004 (build 19041) or later, and a CPU from roughly 2009 or newer
   (x64 with the x86-64-v2 instructions, which includes SSE4.2 and POPCNT; Arm64 with LSE): .NET 11 refuses to start on older
   hardware, and the C# app is built on it.
2. **Unzip** it anywhere and run `Guit.exe` (C#) or `guit.exe` (Rust). Nothing to install. The builds are not
   code-signed yet, so Windows SmartScreen may warn: choose *More info* > *Run anyway*.
3. **Sign in**: click *Sign in with GitHub*. The app shows a short code, copies it to the clipboard and opens
   <https://github.com/login/device>. Paste the code, review what is requested, and click *Authorize*. The app
   notices on its own and opens your inbox.
4. **Look around**: Inbox, Pull requests, Conversations and Actions. Both apps share one sign-in, so signing in with
   one signs in the other.

**What you are authorizing.** The app asks for the scopes `notifications repo read:user`: your notifications, your
profile, and access to your repositories, including private ones. GitHub has no narrower scope that covers marking
notifications done, commenting and re-running workflows. The app talks only to GitHub's API and sends nothing
elsewhere. The token is stored in the Windows Credential Locker, encrypted for your Windows user.
If your organization restricts third-party OAuth Apps, its owner has to approve the app (or you stay with your
personal repositories) before the org's repositories appear.

**Taking it back.** Sign out in the app (C#: right-click your account in the sidebar > *Sign out*), and to revoke
the authorization itself, go to GitHub > Settings > Applications > *Authorized OAuth Apps* > Guit > *Revoke*.

### Use your own OAuth App instead

If you would rather not authorize the project's OAuth App, register your own. It takes two minutes and needs no secret:

1. GitHub > Settings > Developer settings > OAuth Apps > *New OAuth App*. Any name; Homepage URL: this repository;
   Authorization callback URL: `http://127.0.0.1` (required but unused).
2. Open the new app and tick **Enable Device Flow**. Copy the **Client ID**. Do not create a client secret.
3. Give the app your client id:
   - **C#**: set the environment variable `GUIT_GITHUB_CLIENT_ID` before starting `Guit.exe`
     (PowerShell: `$env:GUIT_GITHUB_CLIENT_ID = "<client id>"; .\Guit.exe`).
   - **Rust**: change `GITHUB_CLIENT_ID` in `rust/guit-core/src/lib.rs` and rebuild (see Build). The Rust app has no
     environment override yet.

## Build

C# (run from `dotnet/`; the WinUI app builds on Windows only):

```bash
cd dotnet
dotnet test --project tests/Guit.Api.Tests
dotnet test --project tests/Guit.Core.Tests
```

On Windows:

```bash
cd dotnet
dotnet publish src/Guit.App -c Release -r win-x64 -p:Platform=x64
```

Then run `dotnet\artifacts\publish\Guit.App\release_win-x64\Guit.exe`.

Rust (run from `rust/`):

```bash
cd rust
cargo test -p guit-core                                  # the GitHub client and view logic; runs on any OS
cargo build --release -p guit --target x86_64-pc-windows-msvc   # the app; Windows only
```

`cargo check -p guit --target x86_64-pc-windows-msvc` type-checks the Windows app from any OS. To collect the Rust exe
with its runtime files, use `tools/stage-rust.ps1`.

## Early findings

Numbers from GitHub-hosted CI runners, both apps built from the same commit and measured one after the other with
`tools/measure-startup.ps1` ([docs/benchmarks.md](docs/benchmarks.md) has the method and the caveats). Both apps start
signed out, so this is each app's shell and sign-in view.

| Runner | App | Start-up (median) | Memory (working set) | Executable | App folder |
|---|---|---|---|---|---|
| x64 | C# | 158 ms | 77 MB | 13.9 MB | 132.8 MB |
| x64 | Rust | 124 ms | 67 MB | 2.4 MB | 60.4 MB |
| ARM64 | C# | 167 ms | 85 MB | 14.1 MB | 141.5 MB |
| ARM64 | Rust | 138 ms | 76 MB | 2.3 MB | 67.7 MB |

(Run of 13 November 2026 with .NET 11 Runtime Async on.)

**Memory:** at the sign-in view the Rust app's working set is about **10 MB lower** (67 vs 77 MB on x64, 76 vs 85 MB
on ARM64), roughly 10-13%. Most of what both apps hold is the Windows App Runtime and UI framework they each load, which
is why the gap is small next to the 7x difference in executable size. Treat it as a first indication, not a result:

- it is one run per runner, on shared virtual machines that vary by about 20% between runs;
- it was read two seconds after the window appeared, before any data is loaded;
- the C# app has far more features, so more code is loaded; the apps are not like for like yet.

Memory with a signed-in inbox, a few hundred notifications loaded and under scrolling has **not** been measured: CI
cannot sign in. `tools/measure-inbox.ps1` does that on a person's machine and reports launch to inbox, launch to window
and the working set with the inbox loaded.

Other first findings: Rust starts about 30 ms (17-22%) faster on both architectures, and the ranges in the latest run do not
overlap; the clearest difference is size, with the Rust executable at 2.4 MB against 13.9 MB. Switching on .NET 11 Runtime
Async changed nothing measurable in the C# app at this view (same memory and size, start-up within 2 ms on x64), which is
expected for a screen with almost no async work. Only anything signed in is still to be measured.

## Working in this repository

Start with [CLAUDE.md](CLAUDE.md). The rules for each layer are in `.claude/skills/`.
