# AI Development Guide - Holdfast Modding

This guide provides everything an AI assistant needs to create, build, and publish mods for Holdfast: Nations At War using this repository.

## Repository Structure

```
C:\Users\codyr\OneDrive\Desktop\Mods\
├── AdvancedAdminUI/           # Example mod project
│   ├── AdvancedAdminUI.csproj
│   ├── AdvancedAdminUIMod.cs  # Main plugin class
│   └── Features/              # Feature implementations
├── LauncherCoreMod/           # Core mod (always enabled)
│   ├── LauncherCoreMod.csproj
│   └── LauncherCoreModPlugin.cs
├── CustomSplashScreen/        # Another mod example
├── HoldfastModdingLauncher/   # The launcher application
│   ├── Core/                  # Launcher core logic
│   ├── Services/              # Download, update services
│   ├── Mods/                  # Compiled mod DLLs go here
│   ├── Release/               # Release folder
│   │   ├── ModVersions.json   # Version tracking
│   │   └── Mods/              # Release mod DLLs
│   ├── version.txt            # Launcher version
│   └── build.ps1              # Launcher build script
├── BepInExLibs/               # BepInEx reference DLLs
├── ModVersions.json           # Mod version tracking (root)
├── mod-registry.json          # Mod browser registry
├── build-mod.ps1              # Mod build script
└── publish-mod.ps1            # GitHub release script
```

---

## Creating a New Mod

### Step 1: Create Project Structure

Create a new folder for your mod:

```
NewModName/
├── NewModName.csproj
└── NewModNamePlugin.cs
```

### Step 2: Create the .csproj File

Use this template for a BepInEx mod:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <OutputPath>..\</OutputPath>
    <AssemblyName>NewModName</AssemblyName>
    <RootNamespace>NewModName</RootNamespace>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- BepInEx (required) -->
    <Reference Include="BepInEx">
      <HintPath>..\BepInExLibs\BepInEx.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>..\BepInExLibs\0Harmony.dll</HintPath>
      <Private>False</Private>
    </Reference>
    
    <!-- Unity (add what you need) -->
    <Reference Include="UnityEngine">
      <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.CoreModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    
    <!-- Game (if needed) -->
    <Reference Include="Assembly-CSharp">
      <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\Assembly-CSharp.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
</Project>
```

### Step 3: Create the Main Plugin Class

```csharp
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace NewModName
{
    [BepInPlugin("com.xarkanoth.newmodname", "New Mod Name", "1.0.0")]
    public class NewModNamePlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        
        void Awake()
        {
            Log = Logger;
            Log.LogInfo("New Mod Name loaded!");
            
            // Initialize your mod here
        }
        
        void Update()
        {
            // Called every frame
        }
        
        void OnGUI()
        {
            // For drawing UI (use sparingly)
        }
    }
}
```

### Step 4: Add to ModVersions.json

Add your mod to `HoldfastModdingLauncher/Release/ModVersions.json`:

```json
{
    "Mods": {
        "NewModName": {
            "Version": "1.0.0",
            "DisplayName": "New Mod Name",
            "Description": "Description of what your mod does.",
            "Requirements": "",
            "DllName": "NewModName.dll",
            "ProjectFolder": "NewModName"
        }
    }
}
```

---

## Building Mods

### Build a Single Mod

```powershell
cd "C:\Users\codyr\OneDrive\Desktop\Mods"
.\build-mod.ps1 NewModName
```

This will:
- Increment version (1.0.0 → 1.0.1)
- Update `[BepInPlugin]` version in source
- Update `.csproj` version
- Build the DLL
- Copy to `HoldfastModdingLauncher/Mods/`

### Build Manually (without version increment)

```powershell
cd "C:\Users\codyr\OneDrive\Desktop\Mods\NewModName"
dotnet build -c Release
```

Output: `C:\Users\codyr\OneDrive\Desktop\Mods\NewModName.dll`

---

## Publishing Mods to GitHub

### Publish a Mod Release

```powershell
cd "C:\Users\codyr\OneDrive\Desktop\Mods"
.\publish-mod.ps1 NewModName
```

This will:
1. Build the mod (increments version)
2. Create GitHub release at `Xarkanoth/HoldfastMods`
3. Upload the DLL as release asset
4. Update `mod-registry.json` with new version and URLs

### Add to Mod Registry (for Mod Browser)

Add your mod to `mod-registry.json` for the launcher's Mod Browser:

```json
{
    "mods": [
        {
            "id": "NewModName",
            "name": "New Mod Name",
            "description": "Description of your mod.",
            "author": "Xarkanoth",
            "version": "1.0.0",
            "minLauncherVersion": "1.0.80",
            "requirements": "",
            "category": "Utilities",
            "tags": ["tag1", "tag2"],
            "repositoryUrl": "https://github.com/Xarkanoth/HoldfastMods",
            "releaseUrl": "https://api.github.com/repos/Xarkanoth/HoldfastMods/releases/tags/NewModName-v1.0.0",
            "downloadUrl": "https://github.com/Xarkanoth/HoldfastMods/releases/download/NewModName-v1.0.0/NewModName.dll",
            "dllName": "NewModName.dll",
            "isEnabled": true
        }
    ]
}
```

---

## Building the Launcher

### Update and Build Launcher

```powershell
cd "C:\Users\codyr\OneDrive\Desktop\Mods\HoldfastModdingLauncher"
dotnet build -c Release
```

### Increment Launcher Version

Update both files:
1. `HoldfastModdingLauncher/version.txt` - Just the version number
2. `HoldfastModdingLauncher/Release/ModVersions.json` - Under `"Launcher": { "Version": "..." }`

### Full Launcher Release

```powershell
cd "C:\Users\codyr\OneDrive\Desktop\Mods\HoldfastModdingLauncher"
.\build.ps1
```

---

## Git Workflow

### Committing Changes

Many files are in `.gitignore`. Use `-f` to force-add source files:

```powershell
cd "C:\Users\codyr\OneDrive\Desktop\Mods"

