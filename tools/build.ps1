#!/usr/bin/env pwsh
<#
Package the app for deployment on target machine and produce output folder with helper installer script
Usage:
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -ProjectPath . -OutputDir .\output

Parameters:
  -ProjectPath : path to project/solution (default: .)
  -OutputDir   : folder to create containing publish artifacts + installer (default: .\output)
#>
param(
    [string]$ProjectPath = 'FileWatchRest',
    [string]$OutputDir = '.\output',
    [Switch] $VersionBump
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($VersionBump) {
    # bump version before building
    $bumpScript = Join-Path $PSScriptRoot "bump-version.ps1"
    if (-not (Test-Path $bumpScript)) {
        Write-Warning "bump-version.ps1 not found at: $bumpScript"
    } else {
        & $bumpScript
    }
}

$configuration = 'Release'
$rid = 'win-x64'

$publishDir = Join-Path -Path $ProjectPath -ChildPath "bin/$configuration/net10.0/$rid/publish"

# publish
$publishFlags = @('-c', $configuration, '-r', $rid)
$msbuildProps = @{
    PublishSingleFile = $true
    SelfContained     = $true
    PublishAot        = $true
    PublishTrimmed    = $true
    TrimMode          = 'link'
}
foreach ($k in $msbuildProps.Keys) {
    $publishFlags += "-p:$k=$($msbuildProps[$k])"
}
Write-Host "Publishing to $publishDir"
& dotnet publish $ProjectPath @publishFlags

# determine actual publish folder (handle publishing from solution or project folder)
if (-not (Test-Path $publishDir)) {
    Write-Host "Expected publish folder '$publishDir' not found; searching for publish folder under $ProjectPath ..."
    $found = Get-ChildItem -Path $ProjectPath -Directory -Recurse -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'publish' } | Select-Object -First 1
    if ($found) {
        $publishDir = $found.FullName
        Write-Host "Found publish folder: $publishDir"
    } else {
        Write-Error "Publish folder not found under $ProjectPath. Ensure dotnet publish succeeded and set -ProjectPath to the project folder if needed."
        exit 1
    }
}

# prepare output dir
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# copy publish files
Copy-Item -Path (Join-Path $publishDir '*') -Destination $OutputDir -Recurse -Force -Exclude '*.pdb'

# create installer script for target machine
# Use single-quoted here-string to avoid expanding variables inside the installer script
$installer = @'
# Target-machine installer script for FileWatchRest
param(
    [string] $ServiceName = 'FileWatchRest',
    [string] $ServiceDisplayName = 'File Watch REST Service'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# prepare program files folder
$installDir = Join-Path -Path $env:ProgramFiles -ChildPath $ServiceName
if (-not (Test-Path $installDir)) { New-Item -ItemType Directory -Path $installDir | Out-Null }

# copy files from package dir (this script is expected to be run from the package folder)
Get-ChildItem -Path $PSScriptRoot -Exclude '*.ps1' | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $installDir -Recurse -Force
}

# create ProgramData log folder
$logDir = Join-Path -Path $env:ProgramData -ChildPath $ServiceName
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

# install service
$exe = Get-ChildItem -Path $installDir -Filter '*.exe' | Select-Object -First 1
if (-not $exe) { Write-Error 'No executable found in install dir'; exit 1 }
$exePath = $exe.FullName

# remove existing service if present
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch {}
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# create service
$binPathArg = 'binPath="' + $exePath + '"'
& sc.exe create $ServiceName $binPathArg DisplayName= $ServiceDisplayName start= auto | Out-Null
Start-Sleep -Milliseconds 500
Start-Service -Name $ServiceName

Write-Host "Service installed to $installDir and started. Logs will be under $logDir"
'@

$installerPath = Join-Path -Path $OutputDir -ChildPath 'install_on_target.ps1'
Set-Content -Path $installerPath -Value $installer -Encoding UTF8

Write-Host "Package prepared in $OutputDir (include this folder on target and run install_on_target.ps1 as admin)."
