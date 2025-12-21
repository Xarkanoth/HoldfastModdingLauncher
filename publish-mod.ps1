# Publish Mod to GitHub Release
# This script builds a mod and creates a GitHub release with the mod DLL
# 
# Usage: .\publish-mod.ps1 -ModName "AdvancedAdminUI" [-Draft] [-PreRelease]
# 
# Prerequisites:
#   - GitHub CLI (gh) must be installed and authenticated: https://cli.github.com/
#   - Run 'gh auth login' to authenticate with GitHub
#
# Example:
#   .\publish-mod.ps1 -ModName "AdvancedAdminUI"
#   .\publish-mod.ps1 -ModName "AdvancedAdminUI" -Draft
#   .\publish-mod.ps1 -ModName "AdvancedAdminUI" -PreRelease

param(
    [Parameter(Mandatory=$true)]
    [string]$ModName,
    
    [switch]$Draft,
    [switch]$PreRelease,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path

# Configuration
$repoOwner = "Xarkanoth"
$repoName = "HoldfastMods"
$fullRepoPath = "$repoOwner/$repoName"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "   Publishing Mod: $ModName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check if GitHub CLI is installed
$ghPath = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghPath) {
    Write-Host "`nError: GitHub CLI (gh) is not installed!" -ForegroundColor Red
    Write-Host "Install it from: https://cli.github.com/" -ForegroundColor Yellow
    Write-Host "After installing, run: gh auth login" -ForegroundColor Yellow
    exit 1
}

# Check if authenticated
try {
    $authStatus = gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nError: Not authenticated with GitHub CLI!" -ForegroundColor Red
        Write-Host "Run: gh auth login" -ForegroundColor Yellow
        exit 1
    }
} catch {
    Write-Host "`nError: Failed to check GitHub auth status" -ForegroundColor Red
    Write-Host "Run: gh auth login" -ForegroundColor Yellow
    exit 1
}

# Load ModVersions.json
$versionsFile = Join-Path $scriptPath "ModVersions.json"
if (-not (Test-Path $versionsFile)) {
    Write-Host "Error: ModVersions.json not found at: $versionsFile" -ForegroundColor Red
    exit 1
}

$versions = Get-Content $versionsFile -Raw | ConvertFrom-Json

# Get mod info
if (-not $versions.Mods.PSObject.Properties[$ModName]) {
    Write-Host "Error: Mod '$ModName' not found in ModVersions.json" -ForegroundColor Red
    Write-Host "Available mods:" -ForegroundColor Yellow
    $versions.Mods.PSObject.Properties | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor Gray }
    exit 1
}

$modInfo = $versions.Mods.$ModName
$modVersion = $modInfo.Version
$modDisplayName = if ($modInfo.DisplayName) { $modInfo.DisplayName } else { $ModName }
$modDescription = if ($modInfo.Description) { $modInfo.Description } else { "" }
$modDllName = if ($modInfo.DllName) { $modInfo.DllName } else { "$ModName.dll" }
$modFolder = if ($modInfo.ProjectFolder) { $modInfo.ProjectFolder } else { $ModName }

Write-Host "`nMod Info:" -ForegroundColor Gray
Write-Host "  Name: $modDisplayName" -ForegroundColor White
Write-Host "  Version: $modVersion" -ForegroundColor White
Write-Host "  DLL: $modDllName" -ForegroundColor White

# Build the mod if not skipping
if (-not $SkipBuild) {
    Write-Host "`n--- Building Mod ---" -ForegroundColor Yellow
    
    $buildScript = Join-Path $scriptPath "build-mod.ps1"
    if (Test-Path $buildScript) {
        & $buildScript -ModProjectFolder $modFolder -Configuration "Release"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed!" -ForegroundColor Red
            exit 1
        }
        
        # Reload versions file as build script updates it
        $versions = Get-Content $versionsFile -Raw | ConvertFrom-Json
        $modInfo = $versions.Mods.$ModName
        $modVersion = $modInfo.Version
        Write-Host "New version after build: $modVersion" -ForegroundColor Green
    } else {
        Write-Host "Warning: build-mod.ps1 not found, skipping build" -ForegroundColor Yellow
    }
}

# Find the DLL
$possibleDllPaths = @(
    (Join-Path $scriptPath $modDllName),
    (Join-Path $scriptPath "$modFolder\bin\Release\net472\$modDllName"),
    (Join-Path $scriptPath "$modFolder\bin\Release\$modDllName"),
    (Join-Path $scriptPath "HoldfastModdingLauncher\Mods\$modDllName")
)

$dllPath = $null
foreach ($path in $possibleDllPaths) {
    if (Test-Path $path) {
        $dllPath = $path
        break
    }
}

if (-not $dllPath) {
    Write-Host "`nError: Could not find $modDllName" -ForegroundColor Red
    Write-Host "Searched paths:" -ForegroundColor Yellow
    $possibleDllPaths | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
    exit 1
}

Write-Host "`nDLL found at: $dllPath" -ForegroundColor Green

# Create release tag
$tagName = "v$modVersion-$ModName"
$releaseName = "$modDisplayName v$modVersion"

# Create release notes
$releaseNotes = @"
## $modDisplayName v$modVersion

$modDescription

### Installation
1. Download the DLL file below
2. Place it in your launcher's Mods folder
3. Or use the Mod Browser in the Holdfast Modding Launcher

### Changes
- See commit history for detailed changes

---
*Released via automated publishing script*
"@

Write-Host "`n--- Creating GitHub Release ---" -ForegroundColor Yellow
Write-Host "Repository: $fullRepoPath" -ForegroundColor Gray
Write-Host "Tag: $tagName" -ForegroundColor Gray
Write-Host "Release: $releaseName" -ForegroundColor Gray

# Build gh release command
$ghArgs = @(
    "release", "create", $tagName,
    "--repo", $fullRepoPath,
    "--title", $releaseName,
    "--notes", $releaseNotes,
    $dllPath
)

if ($Draft) {
    $ghArgs += "--draft"
    Write-Host "Mode: Draft release" -ForegroundColor Yellow
}

if ($PreRelease) {
    $ghArgs += "--prerelease"
    Write-Host "Mode: Pre-release" -ForegroundColor Yellow
}

# Create the release
try {
    Write-Host "`nCreating release..." -ForegroundColor Cyan
    & gh @ghArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n========================================" -ForegroundColor Green
        Write-Host "   Release Published Successfully!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "`nRelease URL: https://github.com/$fullRepoPath/releases/tag/$tagName" -ForegroundColor Cyan
        
        # Update mod-registry.json with new version
        $registryFile = Join-Path $scriptPath "mod-registry.json"
        if (Test-Path $registryFile) {
            Write-Host "`nUpdating mod-registry.json..." -ForegroundColor Yellow
            $registry = Get-Content $registryFile -Raw | ConvertFrom-Json
            
            $modEntry = $registry.mods | Where-Object { $_.id -eq $ModName }
            if ($modEntry) {
                $modEntry.version = $modVersion
                $registry.lastUpdated = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
                $registry | ConvertTo-Json -Depth 10 | Set-Content $registryFile -Encoding UTF8
                Write-Host "Updated registry to version $modVersion" -ForegroundColor Green
            }
        }
    } else {
        Write-Host "`nError: Failed to create release" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`nError creating release: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`nDone!" -ForegroundColor Green

