# Triage

A native Windows client for GitHub (inbox, conversations, Actions) written in C#.
The point of the project: see how close C# + WinUI 3 + Native AOT can get to a C++/WinRT app.
**Fast and AOT-clean beat everything else.** When a choice is between convenient and fast, pick fast.

## Before you write code

1. Find your task in `docs/PLAN.md` and read its section in `docs/tasks/`. Do exactly that task.
2. Load the skill for the layer you are touching (they are short, and they are rules, not advice):

| You are touching | Skill |
|---|---|
| anything at all | `aot-rules` |
| `src/Triage.Api` (endpoints, DTOs, JSON) | `api-endpoint` |
| `src/Triage.Core` (view models, services) | `viewmodel` |
| `src/Triage.App` (XAML, pages, controls) | `winui-xaml` |
| lists, images, start-up, anything "slow" | `performance` |
| adding or upgrading a NuGet package | `dependencies` |
| starting / finishing a task | `implement-task` |

## Layout

```
src/Triage.Api    net10.0            Refit interfaces, DTO records, JSON context. No UI. Builds anywhere.
src/Triage.Core   net10.0 + windows  View models, services, interfaces the app implements. No XAML types.
src/Triage.App    windows only       WinUI 3 views, Windows-only services, composition root.
tests/Triage.Api.Tests               TUnit + Refit.Testing (StubHttp). No network, ever.
tests/Triage.Core.Tests              TUnit + hand-written fakes (Fakes.cs).
tests/Triage.AotSmoke                Native AOT console app that runs the real DI graph. The AOT truth test.
tools/ghapi.py                       Look up operations and schemas in GitHub's OpenAPI description.
docs/PLAN.md, docs/tasks/            The roadmap and the task cards.
```

Dependencies point one way: `App -> Core -> Api`. Api types (DTOs) stop at Core: XAML binds to Core types only.

## Commands

```bash
dotnet test --project tests/Triage.Api.Tests
dotnet test --project tests/Triage.Core.Tests
dotnet publish tests/Triage.AotSmoke -c Release -r linux-x64 && ./artifacts/publish/Triage.AotSmoke/release_linux-x64/Triage.AotSmoke
```

On Windows use `-r win-x64` and `artifacts\publish\Triage.AotSmoke\release_win-x64\Triage.AotSmoke.exe`.
The WinUI app only builds on Windows:

```bash
dotnet build src/Triage.App -c Release -p:Platform=x64
dotnet publish src/Triage.App -c Release -r win-x64 -p:Platform=x64
```

If you are not on Windows you cannot build `Triage.App`. Say so in your report; CI (`.github/workflows/ci.yml`)
builds it, publishes it with Native AOT, and launches the exe. Never claim the app works because it compiled.

## Hard rules

- Warnings are errors. Never add `NoWarn`, `#pragma warning disable`, `[UnconditionalSuppressMessage]`,
  or `[DynamicDependency]` to get a build through. An IL2xxx/IL3xxx/RF006/CsWinRT warning means the design is wrong.
- No reflection, no `dynamic`, no `Activator.CreateInstance`, no `Type.GetType(string)`, no `{Binding}`.
- No new NuGet package without following the `dependencies` skill.
- No network in tests.
- Framework and package versions live only in `Directory.Build.props` and `Directory.Packages.props`.
- Commit messages: imperative subject, short body. **No trailers: never add `Co-Authored-By` or `Claude-Session` lines.**
