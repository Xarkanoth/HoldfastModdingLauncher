# Distribution Guide

## What to Give Users

### Required Files

Users need these files from the `Release` folder:

1. **HoldfastModdingLauncher.exe** - Main executable (REQUIRED)
2. **HoldfastModdingLauncher.dll** - Main assembly (REQUIRED)
3. **HoldfastModdingLauncher.deps.json** - Dependency information (REQUIRED)
4. **HoldfastModdingLauncher.runtimeconfig.json** - Runtime configuration (REQUIRED)

### Optional Files

- **HoldfastModdingLauncher.pdb** - Debug symbols (NOT needed for end users)
- **Mods/** folder - Can be empty or contain mod DLLs (will be auto-created if missing)

### Prerequisites

Users must have **.NET 6.0 Runtime** installed on their system.

**Download:** https://dotnet.microsoft.com/download/dotnet/6.0

## Distribution Package Structure

Create a ZIP file with this structure:

```
HoldfastModdingLauncher/
├── HoldfastModdingLauncher.exe
├── HoldfastModdingLauncher.dll
├── HoldfastModdingLauncher.deps.json
├── HoldfastModdingLauncher.runtimeconfig.json
└── Mods/                    (optional - can be empty)
    └── (place mod DLLs here)
```

## Quick Distribution Checklist

- [ ] Copy all 4 required files to a folder
- [ ] Optionally create an empty `Mods` folder
- [ ] Create a ZIP file of the folder
- [ ] Include installation instructions (see below)

## Installation Instructions for Users

1. **Install .NET 6.0 Runtime** (if not already installed)
   - Download from: https://dotnet.microsoft.com/download/dotnet/6.0
   - Run the installer
   - Restart computer if prompted

2. **Extract the launcher**
   - Extract the ZIP file to any location (e.g., Desktop, Program Files, etc.)
   - Keep all files together in the same folder

3. **Add mods (optional)**
   - Download mod DLL files from the mod repository
   - Manually copy downloaded DLL files into the `Mods` folder
   - The launcher will discover them automatically
   - Installation is manual - users must place mods themselves

4. **Run the launcher**
   - Double-click `HoldfastModdingLauncher.exe`
   - On first run, you'll be asked about creating a desktop shortcut
   - The launcher will automatically:
     - Detect Holdfast installation
     - Install MelonLoader (if needed)
     - Configure everything

5. **Select mods and play**
   - Toggle mods on/off in the launcher
   - Click "Play Holdfast" to launch with your selected mods

## Alternative: Self-Contained Deployment

If you want to avoid requiring .NET 6.0 Runtime installation, you can create a self-contained deployment:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

This creates a larger package (~70MB) but includes everything needed. The output will be in `bin/Release/net6.0-windows/win-x64/publish/`.

## File Sizes (Approximate)

- Framework-dependent (current): ~150 KB executable + .NET Runtime required
- Self-contained: ~70 MB (includes .NET Runtime)

## Mod Distribution

**Mods are distributed separately from the launcher.**

### For Mod Developers:
1. Build your mod using `dotnet build` or the provided `build-mod.ps1` script
2. Upload the compiled `.dll` file to your mod repository/hosting location
3. Provide download links to users

### For Users:
1. Download mod DLL files from the mod repository
2. Place downloaded DLL files in the `HoldfastModdingLauncher\Mods` folder
3. The launcher will automatically discover and list them
4. Toggle mods on/off in the launcher UI

### Building Mods Separately

Use the `build-mod.ps1` script in the project root:

```powershell
.\build-mod.ps1 AdminRings Release
```

This will build the mod and show you where the output DLL is located.

## Notes

- The `first_run.flag` file is auto-generated and should NOT be included in distribution
- Users can place mod DLLs in the `Mods` folder at any time
- The launcher creates `mods.json` automatically to remember mod selections
- Logs are stored in `%LocalAppData%\HoldfastModdingLauncher\Logs\`
- **Mods are NOT included in the launcher distribution package**

