<#
.SYNOPSIS
  Copies the Rust build of Guit, with the Windows App Runtime files staged beside it, into a clean folder.

.DESCRIPTION
  `cargo build` leaves the executable and the self-contained runtime files (staged by windows-reactor-setup)
  in the profile output directory, mixed with Cargo's own working folders. This copies only what the app
  needs to run, so the folder can be zipped, measured or handed to someone.

.EXAMPLE
  .\tools\stage-rust.ps1 -BuildDir rust\target\x86_64-pc-windows-msvc\release -Out artifacts\guit-rs
#>
param(
    [Parameter(Mandatory)] [string] $BuildDir,
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $BuildDir 'guit.exe'))) { throw "No guit.exe in $BuildDir. Build first: cargo build --release -p guit" }

# Cargo's own folders and files; everything else in the directory belongs to the app.
$cargoFolders = 'build', 'deps', 'examples', 'incremental', '.fingerprint'
$cargoFiles = '*.d', '*.pdb', '*.rlib', '.cargo-lock', 'CACHEDIR.TAG'

New-Item -ItemType Directory -Force -Path $Out | Out-Null
Get-ChildItem $BuildDir -Force | Where-Object {
    $name = $_.Name
    if ($_.PSIsContainer) { $cargoFolders -notcontains $name }
    else { -not ($cargoFiles | Where-Object { $name -like $_ }) }
} | Copy-Item -Destination $Out -Recurse -Force

$files = Get-ChildItem $Out -Recurse -File
"Staged {0} files, {1:N1} MB, in {2}" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB), $Out
