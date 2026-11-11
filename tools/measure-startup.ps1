<#
.SYNOPSIS
  Times how long an app takes from process start to having a window. Used by CI for both the C#
  and the Rust build, and meant to be run by hand to compare them on the same machine.

.DESCRIPTION
  Starts the executable several times. Each run is timed until the process has a main window handle.
  The first run is discarded (cold disk cache); the median of the rest is reported, with the working
  set two seconds after the window appeared and the size of the executable.

.EXAMPLE
  .\measure-startup.ps1 -Exe .\guit-cs\Guit.exe -Label "C#"
  .\measure-startup.ps1 -Exe .\guit-rs\guit.exe -Label "Rust"
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Label = (Split-Path $Exe -Leaf),
    [int] $Runs = 6,
    # Fail (exit code 1) when the median is over this many milliseconds. 0 means report only.
    [int] $BudgetMs = 0
)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$times = @()
$workingSet = 0

foreach ($run in 1..$Runs) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $app = Start-Process -FilePath $Exe -PassThru
    while ($app.MainWindowHandle -eq 0) {
        if ($app.HasExited) { throw "$Label exited during start-up with code $($app.ExitCode)" }
        if ($watch.ElapsedMilliseconds -gt 30000) { Stop-Process -Id $app.Id -Force; throw "$Label started but never created a window" }
        Start-Sleep -Milliseconds 5
        $app.Refresh()
    }
    $elapsed = $watch.ElapsedMilliseconds

    Start-Sleep -Seconds 2
    $app.Refresh()
    if ($app.HasExited) { throw "$Label exited right after showing its window, code $($app.ExitCode)" }
    $workingSet = $app.WorkingSet64
    Stop-Process -Id $app.Id -Force
    $app.WaitForExit()

    if ($run -gt 1) { $times += $elapsed }
    "${Label} run ${run}: $elapsed ms"
}

$sorted = @($times | Sort-Object)
$median = $sorted[[int][Math]::Floor($sorted.Count / 2)]
$folder = Split-Path $Exe -Parent
$folderSize = (Get-ChildItem $folder -Recurse -File | Measure-Object Length -Sum).Sum
$summary = "{0}: start-up median {1} ms (min {2}, max {3}, {4} runs); working set {5:N0} MB; exe {6:N1} MB; folder {7:N1} MB" -f `
    $Label, $median, $sorted[0], $sorted[-1], $sorted.Count, ($workingSet / 1MB), ((Get-Item $Exe).Length / 1MB), ($folderSize / 1MB)

$summary
if ($env:GITHUB_STEP_SUMMARY) { $summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }

if ($BudgetMs -gt 0 -and $median -gt $BudgetMs) {
    Write-Error "$Label start-up median $median ms is over the budget of $BudgetMs ms"
    exit 1
}
