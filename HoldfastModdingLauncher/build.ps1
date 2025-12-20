# Build script for Holdfast Modding Launcher
# This script builds the Release version and creates versioned release folders

Write-Host "Building Holdfast Modding Launcher..." -ForegroundColor Cyan

# Navigate to the launcher directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

# Read current version
$versionFile = Join-Path $scriptPath "version.txt"
if (-not (Test-Path $versionFile)) {
    "1.0.0" | Out-File -FilePath $versionFile -Encoding ASCII -NoNewline
}
$currentVersion = (Get-Content $versionFile -Raw).Trim()
Write-Host "Current version: $currentVersion" -ForegroundColor Cyan

# Increment version before build (increment patch version: 1.0.0 -> 1.0.1)
$versionParts = $currentVersion.Split('.')
if ($versionParts.Length -eq 3) {
    $major = [int]$versionParts[0]
    $minor = [int]$versionParts[1]
    $patch = [int]$versionParts[2]
    $patch++
    $newVersion = "$major.$minor.$patch"
} else {
    # Fallback if version format is unexpected
    Write-Host "Warning: Unexpected version format. Using current version." -ForegroundColor Yellow
    $newVersion = $currentVersion
}

# Update version.txt with new version before building
# Write without newline to ensure clean file
[System.IO.File]::WriteAllText($versionFile, $newVersion, [System.Text.Encoding]::ASCII)
Write-Host "Version incremented to: $newVersion" -ForegroundColor Green

# Also update ModVersions.json in parent directory
$modVersionsFile = Join-Path (Split-Path -Parent $scriptPath) "ModVersions.json"
if (Test-Path $modVersionsFile) {
    try {
        $content = Get-Content $modVersionsFile -Raw
        # Use regex to update just the Launcher version while preserving JSON formatting
        $pattern = '("Launcher"\s*:\s*\{\s*"Version"\s*:\s*")[^"]*(")'
        $replacement = '${1}' + $newVersion + '${2}'
        $content = [regex]::Replace($content, $pattern, $replacement)
        Set-Content $modVersionsFile -Value $content -NoNewline -Encoding UTF8
        Write-Host "Updated ModVersions.json launcher version" -ForegroundColor Gray
    } catch {
        Write-Host "Warning: Could not update ModVersions.json: $_" -ForegroundColor Yellow
    }
}

# Verify the write worked
$verifyVersion = (Get-Content $versionFile -Raw).Trim()
if ($verifyVersion -ne $newVersion) {
    Write-Host "Warning: Version file verification failed. Expected: $newVersion, Got: $verifyVersion" -ForegroundColor Yellow
}

# Build Release version - pass version as MSBuild property to ensure it's used
Write-Host "`nBuilding Release configuration..." -ForegroundColor Yellow
dotnet build HoldfastModdingLauncher.csproj -c Release /p:Version=$newVersion

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    
    # Create versioned release folder
    $versionedFolder = "Release_$newVersion"
    $versionedPath = (Join-Path $scriptPath $versionedFolder).Trim()
    
    if (Test-Path $versionedPath) {
        Write-Host "`nRemoving existing versioned folder..." -ForegroundColor Yellow
        Remove-Item -Path $versionedPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "`nCreating versioned release folder: $versionedFolder" -ForegroundColor Yellow
    $null = New-Item -ItemType Directory -Path $versionedPath -Force
    
    # Ensure directory was created
    if (-not (Test-Path $versionedPath)) {
        Write-Host "Error: Failed to create versioned folder at: $versionedPath" -ForegroundColor Red
        exit 1
    }
    
    # Copy files from Release folder
    $releasePath = Join-Path $scriptPath "Release"
    $requiredFiles = @(
        "HoldfastModdingLauncher.exe",
        "HoldfastModdingLauncher.dll",
        "HoldfastModdingLauncher.deps.json",
        "HoldfastModdingLauncher.runtimeconfig.json"
    )
    
    # Copy Resources folder if it exists
    $resourcesPath = Join-Path $releasePath "Resources"
    if (Test-Path $resourcesPath) {
        $targetResourcesPath = Join-Path $versionedPath "Resources"
        Copy-Item -Path $resourcesPath -Destination $targetResourcesPath -Recurse -Force
        Write-Host "  [OK] Resources folder" -ForegroundColor Green
    }
    
    # Copy Mods folder from project directory (not release) if it exists
    $modsPath = Join-Path $scriptPath "Mods"
    if (Test-Path $modsPath) {
        $targetModsPath = Join-Path $versionedPath "Mods"
        Copy-Item -Path $modsPath -Destination $targetModsPath -Recurse -Force
        Write-Host "  [OK] Mods folder" -ForegroundColor Green
    } else {
        # Create empty Mods folder
        $targetModsPath = Join-Path $versionedPath "Mods"
        New-Item -ItemType Directory -Path $targetModsPath -Force | Out-Null
        Write-Host "  [OK] Mods folder (empty)" -ForegroundColor Yellow
    }
    
    # Copy ModVersions.json from parent directory if it exists
    $modVersionsSource = Join-Path (Split-Path -Parent $scriptPath) "ModVersions.json"
    if (Test-Path $modVersionsSource) {
        Copy-Item -Path $modVersionsSource -Destination $versionedPath -Force
        # Also copy to Release folder
        Copy-Item -Path $modVersionsSource -Destination $releasePath -Force
        Write-Host "  [OK] ModVersions.json" -ForegroundColor Green
    }
    
    foreach ($file in $requiredFiles) {
        $sourceFile = Join-Path $releasePath $file
        if (Test-Path $sourceFile) {
            Copy-Item -Path $sourceFile -Destination $versionedPath -Force
            Write-Host "  [OK] $file" -ForegroundColor Green
        }
    }
    
    # Create version info file
    $versionInfo = @"
Holdfast Modding Launcher
Version: $newVersion
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@
    $versionInfoPath = Join-Path $versionedPath "VERSION.txt"
    Set-Content -Path $versionInfoPath -Value $versionInfo -Encoding UTF8
    
    $outputPath = Join-Path $versionedPath "HoldfastModdingLauncher.exe"
    
    if (Test-Path $outputPath) {
        $fileInfo = Get-Item $outputPath
        Write-Host "`nVersioned release created!" -ForegroundColor Green
        Write-Host "`nLocation: $versionedPath" -ForegroundColor Cyan
        Write-Host "File size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
        Write-Host "`nBuild version: $newVersion" -ForegroundColor Green
    } else {
        Write-Host "`nWarning: Executable not found at expected location." -ForegroundColor Yellow
    }
} else {
    Write-Host "`nBuild failed! Check the error messages above." -ForegroundColor Red
    exit 1
}

