# Holdfast Mods

This folder contains all available mods for Holdfast: Nations at War that can be installed via the Holdfast Modding Launcher.

## Available Mods

| Mod | Description | Latest Version |
|-----|-------------|----------------|
| [Advanced Admin UI](./AdvancedAdminUI/) | Admin tools: Cavalry Vis, Rambo/AFK indicators, Minimap, Admin UI | v1.0.23 |

## Installation

### Using Mod Browser (Recommended)
1. Open **Holdfast Modding Launcher**
2. Click **"📦 Browse & Download Mods"**
3. Browse available mods
4. Click **Install** on any mod you want

### Manual Installation
1. Download the mod DLL from the mod's [Releases](https://github.com/Xarkanoth/HoldfastModdingLauncher/releases)
2. Place it in your launcher's `Mods` folder
3. Enable the mod in the launcher

## For Mod Developers

Each mod has its own folder containing:
- `mod-info.json` - Mod metadata and release history
- `README.md` - Mod documentation

To add a new mod:
1. Create a folder with your mod name
2. Add `mod-info.json` with your mod's metadata
3. Add `README.md` with documentation
4. Update `mod-registry.json` in the root to include your mod
5. Create a GitHub release with tag `mod-{ModName}-v{Version}`

