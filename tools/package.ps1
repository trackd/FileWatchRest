#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Create MSI/ZIP package for FileWatchRest

.DESCRIPTION
    Packages FileWatchRest with WiX Toolset (MSI) or ZIP archive.
    Includes service configuration and installer script.
    Can be run locally or in CI/CD pipelines.

.PARAMETER PublishDir
    Directory containing publish artifacts (default: ./publish)

.PARAMETER OutputDir
    Output directory for package (default: ./artifacts)

.PARAMETER Version
    Version string for package filename (e.g., 1.2.3 or 1.2.3-alpha.1)

.PARAMETER CreateMsi
    Create MSI package using WiX Toolset (default: true)

.PARAMETER CreateZip
    Create ZIP archive package (default: true)

.EXAMPLE
    # Local packaging (auto-detect version from .csproj)
    .\tools\package.ps1

.EXAMPLE
    # CI packaging with version
    .\tools\package.ps1 -Version "1.2.3"

.EXAMPLE
    # ZIP only
    .\tools\package.ps1 -CreateMsi:$false
#>
param(
    [string]$PublishDir = './publish',
    [string]$OutputDir = './artifacts',
    [string]$Version,
    [switch]$NoMsi,
    [switch]$NoZip
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host '==> FileWatchRest Packaging' -ForegroundColor Cyan

# Inverted switches: default = create both; set internal flags
$CreateMsi = -not $NoMsi
$CreateZip = -not $NoZip

# Note: don't require publish directory for source ZIP or docs packaging.
# We'll only require it for packaging publish artifacts (MSI or publish ZIP).
$Parent = Split-Path $PSScriptRoot -Parent
$csproj = $null
$projPath = Join-Path $Parent 'FileWatchRest' 'FileWatchRest.csproj'
if (Test-Path $projPath) { [xml]$csproj = Get-Content $projPath }

# Detect version if not provided (use loaded csproj when available)
if (-not $Version) {
    # Find the Version element (robust, avoid property-access that fails under StrictMode)
    $versionNode = $csproj.SelectSingleNode('//Project/PropertyGroup/Version')
    if ($null -ne $versionNode) {
        $Version = $versionNode.InnerText.Trim()
    }
    else {
        $pgs = $csproj.Project.SelectNodes('PropertyGroup')
        foreach ($pg in $pgs) {
            $vn = $pg.SelectSingleNode('Version')
            if ($null -ne $vn) { $Version = $vn.InnerText.Trim(); break }
        }
    }

}

# Extract major.minor.patch for MSI (no pre-release tags)
$msiVersion = $Version -replace '-.*$', ''

# Create (fresh) output directory -- remove any previous artifacts so releases contain only latest files
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# Create installer script for ZIP package
$installerScript = @'
# Target-machine installer script for FileWatchRest
# Run as Administrator to install the service
param(
    [string]$ServiceName = 'FileWatchRest',
    [string]$ServiceDisplayName = 'File Watch REST Service',
    [string]$ServiceDescription = 'Modern file watching service with REST API integration'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Require admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

Write-Host "Installing $ServiceDisplayName..." -ForegroundColor Cyan

# Install directory
$installDir = Join-Path -Path $env:ProgramFiles -ChildPath $ServiceName
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir | Out-Null
}

# Copy files
Write-Host "Copying files to $installDir..." -ForegroundColor Gray
Get-ChildItem -Path $PSScriptRoot -Exclude '*.ps1' | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $installDir -Recurse -Force
}

# Create config and log directories
$configDir = Join-Path -Path $env:ProgramData -ChildPath $ServiceName
$logDir = $configDir
if (-not (Test-Path $configDir)) {
    New-Item -ItemType Directory -Path $configDir | Out-Null
}

# Find executable
$exe = Get-ChildItem -Path $installDir -Filter '*.exe' | Select-Object -First 1
if (-not $exe) {
    Write-Error "No executable found in $installDir"
    exit 1
}
$exePath = $exe.FullName

