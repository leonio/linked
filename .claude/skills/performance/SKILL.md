---
name: performance
description: Performance rules and budgets for this app (start-up, lists, images, memory, HTTP, AOT publish settings) and how to measure them. Load when building any list, image, page, start-up code, or when something feels slow.
---

# Performance

The goal is a C# app that is hard to tell from a C++/WinRT one. That is won or lost in a few places.
Rules first, measurement after. **Never claim something is faster without a number from "Measure" below.**

## Budgets (Release, Native AOT, win-x64)

| Metric | Budget | Where it is reported |
|---|---|---|
| `Guit.exe` size | CI prints it; do not grow it more than 5% in one task without saying why | CI job summary "Report binary size" |
| Start-up: process start to window shown | < 400 ms on a normal laptop | manual, see Measure |
| Working set after start, signed out | CI prints it; watch the trend | CI job summary "Launch smoke" |
| List scrolling | no dropped frames with 1,000 items | manual |

## Start-up

- Nothing synchronous and slow before the window is shown: no HTTP, no file reads, no `Task.Wait`.
  `App()` builds the DI container; `OnLaunched` creates and activates the window; data loads after, from a command.
- DI: singletons are created on first use. Do not resolve services "to warm them up".
- Do not create UI that is not visible. Use `x:Load="False"` / `x:Load="{x:Bind ...}"` for panels shown later
  (see `Views/ShellView.xaml`), and navigate to pages on demand.
- One window, one `Frame`. Pages that are expensive to rebuild set `NavigationCacheMode="Required"`.

## Lists (the feed is a list; this matters most)

- Use `ListView` or `ItemsRepeater` (inside a `ScrollView`). Both virtualise. **Never** put a list inside a
  `StackPanel` or a `ScrollViewer` with unbounded height: that turns virtualisation off and creates every item.
- Never bind a list to `ItemsControl`, and never build item UI in code-behind loops.
- Item templates: `x:DataType` + `{x:Bind}` only, default mode (OneTime) unless the value really changes.
- Keep templates shallow: a `Grid` with rows/columns instead of nested `StackPanel`s. Aim for under ~12 elements per item.
- Heavy parts of an item (avatars, secondary text) get `x:Phase="1"` or higher so the first pass draws text only.
- No `IValueConverter`, no per-item event handlers created in code, no per-item `ToolTip` objects that are rarely seen.
- Page through data (50 at a time). Do not load 1,000 items to show 20.
- Update in place. Do not `Clear()` and refill a collection the user is looking at when only a few items changed.

## Images

- Always decode at display size: `Bind.Image(url, size)` in `Guit.App/Xaml/Bind.cs` sets `DecodePixelWidth`.
  A 460 px avatar shown at 32 px otherwise costs ~200x the memory.
- Ask GitHub for a small avatar: append `&s=64` (or `?s=64`) to `avatar_url` in the item model.
- Never load images in a loop yourself; let `Image`/`ImageIcon`/`PersonPicture` and virtualisation do it.

## Memory and allocation (Core and Api)

- DTOs carry only the fields used (see `api-endpoint`). Fewer fields = less parsing and less garbage.
- No LINQ, closures, `string.Format`, or boxing in code that runs per item. `foreach`, interpolated strings, generics.
- `static` lambdas where a lambda captures nothing. `sealed` classes. `readonly record struct` for small values.
- Arrays (`T[]`) for API results. `ObservableCollection<T>` only for what the UI binds to.
- Strings that repeat across items (repo names, reasons) are fine as-is; do not build caches without a measurement.

## HTTP

- One handler configuration (`GuitHttp.CreateHandler`): HTTP/2, pooled connections, Brotli/gzip. Do not create `HttpClient`s by hand.
- Always pass the `CancellationToken`; cancel the previous load when a new one starts or the page is left.
- Do not fetch per list item (N+1). If an item needs more data, fetch it when the item is opened.
- Respect rate limits: poll no faster than GitHub's `X-Poll-Interval` header says.

## AOT publish settings

They live in `src/Guit.App/Guit.App.csproj` under the "Native AOT" comment. Do not change them as part of
another task. A change needs before/after numbers for exe size and start-up time in the commit message.

## Measure

- **Size and working set**: push the branch; read the CI job summary of the "WinUI app" job.
- **Start-up time** (on Windows, Release AOT publish):
  ```powershell
  dotnet publish src/Guit.App -c Release -r win-x64 -p:Platform=x64
  1..5 | % { $t = [Diagnostics.Stopwatch]::StartNew(); $p = Start-Process artifacts\publish\Guit.App\release_win-x64\Guit.exe -PassThru; while ($p.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 5; $p.Refresh() }; $t.ElapsedMilliseconds; Stop-Process $p }
  ```
  Report the median of five runs, and ignore the first run.
- **Never measure Debug or JIT builds.** They say nothing about the shipped app.
- If you are not on Windows you cannot measure the app. Say so; do not estimate.
