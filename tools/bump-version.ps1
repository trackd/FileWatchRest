#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Increments the build/patch version number in FileWatchRest.csproj
.DESCRIPTION
    Reads the current version from the project file and increments the patch (build) number.
    Updates Version, AssemblyVersion, and FileVersion properties.
.PARAMETER Major
    Increment the major version instead (resets minor and patch to 0)
.PARAMETER Minor
    Increment the minor version instead (resets patch to 0)
.EXAMPLE
    .\bump-version.ps1
    Increments patch: 0.4.0 -> 0.4.1
.EXAMPLE
    .\bump-version.ps1 -Minor
    Increments minor: 0.4.0 -> 0.5.0
.EXAMPLE
    .\bump-version.ps1 -Major
    Increments major: 0.4.0 -> 1.0.0
#>
[CmdletBinding()]
param(
    [switch]$Major,
    [switch]$Minor,
    [switch]$Commit,
    [switch]$Amend
)

$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot "../FileWatchRest/FileWatchRest.csproj"

if (-not (Test-Path $projectFile)) {
    Write-Error "Project file not found: $projectFile"
    exit 1
}

# Load project file as XML
[xml]$xml = Get-Content $projectFile

# Find the Version element
$versionNode = $xml.Project.PropertyGroup.Version | Where-Object { $null -ne $_ } | Select-Object -First 1

if (-not $versionNode) {
    Write-Error "Could not find <Version> element in project file"
    exit 1
}

# Try parsing as semver first, fallback to version
$semVer = [semver]::new(0, 0, 0, 0)
$useSemanticVersion = [semver]::TryParse($versionNode, [ref]$semVer)

if (-not $useSemanticVersion) {
    # Fallback to [version] type
    $version = [version]::new()
    if (-not [version]::TryParse($versionNode, [ref]$version)) {
        Write-Error "Could not parse version: $versionNode"
        exit 1
    }
}

# Work with the appropriate version object
if ($useSemanticVersion) {
    $oldVersion = $semVer.ToString()

    # Determine new version based on parameters
    if ($Major) {
        $semVer = [semver]::new($semVer.Major + 1, 0, 0)
    }
    elseif ($Minor) {
        $semVer = [semver]::new($semVer.Major, $semVer.Minor + 1, 0)
    }
    else {
        $semVer = [semver]::new($semVer.Major, $semVer.Minor, $semVer.Patch + 1)
    }

    $newVersionObj = $semVer
}
else {
    $oldVersion = "$($version.Major).$($version.Minor).$($version.Build)"

    # Determine new version based on parameters
    if ($Major) {
        $version = [version]::new($version.Major + 1, 0, 0)
    }
    elseif ($Minor) {
        $version = [version]::new($version.Major, $version.Minor + 1, 0)
    }
    else {
        $version = [version]::new($version.Major, $version.Minor, $version.Build + 1)
    }

    $newVersionObj = $version
}

$newVersion = $newVersionObj.ToString()

Write-Host "Incrementing version: " -NoNewline
Write-Host $oldVersion -ForegroundColor Yellow -NoNewline
Write-Host " -> " -NoNewline
Write-Host $newVersion -ForegroundColor Green

# Update all version nodes in XML
foreach ($propertyGroup in $xml.Project.PropertyGroup) {
    if ($propertyGroup.Version) {
        $propertyGroup.Version = $newVersionObj.ToString()
    }
    if ($propertyGroup.AssemblyVersion) {
        $propertyGroup.AssemblyVersion = $newVersionObj.ToString()
    }
    if ($propertyGroup.FileVersion) {
        # FileVersion always needs 4 parts for .NET
        if ($useSemanticVersion) {
            $propertyGroup.FileVersion = "$($newVersionObj.ToString()).0"
        }
        else {
            $propertyGroup.FileVersion = [version]::new($newVersionObj.Major, $newVersionObj.Minor, $newVersionObj.Build, 0).ToString()
        }
    }
}

# Save XML back to file (preserving formatting)
$xml.Save($projectFile)

Write-Host "✓ Updated $projectFile" -ForegroundColor Green

# Handle git operations based on context
if ($Commit -or $Amend) {
    git add $projectFile
    if ($Amend) {
        git commit --amend --no-edit --no-verify
        Write-Host "✓ Amended commit with version bump" -ForegroundColor Green
    }
    else {
        git commit -m "chore: bump version to $newVersion"
        Write-Host "✓ Committed version bump" -ForegroundColor Green
    }
}
else {
    # When run from pre-commit hook, auto-stage the file
    $gitStatus = git status --porcelain $projectFile 2>$null
    if ($gitStatus) {
        git add $projectFile
        Write-Host "✓ Staged version bump for commit" -ForegroundColor Green
    }
    else {
        Write-Host "Run with -Commit to create new commit or -Amend to amend existing commit" -ForegroundColor Cyan
    }
}
