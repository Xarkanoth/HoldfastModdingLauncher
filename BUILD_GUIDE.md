# Build & Release Guide

This guide explains how to build mods, build the launcher, and publish releases to GitHub.

## Quick Reference

```powershell
# Build a mod (from project root)
.\build-mod.ps1 AdvancedAdminUI

# Build the launcher + create GitHub release (from HoldfastModdingLauncher folder)
cd HoldfastModdingLauncher
.\build.ps1
```

---

## Full Workflow

### Step 1: Build Your Mod

From the project root (`C:\Users\codyr\OneDrive\Desktop\Mods`):

```powershell
.\build-mod.ps1 AdvancedAdminUI
```

**What this does:**
- Increments the mod version in `ModVersions.json` (e.g., 1.0.2 → 1.0.3)
- Updates the `[BepInPlugin]` version attribute in source code
- Updates `.csproj` version properties
- Builds the mod DLL
- Copies the DLL to `HoldfastModdingLauncher/Mods/`

**Output:** `AdvancedAdminUI.dll` is placed in the launcher's Mods folder, ready for packaging.

---

### Step 2: Build the Launcher & Release

From the `HoldfastModdingLauncher` folder:

```powershell
cd HoldfastModdingLauncher
.\build.ps1
```

**What this does:**
1. Increments launcher version in `version.txt` (e.g., 1.0.79 → 1.0.80)
2. Updates `ModVersions.json` with new launcher version
3. Builds the launcher executable
4. Creates versioned release folder: `Release_1.0.80/`
   - Copies executable and dependencies
   - Copies `Mods/` folder (with your built mod DLLs)
   - Copies `ModVersions.json`
   - Creates `VERSION.txt`
5. Creates ZIP: `HoldfastModdingLauncher_v1.0.80.zip`
6. Commits and pushes changes to GitHub
7. Creates GitHub release with ZIP attached

**Release URL:** `https://github.com/Xarkanoth/HoldfastModdingLauncher/releases`

---

## Complete Example: Build Everything & Release

```powershell
# Navigate to project root
cd "C:\Users\codyr\OneDrive\Desktop\Mods"

# Step 1: Build the mod
.\build-mod.ps1 AdvancedAdminUI

# Step 2: Build launcher and create GitHub release
cd HoldfastModdingLauncher
.\build.ps1
```

---

## Version Management

All versions are tracked in `ModVersions.json`:

```json
{
  "Mods": {
    "AdvancedAdminUI": {
      "Version": "1.0.3",
      "DisplayName": "Advanced Admin UI",
      "Description": "...",
      "DllName": "AdvancedAdminUI.dll"
    }
  },
  "Launcher": {
    "Version": "1.0.80"
  }
}
```

- **Mod versions:** Auto-incremented by `build-mod.ps1`
- **Launcher version:** Auto-incremented by `build.ps1`

---

## Adding a New Mod

1. Create mod project folder: `NewMod/`
2. Create `.csproj` file with standard structure
3. Build it:
   ```powershell
   .\build-mod.ps1 NewMod
   ```
4. The mod will be automatically added to `ModVersions.json`

---

## File Structure After Build

```
Mods/
├── AdvancedAdminUI/          # Mod source code
├── HoldfastModdingLauncher/
│   ├── Mods/
│   │   └── AdvancedAdminUI.dll    # Built mod (copied here)
│   ├── Release_1.0.80/            # Versioned release folder
│   │   ├── HoldfastModdingLauncher.exe
│   │   ├── Mods/
│   │   │   └── AdvancedAdminUI.dll
│   │   ├── ModVersions.json
│   │   └── VERSION.txt
│   └── HoldfastModdingLauncher_v1.0.80.zip  # Release ZIP
├── ModVersions.json           # Centralized version tracking
└── build-mod.ps1              # Mod build script
```

---

## GitHub CLI Requirements

The automatic GitHub release requires the GitHub CLI (`gh`):

```powershell
# Install (one-time)
winget install GitHub.cli

# Login (one-time)
gh auth login
```

If `gh` is not installed, the build will still complete but you'll need to manually upload the ZIP to GitHub.

---

## Troubleshooting

### "gh is not recognized"
Restart PowerShell after installing GitHub CLI, or run:
```powershell
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
```

### Build fails
Check that you have:
- .NET SDK installed
- All NuGet packages restored
- Correct references in `.csproj`

### Mod DLL not found after build
Check `ModVersions.json` to ensure `DllName` matches your output DLL name.

