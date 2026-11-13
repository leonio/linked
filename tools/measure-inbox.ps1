<#
.SYNOPSIS
  Times how long an app takes from process start to having its first page of notifications, signed in.

.DESCRIPTION
  Both builds of Guit record the moment the first inbox page arrives when the GUIT_TRACE_FILE environment
  variable names a file (the C# app's PerfTrace, the Rust app's guit_core::trace). This script starts the app
  several times with that variable set, and reports the median time from launch to that moment, the time to
  the first window, and the working set a few seconds after the inbox arrived.

  You must be signed in. Both builds share one sign-in, so signing in with either is enough. The timing
  includes GitHub's response time, which varies: run both apps one after the other, more than once, and
  compare medians rather than single runs.

.EXAMPLE
  .\measure-inbox.ps1 -Exe .\guit-cs\Guit.exe -Label "C#"
  .\measure-inbox.ps1 -Exe .\guit-rs\guit.exe -Label "Rust"
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Label = (Split-Path $Exe -Leaf),
    [int] $Runs = 6
)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$inboxTimes = @()
$windowTimes = @()
$workingSets = @()

foreach ($run in 1..$Runs) {
    $trace = Join-Path ([IO.Path]::GetTempPath()) "guit-trace-$PID-$run.txt"
    Remove-Item $trace -ErrorAction SilentlyContinue
    $env:GUIT_TRACE_FILE = $trace

    $launched = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $app = Start-Process -FilePath $Exe -PassThru
    $window = $null
    $inbox = $null
    while ($watch.ElapsedMilliseconds -lt 60000) {
        if ($app.HasExited) { throw "$Label exited with code $($app.ExitCode)" }
        $app.Refresh()
        if ($null -eq $window -and $app.MainWindowHandle -ne 0) { $window = $watch.ElapsedMilliseconds }
        if (Test-Path $trace) {
            $line = Get-Content $trace | Where-Object { $_ -like 'inbox *' } | Select-Object -First 1
            if ($line) { $inbox = [long]($line.Split(' ')[1]) - $launched; break }
        }
        Start-Sleep -Milliseconds 5
    }
    Remove-Item Env:GUIT_TRACE_FILE

    if ($null -eq $inbox) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
        throw "$Label showed no inbox within 60 s. Is it signed in? (Sign in with either build; they share the sign-in.)"
    }

    # Let the list draw before reading memory.
    Start-Sleep -Seconds 3
    $app.Refresh()
    $workingSet = $app.WorkingSet64
    Stop-Process -Id $app.Id -Force
    $app.WaitForExit()
    Remove-Item $trace -ErrorAction SilentlyContinue

    "${Label} run ${run}: window $window ms, inbox $inbox ms, working set {0:N0} MB" -f ($workingSet / 1MB)
    if ($run -gt 1) {
        $inboxTimes += $inbox
        $windowTimes += $window
        $workingSets += $workingSet
    }
}

function Median($values) { $sorted = @($values | Sort-Object); $sorted[[int][Math]::Floor($sorted.Count / 2)] }

$summary = "{0}: signed in, median to window {1} ms, to first inbox page {2} ms (min {3}, max {4}, {5} runs); working set with the inbox {6:N0} MB" -f `
    $Label, (Median $windowTimes), (Median $inboxTimes), ($inboxTimes | Measure-Object -Minimum).Minimum, ($inboxTimes | Measure-Object -Maximum).Maximum, $inboxTimes.Count, ((Median $workingSets) / 1MB)
$summary
