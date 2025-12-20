# Custom Icon Setup

The application is configured to use a custom icon (your glitch-art eye logo) throughout the application.

## Quick Setup

1. **Convert your image to .ico format**
   - Your image should be converted to Windows .ico format
   - Recommended sizes: 16x16, 32x32, 48x48, 256x256 pixels (multi-resolution)
   - Online converters:
     - https://convertio.co/png-ico/
     - https://www.icoconverter.com/
     - https://cloudconvert.com/png-to-ico

2. **Save as `app.ico`**
   - Name the file exactly: `app.ico`
   - Place it in: `HoldfastModdingLauncher/Resources/app.ico`

3. **Rebuild the application**
   ```powershell
   .\build.ps1
   ```

## Where the Icon Appears

Once set up, your custom icon will appear in:

- ✅ **Application executable** (`HoldfastModdingLauncher.exe`) - Windows taskbar, file explorer
- ✅ **Main window** - Title bar and taskbar
- ✅ **Installer window** - Title bar
- ✅ **Settings window** - Title bar
- ✅ **Desktop shortcut** (if created) - Shortcut icon

## Technical Details

- The icon is embedded in the executable via `.csproj` `ApplicationIcon` property
- Forms load the icon from embedded resources or Resources folder
- The icon is automatically copied to release folders during build
- Falls back to default Windows icon if custom icon not found

## File Structure

```
HoldfastModdingLauncher/
├── Resources/
│   └── app.ico          ← Place your icon here
├── HoldfastModdingLauncher.csproj
└── ...
```

## Troubleshooting

**Icon not showing?**
- Ensure file is named exactly `app.ico` (case-sensitive)
- Verify file is in `Resources/` folder
- Check that icon file is valid .ico format (not just renamed .png)
- Rebuild the project after adding the icon

**Icon appears in some places but not others?**
- Windows may cache icons - restart Windows Explorer or reboot
- Ensure icon contains multiple resolutions (16x16, 32x32, 48x48, 256x256)

