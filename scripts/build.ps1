# Builds the desktop app and the installer.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

Write-Host '› Publishing self-contained app…'
dotnet publish src -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -o publish | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item installer\enable-remote.cmd publish\ -Force
Copy-Item installer\README.txt publish\ -Force

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw 'Inno Setup 6 not found. Install it, then re-run.' }

Write-Host "› Compiling installer with $iscc"
& $iscc installer\SmartSkiimer.iss | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

Get-ChildItem dist | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
