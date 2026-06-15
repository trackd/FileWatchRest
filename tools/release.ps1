#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Release orchestration: test, build, package, generate release notes

.DESCRIPTION
    Runs tests, builds the Windows x64 Native AOT single-file executable via `tools/build.ps1`,
    runs `tools/package.ps1` to produce MSI/ZIP artifacts, and generates release notes.
#>
param(
    [string]$Version,
    [switch]$BumpVersion,
    [switch]$PublishToGitHub
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail {
    param(
        [string]$Message
    )
    Write-Error $Message
    exit 1
}

# Ensure tooling
try { dotnet --version | Out-Null } catch {
    Fail 'dotnet is not available on PATH'
}

$Parent = Split-Path $PSScriptRoot -Parent

$EnablePublish = $env:CI -or $PublishToGitHub
$TagVersion = $null
if ($env:GITHUB_REF -like 'refs/tags/v*') {
    $TagVersion = $env:GITHUB_REF_NAME
    if (-not $TagVersion) {
        $TagVersion = $env:GITHUB_REF.Substring('refs/tags/'.Length)
    }

    $TagVersion = $TagVersion.TrimStart('v')
}

$RequestedVersion = if ($TagVersion) { $TagVersion } else { $Version }
if ($BumpVersion -and $TagVersion) {
    Fail 'Refusing to bump version during a tag build. Tag releases must use the tag version.'
}

if ($Version -and $BumpVersion) {
    Fail 'Use either -Version to set an explicit release version or -BumpVersion to increment the project version, not both.'
}

$csprojPath = Join-Path $Parent 'FileWatchRest' 'FileWatchRest.csproj'
if (-not (Test-Path $csprojPath)) { Fail "Cannot find project file: $csprojPath" }

[xml]$csproj = Get-Content $csprojPath
$bumpScript = Join-Path $PSScriptRoot 'bump-version.ps1'
if (-not (Test-Path $bumpScript)) { Fail "bump-version.ps1 not found at: $bumpScript" }

if ($TagVersion) {
    Write-Host "Using tag version without modifying csproj: $TagVersion" -ForegroundColor Gray
}
elseif ($RequestedVersion) {
    Write-Host "Writing requested release version to csproj: $RequestedVersion" -ForegroundColor Gray
    & $bumpScript $RequestedVersion | Out-Null
    [xml]$csproj = Get-Content $csprojPath
}
elseif ($BumpVersion) {
    Write-Host 'Bumping patch version in csproj (release)...' -ForegroundColor Gray
    & $bumpScript | Out-Null
    [xml]$csproj = Get-Content $csprojPath
}

$ProjectVersion = $csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
$ResolvedVersion = if ($TagVersion) { $TagVersion } else { $ProjectVersion }
if (-not $ResolvedVersion) {
    Fail 'Version not found in csproj; please set <Version> in the project or pass -Version.'
}

if ($TagVersion -and $ProjectVersion -and $ProjectVersion -ne $TagVersion) {
    Write-Warning "Project version is $ProjectVersion but tag version is $TagVersion. Building release artifacts with tag version $TagVersion; csproj is not modified in CI."
}

Write-Host "Using version: $ResolvedVersion" -ForegroundColor Cyan
# Ensure artifacts folder exists
$artifactsDir = Join-Path $Parent 'artifacts'
if (-not (Test-Path $artifactsDir)) { New-Item -ItemType Directory -Path $artifactsDir | Out-Null }

Write-Host "Releasing version: $ResolvedVersion" -ForegroundColor Cyan

# Build (build.ps1 will run tests by default; it auto-enables coverage in CI)
Write-Host '==> Building (restore/build/test/publish)' -ForegroundColor Cyan
$build = Get-Item (Join-Path $PSScriptRoot 'build.ps1')

# assemble build args
$buildArgs = @{
    'ProjectPath' = 'FileWatchRest'
    'OutputDir' = 'output'
    'Version' = $ResolvedVersion
}

Write-Host "Invoking build with Version=$ResolvedVersion" -ForegroundColor Gray
# Ensure build runs from repository root so publish output ('./publish') lands at repo root
Push-Location $Parent
try {
    & $build @buildArgs
}
finally {
    Pop-Location
}

# Package
Write-Host '==> Packaging artifacts (MSI/ZIP)' -ForegroundColor Cyan
# Package
$package = Get-Item (Join-Path $PSScriptRoot 'package.ps1')
# Pass explicit csproj path so packaging doesn't need to re-scan default location
$packsplat = @{
    'PublishDir' = Join-Path $Parent 'publish'
    'OutputDir' = Join-Path $Parent 'artifacts'
    'Version' = $ResolvedVersion
}
& $package @packsplat

# Release notes
Write-Host '==> Generating release notes' -ForegroundColor Cyan
# Release notes
$releaseNotes = Get-Item (Join-Path $PSScriptRoot 'release-notes.ps1')
$rlssplat = @{
    'Version' = $ResolvedVersion
    'OutputPath' = Join-Path $Parent 'artifacts' 'release_notes.md'
    'Repository' = 'trackd/FileWatchRest'
    'CommitSha' = (git rev-parse HEAD)
}
& $releaseNotes @rlssplat

# Optional: publish to GitHub Releases when running in CI or when requested
if ($EnablePublish) {
    if ($env:CI) {
        Write-Host 'CI run detected — skipping in-script gh publish. Workflow will create the release.' -ForegroundColor Gray
    }
    else {
        Write-Host '==> Publishing release to GitHub (gh CLI)' -ForegroundColor Cyan
        $gh = Get-Command gh -ErrorAction SilentlyContinue
        if (-not $gh) {
            Write-Warning 'gh CLI not found; skipping GitHub release upload. Install from https://cli.github.com/'
        }
        else {
            $tag = "v$ResolvedVersion"
            $notesPath = Join-Path "$parent/artifacts" 'release_notes.md'
            $notes = ''
            if (Test-Path $notesPath) { $notes = Get-Content $notesPath -Raw }
            $files = Get-ChildItem -Path "$parent/artifacts" -File | ForEach-Object { $_.FullName }
            try {
                & gh release create $tag @files --title $tag --notes "$notes"
                Write-Host "Published GitHub release: $tag" -ForegroundColor Green
            }
            catch {
                Write-Warning "Failed to publish release via gh: $_"
            }
        }
    }
}
