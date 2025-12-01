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
    [Parameter(Position=0)]
    [string]$TargetVersion,
    [switch]$Major,
    [switch]$Minor,
    [switch]$Commit,
    [switch]$Amend,
    [switch]$NoUpdate
)

$ErrorActionPreference = 'Stop'

$Parent = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $Parent 'FileWatchRest' 'FileWatchRest.csproj'

Write-Host $projectFile
if (-not (Test-Path $projectFile)) {
    Write-Error "Project file not found: $projectFile"
    exit 1
}

# Load project file as XML
[xml]$xml = Get-Content -Path $projectFile -Raw

# Find the Version element (robust, avoid property-access that fails under StrictMode)
$versionString = $null
$versionNode = $xml.SelectSingleNode('//Project/PropertyGroup/Version')
if ($null -ne $versionNode) {
    $versionString = $versionNode.InnerText.Trim()
}
else {
    $pgs = $xml.Project.SelectNodes('PropertyGroup')
    foreach ($pg in $pgs) {
        $vn = $pg.SelectSingleNode('Version')
        if ($null -ne $vn) { $versionString = $vn.InnerText.Trim(); break }
    }
}

if (-not $versionString) {
    Write-Error "Could not find <Version> element in project file"
    exit 1
}

# Try parsing as semver first, fallback to version
$semVer = [semver]::new(0, 0, 0, 0)

# If a target version was passed positionally, honor it instead of reading/incrementing
if ($TargetVersion) {
    $useSemanticVersion = [semver]::TryParse($TargetVersion, [ref]$semVer)
    if (-not $useSemanticVersion) {
        $version = [version]::new()
        if (-not [version]::TryParse($TargetVersion, [ref]$version)) {
            Write-Error "Could not parse provided target version: $TargetVersion"
            exit 1
        }
    }
}
else {
    $useSemanticVersion = [semver]::TryParse($versionString, [ref]$semVer)
    if (-not $useSemanticVersion) {
        # Fallback to [version] type
        $version = [version]::new()
        if (-not [version]::TryParse($versionString, [ref]$version)) {
            Write-Error "Could not parse version: $versionString"
            exit 1
        }
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

# Update only the PropertyGroup(s) that already contain version information (Version/AssemblyVersion/FileVersion)
$pgs = $xml.Project.SelectNodes("PropertyGroup[Version or AssemblyVersion or FileVersion]")
if ($pgs.Count -eq 0) {
    # fallback: pick the first PropertyGroup if present, otherwise create one
    $allPgs = $xml.Project.SelectNodes('PropertyGroup')
    if ($allPgs.Count -gt 0) {
        $pgs = @($allPgs.Item(0))
    }
    else {
        $newPg = $xml.CreateElement('PropertyGroup')
        $xml.Project.AppendChild($newPg) | Out-Null
        $pgs = @($newPg)
    }
}

# Compute FileVersion value once
if ($useSemanticVersion) {
    $baseVer = $newVersionObj.ToString().Split('+')[0].Split('-')[0]
    $parts = $baseVer.Split('.') | ForEach-Object { $_ }
    while ($parts.Count -lt 3) { $parts += '0' }
    $fileVersionV = "$($parts[0]).$($parts[1]).$($parts[2]).0"
}
else {
    $fileVersionV = [version]::new($newVersionObj.Major, $newVersionObj.Minor, $newVersionObj.Build, 0).ToString()
}

foreach ($propertyGroup in $pgs) {
    # Version
    $vn = $propertyGroup.SelectSingleNode('Version')
    if ($null -ne $vn) {
        $vn.InnerText = $newVersionObj.ToString()
    }
    else {
        $newNode = $xml.CreateElement('Version')
        $newNode.InnerText = $newVersionObj.ToString()
        $propertyGroup.AppendChild($newNode) | Out-Null
    }

    # AssemblyVersion
    $av = $propertyGroup.SelectSingleNode('AssemblyVersion')
    if ($null -ne $av) { $av.InnerText = $newVersionObj.ToString() }
    else {
        $newAv = $xml.CreateElement('AssemblyVersion')
        $newAv.InnerText = $newVersionObj.ToString()
        $propertyGroup.AppendChild($newAv) | Out-Null
    }

    # FileVersion
    $fvNode = $propertyGroup.SelectSingleNode('FileVersion')
    if ($null -ne $fvNode) { $fvNode.InnerText = $fileVersionV }
    else {
        $newFv = $xml.CreateElement('FileVersion')
        $newFv.InnerText = $fileVersionV
        $propertyGroup.AppendChild($newFv) | Out-Null
    }
}

# Save XML back to file (preserving formatting), unless NoUpdate (dry-run) was requested
if (-not $NoUpdate) {
    $xml.Save($projectFile)
    Write-Host "✓ Updated $projectFile" -ForegroundColor Green
}
else {
    Write-Host "Dry-run: -NoUpdate specified; skipping write to $projectFile" -ForegroundColor Yellow
    Write-Host "Proposed updates:" -ForegroundColor Yellow
    foreach ($propertyGroup in $xml.Project.SelectNodes('PropertyGroup')) {
        $v = $propertyGroup.SelectSingleNode('Version')
        if ($null -ne $v) { Write-Host "  Version: $($v.InnerText)" }
        $av = $propertyGroup.SelectSingleNode('AssemblyVersion')
        if ($null -ne $av) { Write-Host "  AssemblyVersion: $($av.InnerText)" }
        $fv = $propertyGroup.SelectSingleNode('FileVersion')
        if ($null -ne $fv) { Write-Host "  FileVersion: $($fv.InnerText)" }
    }
    return $newVersionObj
}

# Handle git operations based on context (skip when NoUpdate/dry-run)
if (-not $NoUpdate) {
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
}
else {
    Write-Host "Dry-run: -NoUpdate specified; skipping git operations." -ForegroundColor Yellow
}
