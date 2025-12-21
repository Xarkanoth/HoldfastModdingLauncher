# Build script for mods with centralized version management
# Usage: .\build-mod.ps1 [ModProjectFolder] [Configuration]
# Example: .\build-mod.ps1 AdvancedAdminUI Release

param(
    [Parameter(Mandatory=$true)]
    [string]$ModProjectFolder,
    
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release"
)

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$modPath = Join-Path $scriptPath $ModProjectFolder
$modCsproj = Join-Path $modPath "$ModProjectFolder.csproj"
$versionsFile = Join-Path $scriptPath "ModVersions.json"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "   Building Mod: $ModProjectFolder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check if mod project exists
if (-not (Test-Path $modCsproj)) {
    Write-Host "Error: Mod project not found at: $modCsproj" -ForegroundColor Red
    exit 1
}

# Load or create ModVersions.json
if (-not (Test-Path $versionsFile)) {
    $defaultVersions = @{
        Mods = @{
            $ModProjectFolder = @{
                Version = "1.0.0"
                DisplayName = $ModProjectFolder
                Description = ""
                Requirements = ""
                DllName = "$ModProjectFolder.dll"
                ProjectFolder = $ModProjectFolder
            }
        }
        Launcher = @{
            Version = "1.0.0"
        }
    }
    $defaultVersions | ConvertTo-Json -Depth 10 | Set-Content $versionsFile -Encoding UTF8
    Write-Host "Created ModVersions.json with initial version 1.0.0" -ForegroundColor Yellow
}

$versions = Get-Content $versionsFile -Raw | ConvertFrom-Json

# Ensure this mod exists in the versions file
if (-not $versions.Mods.PSObject.Properties[$ModProjectFolder]) {
    $versions.Mods | Add-Member -NotePropertyName $ModProjectFolder -NotePropertyValue @{
        Version = "1.0.0"
        DisplayName = $ModProjectFolder
        Description = ""
        Requirements = ""
        DllName = "$ModProjectFolder.dll"
        ProjectFolder = $ModProjectFolder
    }
}

$modInfo = $versions.Mods.$ModProjectFolder
$currentVersion = $modInfo.Version
Write-Host "Current version: $currentVersion" -ForegroundColor Gray

# Increment version (increment patch version: 1.0.0 -> 1.0.1)
$versionParts = $currentVersion.Split('.')
if ($versionParts.Length -eq 3) {
    $major = [int]$versionParts[0]
    $minor = [int]$versionParts[1]
    $patch = [int]$versionParts[2]
    $patch++
    $newVersion = "$major.$minor.$patch"
} else {
    Write-Host "Warning: Unexpected version format. Using current version." -ForegroundColor Yellow
    $newVersion = $currentVersion
}

# Update ModVersions.json
$modInfo.Version = $newVersion
$versions | ConvertTo-Json -Depth 10 | Set-Content $versionsFile -Encoding UTF8
Write-Host "Version incremented to: $newVersion" -ForegroundColor Green

# Update the BepInPlugin attribute version in source code
$mainSourceFile = Join-Path $modPath "$($ModProjectFolder)Mod.cs"
if (-not (Test-Path $mainSourceFile)) {
    # Try alternative naming patterns
    $alternatives = @(
        (Join-Path $modPath "AdvancedAdminUIMod.cs"),
        (Join-Path $modPath "Plugin.cs"),
        (Join-Path $modPath "$ModProjectFolder.cs")
    )
    foreach ($alt in $alternatives) {
        if (Test-Path $alt) {
            $mainSourceFile = $alt
            break
        }
    }
}

if (Test-Path $mainSourceFile) {
    $content = Get-Content $mainSourceFile -Raw
    # Match pattern: [BepInPlugin("...", "...", "x.x.x")]
    $pattern = '\[BepInPlugin\("([^"]+)",\s*"([^"]+)",\s*"[^"]+"\)\]'
    $replacement = "[BepInPlugin(`"`$1`", `"`$2`", `"$newVersion`")]"
    $newContent = $content -replace $pattern, $replacement
    if ($content -ne $newContent) {
        Set-Content $mainSourceFile -Value $newContent -NoNewline
        Write-Host "Updated BepInPlugin version in source code" -ForegroundColor Gray
    }
}

# Also update .csproj version properties
if (Test-Path $modCsproj) {
    $csprojContent = Get-Content $modCsproj -Raw
    
    # Update Version property
    $csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$newVersion</Version>"
    $csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$newVersion.0</AssemblyVersion>"
    $csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$newVersion.0</FileVersion>"
    
    Set-Content $modCsproj -Value $csprojContent -NoNewline
    Write-Host "Updated .csproj version properties" -ForegroundColor Gray
}

# Build the mod with version
Write-Host "`nBuilding mod project..." -ForegroundColor Yellow
Push-Location $modPath
try {
    dotnet build -c $Configuration /p:Version=$newVersion
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    
    # Find the output DLL (check common output locations)
    $dllName = if ($modInfo.DllName) { $modInfo.DllName } else { "$ModProjectFolder.dll" }
    $outputDll = $null
    $possibleOutputs = @(
        (Join-Path $scriptPath $dllName),
        (Join-Path $modPath "bin\$Configuration\net472\$dllName"),
        (Join-Path $modPath "bin\$Configuration\$dllName"),
        (Join-Path $modPath $dllName)
    )
    
    foreach ($possiblePath in $possibleOutputs) {
        if (Test-Path $possiblePath) {
            $outputDll = $possiblePath
            break
        }
    }
    
    if ($null -eq $outputDll) {
        Write-Host "Warning: Could not locate output DLL. Check build output." -ForegroundColor Yellow
        Write-Host "Searched locations:" -ForegroundColor Yellow
        foreach ($loc in $possibleOutputs) {
            Write-Host "  - $loc" -ForegroundColor Gray
        }
    } else {
        $dllInfo = Get-Item $outputDll
        Write-Host "`nMod built successfully!" -ForegroundColor Green
        Write-Host "Output: $outputDll" -ForegroundColor Cyan
        Write-Host "Size: $([math]::Round($dllInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
        Write-Host "Version: $newVersion" -ForegroundColor Green
        
        # Copy to launcher's Mods folder
        $launcherModsDir = Join-Path $scriptPath "HoldfastModdingLauncher\Mods"
        if (Test-Path $launcherModsDir) {
            Copy-Item -Path $outputDll -Destination $launcherModsDir -Force
            Write-Host "Copied to launcher Mods folder" -ForegroundColor Gray
        }
        
        Write-Host "`n----------------------------------------" -ForegroundColor Gray
        Write-Host "Build complete! Version: $newVersion" -ForegroundColor Green
        Write-Host "----------------------------------------`n" -ForegroundColor Gray
    }
} finally {
    Pop-Location
}
