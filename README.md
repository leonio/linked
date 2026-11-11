# Guit

A native Windows client for GitHub - inbox, conversations, Actions - written in C# with WinUI 3 and compiled with
Native AOT. The experiment: how close can C# and Microsoft's current tooling get to a C++/WinRT app?

- **.NET 11, WinUI 3 (Windows App SDK 2.x), Native AOT**, unpackaged and self-contained
- **Refit 16** generated-only HTTP clients, **System.Text.Json** source generation - no reflection anywhere
- **CommunityToolkit.Mvvm** source generators, `x:Bind` compiled bindings only
- Sign-in with GitHub's device flow: no client secret in the app

## Status

Sign-in works end to end in code and tests; the shell starts as a native executable in CI.
Nobody has yet signed in interactively on a Windows machine - see card 1.4 in [docs/tasks/phase-1-sign-in.md](docs/tasks/phase-1-sign-in.md).
The roadmap is [docs/PLAN.md](docs/PLAN.md).

## Build

```bash
dotnet test --project tests/Guit.Api.Tests
dotnet test --project tests/Guit.Core.Tests
```

On Windows:

```bash
dotnet publish src/Guit.App -c Release -r win-x64 -p:Platform=x64
```

Then run `artifacts\publish\Guit.App\release_win-x64\Guit.exe`. Set a GitHub OAuth App client id first
(card 1.4 explains how).

## Working in this repository

Start with [CLAUDE.md](CLAUDE.md). The rules for each layer are in `.claude/skills/`.
