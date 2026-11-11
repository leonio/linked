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

## Not measured yet

- **Anything after sign-in**: time to a filled inbox, memory with a few hundred notifications loaded, scrolling
  frame times. CI cannot sign in. These need a Windows machine, a signed-in account and Windows Performance Recorder.
- **A laptop**: the budget in the `performance` skill (under 400 ms) was written for one; only CI machines have been timed.
- **C++/WinRT**: the comparison this project exists for (plan card 5.8). It needs the same page built as a C++/WinRT
  WinUI 3 app and measured on the same machine with the method above. The four rows above are the C# half.
- **CsWinRT 3**: expected to cut size and start-up further; it was still a preview when this was written (card 5.7b).
