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
| x64 | C# | 160 ms | 77 MB | 13.9 MB | 132.8 MB |
| x64 | Rust | 139 ms | 66 MB | 1.9 MB | 60.0 MB |
| ARM64 | C# | 339 ms (erratic) | 85 MB | 14.1 MB | 141.5 MB |
| ARM64 | Rust | 143 ms | 77 MB | 1.9 MB | 67.3 MB |

**Memory:** at the sign-in view the Rust app's working set is about **10 MB lower** (66 vs 77 MB on x64, 77 vs 85 MB
on ARM64), roughly 10-14%. Most of what both apps hold is the Windows App Runtime and UI framework they each load, which
is why the gap is small next to the 7x difference in executable size. Treat it as a first indication, not a result:

- it is one run per runner, on shared virtual machines that vary by about 20% between runs;
- it was read two seconds after the window appeared, before any data is loaded;
- the C# app has far more features, so more code is loaded; the apps are not like for like yet.

Memory with a signed-in inbox, a few hundred notifications loaded and under scrolling has **not** been measured: CI
cannot sign in. `tools/measure-inbox.ps1` does that on a person's machine and reports launch to inbox, launch to window
and the working set with the inbox loaded.

Other first findings: start-up on x64 is about the same (Rust about 13% faster, inside the run-to-run variation); on
ARM64 the C# app's start-up was erratic (153-416 ms) while Rust stayed at 138-153 ms, which is the first thing to repeat;
and the clear difference is size, with the Rust executable at 1.9 MB against 13.9 MB.

## Working in this repository

Start with [CLAUDE.md](CLAUDE.md). The rules for each layer are in `.claude/skills/`.
