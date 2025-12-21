# Holdfast Modding Launcher

A clean, user-friendly launcher application for Holdfast: Nations At War modding that automatically manages MelonLoader installation and configuration.

## Overview

The Holdfast Modding Launcher provides a seamless modding experience by:

- **Automatic Installation**: Installs and configures MelonLoader automatically
- **Hidden Console**: MelonLoader console is hidden by default for normal users
- **Owner Mode**: Debug console can be enabled for developers/owners
- **Easy Support**: One-click support bundle creation for troubleshooting
- **Single Install**: Users only need to install this launcher - everything else is handled automatically

## Features

### For Users

- **One-Click Play**: Simple "Play Holdfast" button launches the game with mods
- **Automatic Setup**: First launch automatically installs MelonLoader and configures everything
- **Clean Experience**: No MelonLoader console window, no technical complexity
- **Support Tools**: Easy log collection for troubleshooting

### For Owners/Developers

- **Owner Mode**: Enable debug console via:
  - `Owner.key` file in launcher directory
  - `--debug` launch flag
  - Environment variable `HOLDFAST_MODDING_OWNER=true`
- **Full Control**: Console visibility can be toggled in owner mode
- **Debug Access**: Full access to MelonLoader console and logs when needed

## Installation

1. Build the launcher (see Build Instructions below)
2. Copy the launcher files to your desired location
3. Download mod DLLs from the mod repository and place them in the `Mods` folder
4. Run `HoldfastModdingLauncher.exe`

On first launch, the launcher will:
- Detect your Holdfast installation
- Download and install MelonLoader automatically
- Configure preferences to hide the console
- Discover and list available mods from the `Mods` folder

## Usage

### Normal Users

1. Launch `HoldfastModdingLauncher.exe`
2. Wait for setup to complete (first time only)
3. Click "Play Holdfast"
4. Enjoy modded gameplay!

### Owner/Developer Mode

To enable owner mode, create a file named `Owner.key` in the same directory as the launcher, or launch with `--debug` flag:

```bash
HoldfastModdingLauncher.exe --debug
```

When owner mode is active:
- A "Enable Debug Console" checkbox appears
- You can toggle console visibility
- Full access to MelonLoader console and debugging tools

## Support Bundle

If you encounter issues:

1. Click "Create Support Bundle"
2. The launcher collects:
   - MelonLoader logs (last 10 log files)
   - Mod logs (all .log files in mods directory)
   - Launcher logs (last 5 launcher log files)
   - Configuration files (Preferences.cfg and mod configs)
   - System information (OS, .NET version, Holdfast installation details)
3. A ZIP file is created in your temp folder with timestamp
4. Send the ZIP file to support

The support bundle includes everything needed for troubleshooting without exposing developer tools to users.

## Architecture

### Core Components

- **HoldfastManager**: Detects Holdfast installation via Steam registry and common paths
- **MelonLoaderManager**: Downloads and installs MelonLoader automatically
- **PreferencesManager**: Manages `Preferences.cfg` to control console visibility
- **OwnerModeManager**: Detects owner mode via key file, flag, or environment variable
- **LogCollector**: Creates support bundles with logs and system info

### Console Control

The launcher enforces console visibility through a "soft lock" mechanism:

1. **Preferences Rewritten on Every Launch**: The launcher rewrites `Preferences.cfg` every time it starts, ensuring console settings are always enforced
2. **Read-Only Protection**: After writing preferences, the file is set to read-only to prevent manual modification
3. **Console Disabled by Default**: `Console_Enabled = false` for normal users
4. **Logs Always Enabled**: `Log_Enabled = true` ensures logs are written to disk even when console is hidden
5. **Owner Override**: In owner mode, console can be enabled via the UI checkbox

This ensures users can't accidentally enable the console, while the launcher can still update preferences when needed (it temporarily removes read-only before writing).

## Build Instructions

### Prerequisites

- .NET 6.0 SDK or later
- Visual Studio 2022 (recommended) or Visual Studio Code

### Building

1. Open `Mods.sln` in Visual Studio
2. Build the solution (Ctrl+Shift+B)
3. Or build from command line:
   ```powershell
   dotnet build HoldfastModdingLauncher\HoldfastModdingLauncher.csproj -c Release
   ```

### Output

The compiled executable will be in:
- `HoldfastModdingLauncher\bin\Release\net6.0-windows\HoldfastModdingLauncher.exe`

## Configuration

### MelonLoader Preferences

The launcher automatically manages `MelonLoader/Preferences.cfg` and enforces settings on every launch:

- `Console_Enabled`: `false` for normal users, `true` for owner mode (when enabled)
- `Console_Title`: Cleared when console is disabled
- `Console_AlwaysOnTop`: `false` when console is disabled
- `Log_Enabled`: Always `true` (logs always written to disk)
- `Log_File_Enabled`: Always `true`
- `Log_File_Append`: `false` (fresh log each launch)
- `Log_File_Path`: `Logs` (default MelonLoader logs directory)
- `Log_File_Console_Enabled`: `false` (don't log console output to file)

**Important**: The preferences file is set to read-only after being written to prevent manual modification. The launcher will update it automatically on each launch.

### Mod Files

**Mods are distributed separately from the launcher.**

1. **For Users:**
   - Download mod DLL files from the mod repository
   - **Manually copy** downloaded DLL files into the `HoldfastModdingLauncher\Mods` folder
   - The launcher will automatically discover and list them on next launch
   - Toggle mods on/off in the launcher UI
   - **Installation is manual** - users must download and place mods themselves

2. **For Mod Developers:**
   - Build your mod using `dotnet build` or the `build-mod.ps1` script
   - Upload the compiled `.dll` file to your hosting location
   - Provide download links to users

3. **Mod Deployment:**
   - On launch, the launcher copies enabled mods from `Mods` folder to:
     ```
     Holdfast/MelonLoader/Mods/[ModName].dll
     ```
   - Only enabled mods are copied to the game directory
   - Disabled mods are removed from the game directory automatically

## Troubleshooting

### Holdfast Not Found

- Ensure Holdfast: Nations At War is installed via Steam
- Use Settings to manually specify the installation path
- Check that `Holdfast NaW.exe` exists in the specified directory

### MelonLoader Installation Fails

- Check internet connection (required for first-time download)
- Verify write permissions to Holdfast directory
- Check launcher logs in `%LocalAppData%\HoldfastModdingLauncher\Logs\`

### Console Still Appears

- Verify owner mode is not active (check for `Owner.key` file)
- Check `MelonLoader/Preferences.cfg` - `Console_Enabled` should be `false`
- Ensure launcher runs before launching Holdfast (it updates preferences on launch)

## Technical Details

- **Framework**: .NET 6.0 Windows Forms
- **Target**: Windows x64
- **Dependencies**: 
  - System.IO.Compression.ZipFile (for support bundles)
  - Windows Forms (UI)

## License

This launcher is provided as-is for use with Holdfast: Nations At War modding.

