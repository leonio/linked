# Phase 5 - Speed and shipping

Most of these are **[S]** cards: they need measurement, judgement, or a Windows machine. They are described as
goals and acceptance criteria, not as step-by-step recipes.

---

## 5.1 [S] Start-up time measured in CI, with a budget

The launch smoke test in `.github/workflows/ci.yml` already starts the exe and reports working set.
Extend it: start the exe 6 times, time process start -> `MainWindowHandle != 0`, drop the first run, report the median
in the job summary, and fail the job if the median exceeds a budget. Set the budget from five CI runs of the current
main branch (median + 30%), and write the number into the `performance` skill. Hosted runners are noisy: if the spread
between runs is larger than the budget margin, report the numbers and do not add the failing gate.

## 5.2 [S] Conditional requests for polling

GitHub answers `304 Not Modified` (free against the rate limit) when a poll sends `If-Modified-Since` / `If-None-Match`,
and tells clients how often to poll with `X-Poll-Interval`. Needs a Refit return type that exposes status and headers
(`ApiResponse<T>` / `IApiResponse<T>`): first prove with a one-method spike that the generator supports it without RF006
and that it survives the AOT smoke test. Then use it for the inbox poll (card 2.9). Acceptance: a poll with no changes
makes one request, receives 304, allocates no item models, and the poll period follows `X-Poll-Interval`.

## 5.3 Settings page: theme, account, about

**Layer** Core + App - **Skills** `viewmodel`, `winui-xaml` - **Depends on** 2.4

`SettingsViewModel` (singleton): `[ObservableProperty] AppTheme Theme` (`System`, `Light`, `Dark`), the signed-in login,
`SignOutCommand`, version string. Core `ISettingsStore` (`string? Get(string key)`, `void Set(string key, string value)`),
implemented in App with a small JSON file under `Environment.SpecialFolder.LocalApplicationData/Linked/settings.json`
using a `[JsonSerializable(typeof(Dictionary<string, string>))]` context of its own in Linked.App.
App `SettingsPage` using a `RadioButtons` for theme; apply with `((FrameworkElement)window.Content).RequestedTheme`.
Turn on `IsSettingsVisible` in `ShellView` and map `Route.Settings`.
(`ApplicationData.Current` needs package identity; the app is unpackaged, so do not use it.)

## 5.4 Keyboard and accessibility pass

**Layer** App - **Skills** `winui-xaml`

For every page: tab order follows reading order; every icon-only button has `AutomationProperties.Name` and a `ToolTipService.ToolTip`;
list items expose a useful `AutomationProperties.Name` (bind it to a pre-computed string on the item model);
`F5` refresh, `Alt+Left` back, `Ctrl+1..3` switch sections, `Esc` leaves a conversation. Check with Narrator and
Accessibility Insights on Windows, and in High Contrast. Not verifiable off Windows.

## 5.5 ARM64 build in CI

Add a matrix to the `app` job: `win-x64` on `windows-latest`, `win-arm64` on `windows-11-arm`. Both publish with AOT,
both run the launch smoke test, both report size. If the ARM runner is not available to this repository, cross-publish
`-r win-arm64` on the x64 runner and skip the launch step for that leg.

## 5.6 [S][H] MSIX packaging and signing

The app is unpackaged (`WindowsPackageType=None`). Packaging gives clean install/uninstall, a Start tile, and package
identity (needed for toast notifications with activation). Needs: `Package.appxmanifest`, logo assets, a signing
certificate (human), and a decision on distribution (Store, winget, or sideload). Keep the unpackaged build working:
it is what CI launches and what performance is measured on.

## 5.7 [S] .NET 11 and CsWinRT 3

When .NET 11 and CsWinRT 3.0 are both stable and the Windows App SDK supports them: change `LinkedNetVersion` in
`Directory.Build.props`, update `global.json`, update packages. Report exe size, start-up median and working set
before and after. CsWinRT 3 was rebuilt around AOT, so this is expected to be the single largest free improvement;
it is also a breaking change (new TFM revision, span-based projections), so read its migration notes first.

## 5.8 [S] Head-to-head with C++/WinRT

The question this project exists to answer. Build the inbox page only (sign-in can be a pasted token) as a C++/WinRT
WinUI 3 app using the same XAML, and compare against this app: exe size, start-up median, working set after loading
200 notifications, and frame times while scrolling (Windows Performance Recorder). Write the results up in
`docs/benchmarks.md` with the method, the machine, and the raw numbers.
