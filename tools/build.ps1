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
    [string]$Configuration = 'Release',
    [switch]$VersionBump,
    [string]$Version,
    [string]$AssemblyVersion,
    [string]$FileVersion,
    [string]$InformationalVersion,
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$CollectCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Optional version bump before building
if ($VersionBump -and -not $Version) {
    $bumpScript = Join-Path $PSScriptRoot "bump-version.ps1"
    if (Test-Path $bumpScript) {
        Write-Host "Running version bump..." -ForegroundColor Gray
        $version = & $bumpScript #-NoUpdate
    }
    else { Write-Warning "bump-version.ps1 not found at: $bumpScript" }
}

# Default CollectCoverage to true when running in CI unless explicitly provided
if (-not $PSBoundParameters.ContainsKey('CollectCoverage') -and $env:CI) { $CollectCoverage = $true }
$Parent = Split-Path $PSScriptRoot -Parent

$solution = 'FileWatchRest.sln'
$project = Join-Path $Parent $ProjectPath 'FileWatchRest.csproj'
$rid = 'win-x64'

Write-Host "==> Restoring dependencies..." -ForegroundColor Cyan
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Building solution..." -ForegroundColor Cyan
$buildArgs = @(
    'build',
    $solution,
    '--configuration',
    $Configuration,
    '--no-restore'
)
if ($Version) { $buildArgs += "/p:Version=$Version" }
if ($AssemblyVersion) { $buildArgs += "/p:AssemblyVersion=$AssemblyVersion" }
if ($FileVersion) { $buildArgs += "/p:FileVersion=$FileVersion" }
if ($InformationalVersion) { $buildArgs += "/p:InformationalVersion=$InformationalVersion" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Run tests (optionally collect coverage)
if (-not $SkipTests) {
    Write-Host "==> Running tests..." -ForegroundColor Cyan
    $testArgs = @('test', $solution, '--configuration', $Configuration, '--no-build')
    if ($CollectCoverage) {
        Write-Host "Collecting code coverage..." -ForegroundColor Gray
        $testArgs += '--collect:XPlat Code Coverage'
        $testArgs += '--results-directory'
        $testArgs += './coverage'
    }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Publish with Native AOT unless skipped
if (-not $SkipPublish) {
    Write-Host "==> Publishing with Native AOT..." -ForegroundColor Cyan

    $publishDir = Join-Path -Path $ProjectPath -ChildPath "bin/$Configuration/net10.0/$rid/publish"

    # Clean previous publish/output/artifacts
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    foreach ($d in @((Join-Path (Get-Location) 'publish'), (Join-Path (Get-Location) 'output'), (Join-Path (Get-Location) 'artifacts'))) {
        if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    }

    # Ensure restore includes the target runtime so project.assets.json contains net10.0/$rid
    Write-Host "Ensuring restore for runtime: $rid" -ForegroundColor Gray
    & dotnet restore $project --runtime $rid
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Runtime-specific restore failed; proceeding to publish may fail'; }

    $publishArgs = @(
        'publish', $project,
        '--configuration', $Configuration,
        '--runtime', $rid,
        '--output', "./publish",
        '-p:PublishAot=true',
        '-p:PublishSingleFile=true',
        '-p:SelfContained=true',
        '-p:PublishTrimmed=true',
        '-p:TrimMode=link'
    )
    if ($Version) { $publishArgs += "/p:Version=$Version" }
    if ($AssemblyVersion) { $publishArgs += "/p:AssemblyVersion=$AssemblyVersion" }
    if ($FileVersion) { $publishArgs += "/p:FileVersion=$FileVersion" }
    if ($InformationalVersion) { $publishArgs += "/p:InformationalVersion=$InformationalVersion" }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Published to: ./publish" -ForegroundColor Green

    # Prepare output dir (manual install bundle)
    if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
    Copy-Item -Path (Join-Path (Resolve-Path './publish').Path '*') -Destination $OutputDir -Recurse -Force -Exclude '*.pdb'

    Write-Host "Package prepared in $OutputDir" -ForegroundColor Gray
}