# Add specific files
git add -f AdvancedAdminUI/AdvancedAdminUIMod.cs
git add -f HoldfastModdingLauncher/Core/ModManager.cs
git add -f mod-registry.json

# Commit and push
git commit -m "Description of changes"
git push
```

### Files to Track (force add if needed)

- `*/**.cs` - Source code
- `*/*.csproj` - Project files
- `mod-registry.json` - Mod browser registry
- `ModVersions.json` - Version tracking
- `HoldfastModdingLauncher/Release/ModVersions.json` - Launcher's version file
- `HoldfastModdingLauncher/version.txt` - Launcher version

### Files NOT to Track

- `*.dll` - Built binaries
- `obj/` - Build artifacts
- `bin/` - Build output

---

## Core vs Optional Mods

### Core Mods (Always Enabled)

Core mods are defined in `HoldfastModdingLauncher/Core/ModManager.cs`:

```csharp
private static readonly HashSet<string> CoreMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "LauncherCoreMod.dll"  // Cannot be disabled
};
```

Core mods:
- Are always enabled
- Cannot be disabled by users
- Should contain essential launcher functionality

### Optional Mods

All other mods can be toggled on/off by users in the launcher.

---

## Common Unity/BepInEx References

Add these to `.csproj` as needed:

```xml
<!-- Input -->
<Reference Include="UnityEngine.InputLegacyModule">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.InputLegacyModule.dll</HintPath>
  <Private>False</Private>
</Reference>

<!-- Physics -->
<Reference Include="UnityEngine.PhysicsModule">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.PhysicsModule.dll</HintPath>
  <Private>False</Private>
</Reference>

<!-- Terrain -->
<Reference Include="UnityEngine.TerrainModule">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.TerrainModule.dll</HintPath>
  <Private>False</Private>
</Reference>

<!-- UI -->
<Reference Include="UnityEngine.UIModule">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.UIModule.dll</HintPath>
  <Private>False</Private>
</Reference>
<Reference Include="UnityEngine.UI">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.UI.dll</HintPath>
  <Private>False</Private>
</Reference>

<!-- IMGUI (OnGUI) -->
<Reference Include="UnityEngine.IMGUIModule">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\UnityEngine.IMGUIModule.dll</HintPath>
  <Private>False</Private>
</Reference>

<!-- Windows Forms (external UI) -->
<Reference Include="System.Windows.Forms">
  <Private>False</Private>
</Reference>
<Reference Include="System.Drawing">
  <Private>False</Private>
</Reference>

<!-- Game Scripts -->
<Reference Include="HoldfastSharedMethods">
  <HintPath>D:\SteamLibrary\steamapps\common\Holdfast Nations At War\Holdfast NaW_Data\Managed\HoldfastSharedMethods.dll</HintPath>
  <Private>False</Private>
</Reference>
```

---

## Harmony Patching

For patching game methods:

```csharp
using HarmonyLib;

public class MyPatches
{
    private static Harmony _harmony;
    
    public static void Apply()
    {
        _harmony = new Harmony("com.xarkanoth.mymod");
        
        // Find and patch a method
        var targetType = AccessTools.TypeByName("SomeGameClass");
        var targetMethod = targetType.GetMethod("SomeMethod");
        var postfix = typeof(MyPatches).GetMethod(nameof(SomeMethod_Postfix));
        
        _harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
    }
    
    public static void Remove()
    {
        _harmony?.UnpatchSelf();
    }
    
    // Postfix runs AFTER the original method
    private static void SomeMethod_Postfix(object __instance, ref object __result)
    {
        // Modify result or do something after
    }
    
    // Prefix runs BEFORE - return false to skip original
    private static bool SomeMethod_Prefix(object __instance)
    {
        return true; // true = run original, false = skip
    }
}
```

---

## Quick Command Reference

```powershell
# Build mod
.\build-mod.ps1 ModName

# Publish mod to GitHub
.\publish-mod.ps1 ModName

# Build launcher
cd HoldfastModdingLauncher
dotnet build -c Release

# Git commit (force add ignored files)
git add -f path/to/file.cs
git commit -m "message"
git push
```

