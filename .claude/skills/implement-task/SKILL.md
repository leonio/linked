---
name: implement-task
description: How to pick up, implement, verify and hand over one task card from docs/tasks in this repo. Load at the start of any task from docs/PLAN.md and again before you say you are done.
---

# Implementing a task card

## Start

1. Open `docs/PLAN.md`. Take the first task that is `[ ]` and whose "Depends on" tasks are all `[x]`,
   unless you were told which task to do.
2. Read the whole task card in `docs/tasks/`. Read every file it lists under "Read first".
3. Load the skills named in the card (`CLAUDE.md` has the table).
4. If the card is unclear or contradicts the code, stop and ask. Do not guess and do not redesign.

## While working

- Do exactly the card. Things you notice outside it go in your final report, not in the diff.
- Copy the nearest existing example (the card names it). Same file layout, same naming, same test style.
- Do not rename, move or reformat files the card does not mention.
- Do not edit `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, the AOT block in
  `Guit.App.csproj`, or `.github/` unless the card says so.
- Layer order for a feature: Api (endpoint + test) -> Core (view model + test) -> AotSmoke -> App (XAML).
  Get each layer green before starting the next.

## Verify (all of it, every time)

```bash
dotnet test --project tests/Guit.Api.Tests
dotnet test --project tests/Guit.Core.Tests
dotnet publish tests/Guit.AotSmoke -c Release -r linux-x64
./artifacts/publish/Guit.AotSmoke/release_linux-x64/Guit.AotSmoke
```

On Windows also:

```bash
dotnet build src/Guit.App -c Release -p:Platform=x64
```

Zero warnings, all tests pass, smoke prints `AOT smoke: all checks passed`.
If you changed `src/Guit.App` and are not on Windows, you have not verified it: push and read the CI result,
or say plainly that the XAML is unverified.

## Finish

1. Tick the task in `docs/PLAN.md` (`[ ]` -> `[x]`). If part of it is not done, leave it unticked and say what is missing.
2. Commit: imperative subject under 72 characters, e.g. `Add inbox view model`. Body: what and why in a few lines.
   **No trailers: no `Co-Authored-By`, no `Claude-Session` link.** One task = one commit where practical.
3. Report, in this order:
   - what you built (files),
   - the verification output (test counts, smoke result, whether the app was built on Windows),
   - anything you could not verify,
   - anything you noticed that the card did not cover.

## Stop and ask when

- a warning cannot be fixed with the table in `aot-rules`,
- the task seems to need a new package (`dependencies`),
- the OpenAPI description disagrees with the card,
- you would need to change a public type that other finished tasks use,
- a test would need the real network.