# Remove existing service if present
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "Stopping and removing existing service..." -ForegroundColor Yellow
    try {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    } catch {
        Write-Warning "Failed to stop service: $_"
    }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Create Windows service
Write-Host "Creating Windows service..." -ForegroundColor Gray
$binPathArg = "binPath=`"$exePath`""
& sc.exe create $ServiceName $binPathArg DisplayName= "`"$ServiceDisplayName`"" start= auto | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service (sc.exe exit code: $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# Set service description
& sc.exe description $ServiceName $ServiceDescription | Out-Null

# Set service recovery options (restart on failure)
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

# Start service
Write-Host "Starting service..." -ForegroundColor Gray
Start-Sleep -Milliseconds 500
Start-Service -Name $ServiceName

Write-Host "`nService installed successfully!" -ForegroundColor Green
Write-Host "  Install directory: $installDir" -ForegroundColor Gray
Write-Host "  Config directory:  $configDir" -ForegroundColor Gray
Write-Host "  Log directory:     $logDir" -ForegroundColor Gray
Write-Host "`nCreate your configuration file at:" -ForegroundColor Yellow
Write-Host "  $configDir\FileWatchRest.json" -ForegroundColor Yellow
Write-Host "`nService status:" -ForegroundColor Cyan
Get-Service -Name $ServiceName | Format-Table -AutoSize
'@

# Write installer script into the repo-root 'output' folder only (manual-install bundle).
# Do NOT place the installer into the top-level artifacts folder to avoid including it
# directly in GitHub Release assets. The manual publish ZIP will include this installer.
$repoOutputDir = Join-Path $Parent 'output'
if (-not (Test-Path $repoOutputDir)) { New-Item -ItemType Directory -Path $repoOutputDir | Out-Null }
$repoInstaller = Join-Path $repoOutputDir 'install_on_target.ps1'
Set-Content -Path $repoInstaller -Value $installerScript -Encoding UTF8
Write-Host "Created installer script in repo output: $repoInstaller" -ForegroundColor Gray

# Determine artifact source directory for packaging (publish or output)
$artifactSource = $PublishDir
if (-not (Test-Path (Join-Path $artifactSource 'FileWatchRest.exe'))) {
    if (Test-Path (Join-Path $Parent 'output' 'FileWatchRest.exe')) { $artifactSource = (Resolve-Path (Join-Path $Parent 'output')).Path }
    elseif (Test-Path (Join-Path $OutputDir 'FileWatchRest.exe')) { $artifactSource = (Resolve-Path $OutputDir).Path }
    else {
        Write-Error "Publish artifacts not found in '$PublishDir' or './output'. Run 'pwsh ./tools/build.ps1' or 'pwsh ./tools/release.ps1' to produce publish artifacts before packaging."
        exit 2
    }
}

