# Script to increment version number
# Usage: .\increment-version.ps1 [major|minor|patch]
# Default: patch (1.0.0 -> 1.0.1)

param(
    [Parameter(Position=0)]
    [ValidateSet("major", "minor", "patch")]
    [string]$Increment = "patch"
)

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionFile = Join-Path $scriptPath "version.txt"

if (-not (Test-Path $versionFile)) {
    Write-Host "Creating version.txt with 1.0.0" -ForegroundColor Yellow
    "1.0.0" | Out-File -FilePath $versionFile -Encoding ASCII
}

$currentVersion = (Get-Content $versionFile).Trim()
$versionParts = $currentVersion.Split('.')

if ($versionParts.Length -ne 3) {
    Write-Host "Error: Invalid version format. Expected format: X.Y.Z" -ForegroundColor Red
    exit 1
}

$major = [int]$versionParts[0]
$minor = [int]$versionParts[1]
$patch = [int]$versionParts[2]

switch ($Increment) {
    "major" {
        $major++
        $minor = 0
        $patch = 0
    }
    "minor" {
        $minor++
        $patch = 0
    }
    "patch" {
        $patch++
    }
}

$newVersion = "$major.$minor.$patch"
Set-Content -Path $versionFile -Value $newVersion -Encoding ASCII

Write-Host "Version incremented: $currentVersion -> $newVersion" -ForegroundColor Green
Write-Host "Updated version.txt" -ForegroundColor Cyan

