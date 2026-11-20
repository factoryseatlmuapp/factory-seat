# Builds the release: publishes the single-file exe to publish\, then wraps it in the installer
# publish\installer\FactorySeat-Setup-<version>.exe. The version comes from Directory.Build.props.
#   powershell -File installer\build.ps1                    (both steps)
#   powershell -File installer\build.ps1 -Step publish      (exe only, so CI can sign it before Inno packs it)
#   powershell -File installer\build.ps1 -Step installer    (wrap the exe already in publish\)
param([ValidateSet('all', 'publish', 'installer')] [string] $Step = 'all')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$props = 'Directory.Build.props'
$version = ([xml](Get-Content $props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $props" }

if ($Step -ne 'installer') {
dotnet publish src/LmuCareer.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
if ($Step -eq 'publish') { return }

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$version" installer\FactorySeat.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

Get-ChildItem publish\installer\*.exe | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