if ($artifactSource -ne $PublishDir) {
    Write-Host "Using artifact source directory for packaging: $artifactSource" -ForegroundColor Gray
    $PublishDir = $artifactSource
}
# Create MSI package (requires publish artifacts)
if ($CreateMsi) {
    if (-not (Test-Path $PublishDir)) {
        # Write-Warning "Publish directory '$PublishDir' not found; skipping MSI creation. Run ci-build.ps1 first to produce publish artifacts."
        New-Item -ItemType Directory -Path $PublishDir | Out-Null
    }
    Write-Host "`n==> Creating MSI package..." -ForegroundColor Cyan


    # Prepare WiX source from template
    $appGuid = 'D179783F-B2AC-4B8C-9CA3-FCDB61236484'
    try {
        if ($csproj) {
            $g = $csproj.Project.PropertyGroup | ForEach-Object { $_.ApplicationGuid } | Where-Object { $_ } | Select-Object -First 1
            if ($g) { $appGuid = $g }
        }
    }
    catch { }

    # Build Product files fragment
    $hasExe = Test-Path (Join-Path $PublishDir 'FileWatchRest.exe')
    $hasDll = Test-Path (Join-Path $PublishDir 'FileWatchRest.dll')
    $productFilesXml = ''
    if ($hasExe) {
        $exePath = (Resolve-Path (Join-Path $PublishDir 'FileWatchRest.exe')).Path
        $exePath = $exePath -replace '&','&amp;'
        $exePath = $exePath -replace '<','&lt;'
        $exePath = $exePath -replace '>','&gt;'
        $productFilesXml += "<Component Id='MainExecutable' Guid='*' Bitness='always64'>`n  <File Source='$exePath' KeyPath='yes' />`n  <ServiceInstall Id='FileWatchRestService' Name='FileWatchRest' DisplayName='File Watch REST Service' Description='Modern file watching service with REST API integration' Type='ownProcess' Start='auto' ErrorControl='normal' Account='LocalSystem' />`n  <ServiceControl Id='StartService' Name='FileWatchRest' Start='install' Stop='both' Remove='uninstall' />`n</Component>`n"
    }
    if ($hasDll) {
        $dllPath = (Resolve-Path (Join-Path $PublishDir 'FileWatchRest.dll')).Path
        $dllPath = $dllPath -replace '&','&amp;'
        $dllPath = $dllPath -replace '<','&lt;'
        $dllPath = $dllPath -replace '>','&gt;'
        $productFilesXml += "<Component Id='ConfigFile' Guid='*' Bitness='always64'>`n  <File Source='$dllPath' KeyPath='yes' Checksum='yes' />`n</Component>`n"
    }
}

$templatePath = Join-Path $PSScriptRoot 'wix.xml'
if (-not (Test-Path $templatePath)) { $templatePath = Join-Path $Parent 'tools' 'wix.xml' }
if (Test-Path $templatePath) {
    $template = Get-Content -Raw -Path $templatePath
    # Write main template unchanged; product components will be provided as a separate fragment file
    $wxsContent = $template.TrimStart()
    $wxsPath = Join-Path $OutputDir 'FileWatchRest.wxs'
    Set-Content -Path $wxsPath -Value $wxsContent -Encoding UTF8
    Write-Host "Created WiX source: $wxsPath" -ForegroundColor Gray

    # Create a separate fragment file containing generated product components so we don't rely on string replacement
    $productWxsPath = Join-Path $OutputDir 'ProductFiles.wxs'
    if ($productFilesXml -and $productFilesXml.Trim().Length -gt 0) {
        $productFragment = "<Wix xmlns='http://wixtoolset.org/schemas/v4/wxs'>`n<Fragment>`n<ComponentGroup Id='ProductComponents' Directory='INSTALLFOLDER'>`n$productFilesXml`n</ComponentGroup>`n</Fragment>`n</Wix>"
        Set-Content -Path $productWxsPath -Value $productFragment -Encoding UTF8
        Write-Host "Created WiX product fragment: $productWxsPath" -ForegroundColor Gray
    }
    else { $productWxsPath = $null }
}
else {
    Write-Warning "WiX template not found at $templatePath. Skipping MSI creation."
    $wxsPath = $null
}

# Build MSI
$msiPath = Join-Path $OutputDir "FileWatchRest-$Version.msi"
try {
    if ($wxsPath) {
        if ($productWxsPath) {
            & wix build -arch x64 $wxsPath $productWxsPath -out $msiPath -d "MSI_VERSION=$msiVersion" -d "UPGRADE_GUID=$appGuid"
        }
        else {
            & wix build -arch x64 $wxsPath -out $msiPath -d "MSI_VERSION=$msiVersion" -d "UPGRADE_GUID=$appGuid"
        }
    }
    else { throw 'No WiX source to build' }
    if ($LASTEXITCODE -eq 0 -and (Test-Path $msiPath)) {
        $msiSize = (Get-Item $msiPath).Length / 1MB
        Write-Host "Created MSI: $msiPath ($([math]::Round($msiSize, 2)) MB)" -ForegroundColor Green
    }
    else {
        Write-Warning "WiX build failed (exit code: $LASTEXITCODE)"
    }
}
catch {
    Write-Warning "Failed to create MSI: $_"
}
# Remove WiX working files produced by wix build so artifacts contain only final packages
Get-ChildItem -Path $OutputDir -Filter '*.wxs' -File | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $OutputDir -Filter '*.wixpdb' -File | Remove-Item -Force -ErrorAction SilentlyContinue


