# Benchmarks

What has been measured so far, how, and what has not. Every number here comes from the CI job
"WinUI app (build, Native AOT publish, launch)" in `.github/workflows/ci.yml`; nothing is estimated.

## Method

- **Build**: `dotnet publish src/Guit.App -c Release -r <rid>` (Native AOT, unpackaged, self-contained Windows App SDK).
- **Start-up**: the published `Guit.exe` is started six times. Each run is timed from process start until the
  process has a main window handle. The first run is discarded (cold disk cache); the median of the other five is reported.
  The app starts signed out, so this is the shell and the sign-in view: no network, no list.
- **Working set**: read two seconds after the window appears.
- **Size**: `Guit.exe` alone. The publish folder is much larger because it carries the self-contained
  Windows App SDK runtime and debug symbols.
- **Machines**: GitHub-hosted runners `windows-latest` (x64) and `windows-11-arm` (ARM64). They are shared virtual
  machines: expect roughly 20% variation between runs, and do not read these as numbers for a laptop.

## Results

| Build | Runner | Start-up median (min-max) | Working set | `Guit.exe` |
|---|---|---|---|---|
| .NET 10, run 40 | x64 | 211 ms (203-240) | 75 MB | 13.2 MB |
| .NET 10, run 40 | ARM64 | 294 ms (232-386) | 84 MB | 13.3 MB |
| .NET 11, run 42 | x64 | 187 ms (180-219) | 77 MB | 13.9 MB |
| .NET 11, run 42 | ARM64 | 261 ms (170-342) | 86 MB | 14.1 MB |

Reading the .NET 10 to .NET 11 rows: start-up is about 10% lower, which is inside the run-to-run variation of these
machines, so treat it as "no worse" until more runs agree. The executable grew by about 0.7 MB, but the two runs are
not the same code: the settings page, keyboard shortcuts and the Files changed page landed in between. A clean
before/after needs the same commit built on both, which was not done.

CI now fails if the start-up median goes over 400 ms on x64 or 600 ms on ARM64.

## C# and Rust, side by side

Both apps built at the same commit by the manually started workflow (`.github/workflows/release.yml`, run
43049293921, 11 November 2026) and measured one after the other **on the same runner** with
`tools/measure-startup.ps1`. Both start signed out, so this is each app's shell and sign-in view.

| Runner | App | Start-up median (min-max) | Working set | Executable | App folder | Zip |
|---|---|---|---|---|---|---|
| x64 | C# (.NET 11 Native AOT, WinUI 3) | 160 ms (152-189) | 77 MB | 13.9 MB | 132.8 MB | 53.2 MB |
| x64 | Rust (windows-reactor) | 139 ms (132-151) | 66 MB | 1.9 MB | 60.0 MB | 23.6 MB |
| ARM64 | C# | 339 ms (153-416) | 85 MB | 14.1 MB | 141.5 MB | 52.3 MB |
| ARM64 | Rust | 143 ms (138-153) | 77 MB | 1.9 MB | 67.3 MB | 23.5 MB |

How to read this:

- **x64 start-up is close**: Rust is about 20 ms (13%) faster in this run. In the ordinary CI run just before
  it, on separate machines, C# measured 141 ms and Rust 154 ms. The gap is inside the variation between runs,
  so on x64 the honest summary is "about the same".
- **ARM64 start-up differs more, and C# is erratic there**: its five runs ranged from 153 to 416 ms, while Rust
  stayed within 138-153 ms. One run is not enough to call it; it is the first thing to repeat.
- **Size is the clear difference**: the Rust executable is 1.9 MB against 13.9 MB, and its folder is less than
  half the size. Most of both folders is the Windows App Runtime that each app carries; the C# folder also
  carries more of the Windows App SDK than the Rust one.
- **Working set**: Rust uses about 10 MB less at the sign-in view.
- **This is not yet a like-for-like app**: the Rust app has sign-in and a basic inbox; the C# app has much more.
  More features mean more code to load, so expect the C# start-up figure to be the more "finished" one.
  Only the signed-out start is comparable today.

## Measuring signed in

CI cannot sign in, so the comparison that matters most has to run on a person's machine. Both builds write the
moment their first inbox page arrives to the file named by `GUIT_TRACE_FILE`, and `tools/measure-inbox.ps1`
uses that to time launch to inbox, launch to window and the working set with the inbox loaded:

```powershell
.\tools\measure-inbox.ps1 -Exe <path>\Guit.exe -Label "C#"
.\tools\measure-inbox.ps1 -Exe <path>\guit.exe -Label "Rust"
```

The zips from the side-by-side workflow include the script. The time includes GitHub's response, which varies
by more than either app's own cost, so run both apps alternately a few times and compare medians.

## Not measured yet

- **Anything after sign-in**: time to a filled inbox, memory with a few hundred notifications loaded, scrolling
  frame times. CI cannot sign in. These need a Windows machine, a signed-in account and Windows Performance Recorder.
- **A laptop**: the budget in the `performance` skill (under 400 ms) was written for one; only CI machines have been timed.
- **The signed-in comparison**: both apps showing the same inbox on the same machine (plan card 6.10).
- **CsWinRT 3**: expected to cut size and start-up further; it was still a preview when this was written (card 5.7b).
