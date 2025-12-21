# Package script for Holdfast Modding Launcher
# Creates a clean distribution package

Write-Host "Packaging Holdfast Modding Launcher..." -ForegroundColor Cyan

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$releasePath = Join-Path $scriptPath "Release"
# Create package folder - use "Distribution" to avoid confusion with project folder name
$packagePath = Join-Path $scriptPath "Distribution"

# Check if Release folder exists
if (-not (Test-Path $releasePath)) {
    Write-Host "Error: Release folder not found. Please build the project first." -ForegroundColor Red
    exit 1
}

# Remove existing package folder if it exists
if (Test-Path $packagePath) {
    Write-Host "Removing existing package folder..." -ForegroundColor Yellow
    Remove-Item -Path $packagePath -Recurse -Force
}

# Create package folder
Write-Host "Creating package folder..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null

# Copy required files
Write-Host "Copying required files..." -ForegroundColor Yellow
$requiredFiles = @(
    "HoldfastModdingLauncher.exe",
    "HoldfastModdingLauncher.dll",
    "HoldfastModdingLauncher.deps.json",
    "HoldfastModdingLauncher.runtimeconfig.json"
)

foreach ($file in $requiredFiles) {
    $sourceFile = Join-Path $releasePath $file
    if (Test-Path $sourceFile) {
        Copy-Item -Path $sourceFile -Destination $packagePath -Force
        Write-Host "  [OK] $file" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $file (not found!)" -ForegroundColor Red
    }
}

# Create Mods folder (empty)
$modsPath = Join-Path $packagePath "Mods"
if (-not (Test-Path $modsPath)) {
    New-Item -ItemType Directory -Path $modsPath -Force | Out-Null
    Write-Host "  [OK] Created Mods folder" -ForegroundColor Green
}

# Create README for users
$readmePath = Join-Path $packagePath "README.txt"
$readmeContent = @"
Holdfast Modding Launcher
=========================

INSTALLATION:
1. Ensure .NET 6.0 Runtime is installed
   Download from: https://dotnet.microsoft.com/download/dotnet/6.0

2. Run HoldfastModdingLauncher.exe

3. On first run:
   - You'll be asked about creating a desktop shortcut
   - The launcher will automatically detect Holdfast
   - MelonLoader will be installed automatically

USAGE:
- Download mod DLL files from the mod repository
- Manually place mod DLL files in this Mods folder
- Toggle mods on/off in the launcher
- Click "Play Holdfast" to launch with selected mods

MODS:
- Mods are distributed separately from the launcher
- Download mod DLLs from the mod repository
- Manually copy downloaded DLL files into this Mods folder
- The launcher will automatically discover and list them
- No automatic installation - users must manually place mods here

SUPPORT:
- Click "Create Support Bundle" to collect logs for troubleshooting
- Support bundle will be created in your temp folder

NOTES:
- The launcher automatically manages MelonLoader
- Console is hidden by default for a clean experience
- Mod selections are remembered between launches
"@

Set-Content -Path $readmePath -Value $readmeContent -Encoding UTF8
Write-Host "  [OK] Created README.txt" -ForegroundColor Green

# Get package size
$packageSize = (Get-ChildItem -Path $packagePath -Recurse -File | Measure-Object -Property Length -Sum).Sum
$packageSizeKB = [math]::Round($packageSize / 1KB, 2)

Write-Host "`nPackage created successfully!" -ForegroundColor Green
Write-Host "`nPackage location: $packagePath" -ForegroundColor Cyan
Write-Host "Package size: $packageSizeKB KB" -ForegroundColor Gray
Write-Host "`nFiles included:" -ForegroundColor Yellow
Get-ChildItem -Path $packagePath -File | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host "  - Mods/ (folder)" -ForegroundColor White

Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Copy the 'Distribution' folder contents to your desired location" -ForegroundColor White
Write-Host "2. Users can run HoldfastModdingLauncher.exe from that location" -ForegroundColor White
Write-Host "3. Optionally create a ZIP file of the Distribution folder for distribution" -ForegroundColor White
Write-Host "`nNote: Use 'Release_Version_X_X_X' folders for versioned releases" -ForegroundColor Cyan

