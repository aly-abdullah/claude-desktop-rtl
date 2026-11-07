<#
  Rebuilds both tools into the project folder:
    Enable Arabic RTL.exe   Disable Arabic RTL.exe
  1. Regenerates the RTL script from the newest extension version (if available).
  2. Publishes both variants as single, self-contained, trimmed executables.
  ASCII-only on purpose (PowerShell 5.1).
#>
param([switch]$KeepSnippet)
$ErrorActionPreference = 'Stop'
$src  = $PSScriptRoot
$root = Split-Path $src -Parent

if (-not $KeepSnippet) { & (Join-Path $src 'snippet\make-snippet.ps1') }

$targets = @(
  @{ Variant = 'Enable';  Exe = 'ClaudeDesktopRtl.exe';         Out = 'Enable Arabic RTL.exe' },
  @{ Variant = 'Disable'; Exe = 'ClaudeDesktopRtl.Disable.exe'; Out = 'Disable Arabic RTL.exe' }
)

foreach ($t in $targets) {
  $pub = Join-Path $src ('bin\publish\' + $t.Variant)
  if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
  $obj = Join-Path $src 'obj\Release'
  if (Test-Path $obj) { Remove-Item $obj -Recurse -Force }
  dotnet publish (Join-Path $src 'ClaudeDesktopRtl.csproj') -c Release "-p:Variant=$($t.Variant)" -o $pub -v quiet -nologo
  if ($LASTEXITCODE -ne 0) { throw "Build failed: $($t.Variant)" }
  Copy-Item (Join-Path $pub $t.Exe) (Join-Path $root $t.Out) -Force
  Write-Host ("Built " + $t.Out)
}