# Create ZIP package of publish artifacts (requires publish folder)
if ($CreateZip) {
    if (-not (Test-Path $PublishDir)) {
        New-Item -ItemType Directory -Path $PublishDir | Out-Null
    }

    Write-Host "`n==> Creating ZIP package..." -ForegroundColor Cyan
    $zipPath = Join-Path $OutputDir "FileWatchRest-manual-$Version.zip"

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # If a single-file executable exists, prefer packaging that (smaller, self-contained). Otherwise include full publish folder.
    if (Test-Path (Join-Path $PublishDir 'FileWatchRest.exe')) {
        Write-Host 'Found single-file executable; creating ZIP with executable and installer only.' -ForegroundColor Gray
        $tempPack = Join-Path $env:TEMP "fwr_pkg_$([guid]::NewGuid().ToString())"
        New-Item -ItemType Directory -Path $tempPack | Out-Null
        Copy-Item -Path (Join-Path $PublishDir 'FileWatchRest.exe') -Destination $tempPack -Force
        # include installer script if present
        if (Test-Path $repoInstaller) { Copy-Item -Path $repoInstaller -Destination $tempPack -Force }
        Compress-Archive -Path (Join-Path $tempPack '*') -DestinationPath $zipPath -CompressionLevel Optimal
        Remove-Item -Recurse -Force $tempPack
    }
    else {
        Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    }

    if (Test-Path $zipPath) {
        $zipSize = (Get-Item $zipPath).Length / 1MB
        Write-Host "Created ZIP: $zipPath ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
    }

}

# Create source ZIP (include source folder + README)
if ($CreateZip) {
    Write-Host "`n==> Creating Source ZIP..." -ForegroundColor Cyan
    $sourceZipPath = Join-Path $OutputDir "FileWatchRest-source-$Version.zip"

    # Build list of items to include
    $items = @()
    if (Test-Path 'FileWatchRest') { $items += 'FileWatchRest' }
    if (Test-Path 'README.md') { $items += 'README.md' }

    if ($items.Count -eq 0) {
        Write-Warning 'No source files found to include in source ZIP. Skipping source archive.'
    }
    else {
        if (Test-Path $sourceZipPath) { Remove-Item $sourceZipPath -Force }
        # Build list of files under FileWatchRest excluding bin/ and obj/ paths, then compress from repo root so relative paths are preserved
        $repoRoot = (Resolve-Path -Path $Parent).Path
        Push-Location $repoRoot
        try {
            $files = Get-ChildItem -Path (Join-Path $repoRoot 'FileWatchRest') -Exclude 'bin', 'obj'
            if ($files.Count -gt 0) {
                Compress-Archive -Path $files -DestinationPath $sourceZipPath -CompressionLevel Optimal
                if (Test-Path $sourceZipPath) {
                    $sz = (Get-Item $sourceZipPath).Length / 1MB
                    Write-Host "Created Source ZIP: $sourceZipPath ($([math]::Round($sz,2)) MB)" -ForegroundColor Green
                }
            }
            else {
                Write-Warning 'No files found to include in source ZIP after excluding build folders.'
            }
        }
        catch {
            Write-Warning "Failed to create source ZIP: $_"
        }
        finally { Pop-Location }
    }
}

Write-Host "`n==> Packaging completed!" -ForegroundColor Green



Write-Host "`n==> Packaging completed!" -ForegroundColor Green
Write-Host "Output directory: $OutputDir" -ForegroundColor Gray
Get-ChildItem -Path $OutputDir -File | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}
