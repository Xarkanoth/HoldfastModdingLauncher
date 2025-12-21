# Folder Structure Explanation

This document explains the different folders created during the build process.

## Build Output Folders

### `Release\`
- **Purpose**: Direct build output from `dotnet build`
- **Created by**: MSBuild (configured in `.csproj` file)
- **Contains**: Compiled DLL, EXE, and runtime files
- **When**: Created automatically on every build
- **Can be deleted**: Yes, it will be recreated on next build

### `Release_Version_X_X_X\` (e.g., `Release_Version_1_0_0\`)
- **Purpose**: Versioned release folder for distribution
- **Created by**: `build.ps1` script
- **Contains**: Copy of Release files + VERSION.txt + Mods folder
- **When**: Created when you run `.\build.ps1`
- **Use**: This is what you distribute to users (versioned releases)

### `Distribution\`
- **Purpose**: Clean distribution package (same files, different structure)
- **Created by**: `package.ps1` script
- **Contains**: Same as Release_Version but with README.txt
- **When**: Created when you run `.\package.ps1`
- **Use**: Alternative packaging format (less common)

## Which Folder Should You Use?

**For Distribution:**
- Use `Release_Version_X_X_X\` folders - these are versioned and ready to distribute
- Each version gets its own folder, making it easy to track releases

**For Development:**
- Use `Release\` folder - this is the latest build output
- Can be cleaned up anytime (will be recreated on next build)

**For Custom Packaging:**
- Use `Distribution\` folder if you need a different package structure
- Less commonly used than versioned releases

## Cleaning Up

You can safely delete:
- `Release\` - Will be recreated on next build
- `Distribution\` - Will be recreated if you run package.ps1
- Old `Release_Version_X_X_X\` folders - Keep only the latest version(s)

## Recommended Workflow

1. **Build**: Run `.\build.ps1` → Creates `Release_Version_X_X_X\`
2. **Distribute**: Share the `Release_Version_X_X_X\` folder
3. **Clean**: Delete old version folders as needed

