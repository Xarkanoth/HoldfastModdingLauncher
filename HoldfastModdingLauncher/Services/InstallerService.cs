using System;
using System.Diagnostics;
using System.IO;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher.Services
{
    public class InstallerService
    {
        /// <summary>
        /// Installs the launcher to the Holdfast directory.
        /// Copies all required files to HoldfastModdingLauncher folder in the game directory.
        /// </summary>
        public static bool InstallToHoldfastDirectory(string holdfastPath)
        {
            try
            {
                if (string.IsNullOrEmpty(holdfastPath) || !Directory.Exists(holdfastPath))
                {
                    Logger.LogError("Invalid Holdfast path for installation");
                    return false;
                }

                string targetDir = Path.Combine(holdfastPath, "HoldfastModdingLauncher");
                
                // Create target directory
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Logger.LogInfo($"Created installation directory: {targetDir}");
                }

                // Get current launcher location (use AppContext for .NET 6+ compatibility)
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(currentDir))
                {
                    currentDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
                }
                Logger.LogInfo($"Source directory: {currentDir}");

                // Files to copy
                string[] filesToCopy = {
                    "HoldfastModdingLauncher.exe",
                    "HoldfastModdingLauncher.dll",
                    "HoldfastModdingLauncher.deps.json",
                    "HoldfastModdingLauncher.runtimeconfig.json"
                };

                bool allCopied = true;
                foreach (string fileName in filesToCopy)
                {
                    string sourcePath = Path.Combine(currentDir, fileName);
                    string targetPath = Path.Combine(targetDir, fileName);

                    if (File.Exists(sourcePath))
                    {
                        // Skip if already in target location
                        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.LogInfo($"Skipping {fileName} - already in target location");
                            continue;
                        }

                        File.Copy(sourcePath, targetPath, true);
                        Logger.LogInfo($"Copied {fileName} to {targetDir}");
                    }
                    else
                    {
                        Logger.LogWarning($"Source file not found: {sourcePath}");
                        allCopied = false;
                    }
                }

                // Create Mods folder in target directory
                string modsDir = Path.Combine(targetDir, "Mods");
                if (!Directory.Exists(modsDir))
                {
                    Directory.CreateDirectory(modsDir);
                    Logger.LogInfo($"Created Mods directory: {modsDir}");
                }

                // Copy existing Mods folder contents if they exist
                string sourceModsDir = Path.Combine(currentDir, "Mods");
                if (Directory.Exists(sourceModsDir) && !string.Equals(sourceModsDir, modsDir, StringComparison.OrdinalIgnoreCase))
                {
                    var modFiles = Directory.GetFiles(sourceModsDir, "*.dll");
                    foreach (string modFile in modFiles)
                    {
                        string targetModFile = Path.Combine(modsDir, Path.GetFileName(modFile));
                        File.Copy(modFile, targetModFile, true);
                        Logger.LogInfo($"Copied mod: {Path.GetFileName(modFile)}");
                    }
                }

                // Copy Resources folder (contains icon)
                string sourceResourcesDir = Path.Combine(currentDir, "Resources");
                string targetResourcesDir = Path.Combine(targetDir, "Resources");
                if (Directory.Exists(sourceResourcesDir) && !string.Equals(sourceResourcesDir, targetResourcesDir, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(targetResourcesDir))
                    {
                        Directory.CreateDirectory(targetResourcesDir);
                    }
                    foreach (string file in Directory.GetFiles(sourceResourcesDir))
                    {
                        string targetFile = Path.Combine(targetResourcesDir, Path.GetFileName(file));
                        File.Copy(file, targetFile, true);
                        Logger.LogInfo($"Copied resource: {Path.GetFileName(file)}");
                    }
                }

                // Create first_run.flag in target directory so it won't show installer again
                string flagPath = Path.Combine(targetDir, "first_run.flag");
                File.WriteAllText(flagPath, DateTime.Now.ToString());
                Logger.LogInfo("Created first_run.flag in target directory");

                Logger.LogInfo($"Launcher installed to: {targetDir}");
                return allCopied;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to install launcher to Holdfast directory: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Creates a desktop shortcut for the launcher using PowerShell.
        /// </summary>
        public static bool CreateDesktopShortcut(string? launcherPath = null)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "Holdfast Modding.lnk");
                string exePath = launcherPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                string workingDir = Path.GetDirectoryName(exePath) ?? "";

                // Use PowerShell to create shortcut
                string psScript = $@"
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut('{shortcutPath.Replace("'", "''")}')
$Shortcut.TargetPath = '{exePath.Replace("'", "''")}'
$Shortcut.WorkingDirectory = '{workingDir.Replace("'", "''")}'
$Shortcut.Description = 'Holdfast Modding Launcher'
$Shortcut.Save()
";

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(processStartInfo))
                {
                    process?.WaitForExit();
                    if (process?.ExitCode == 0 && File.Exists(shortcutPath))
                    {
                        Logger.LogInfo("Desktop shortcut created successfully");
                        return true;
                    }
                }

                Logger.LogError("Failed to create desktop shortcut - PowerShell command failed");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create desktop shortcut: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a desktop shortcut already exists.
        /// </summary>
        public static bool DesktopShortcutExists()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "Holdfast Modding.lnk");
                return File.Exists(shortcutPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes the desktop shortcut.
        /// </summary>
        public static bool RemoveDesktopShortcut()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "Holdfast Modding.lnk");
                
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    Logger.LogInfo("Desktop shortcut removed");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to remove desktop shortcut: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the launcher is installed in a Holdfast directory.
        /// </summary>
        public static string? FindInstalledLocation()
        {
            try
            {
                string currentExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string currentDir = Path.GetDirectoryName(currentExe) ?? "";
                
                // Check if we're in Holdfast/HoldfastModdingLauncher/
                string parentDir = Directory.GetParent(currentDir)?.FullName;
                if (!string.IsNullOrEmpty(parentDir))
                {
                    // Check if parent is Holdfast directory
                    string holdfastExe = Path.Combine(parentDir, "Holdfast NaW.exe");
                    if (File.Exists(holdfastExe))
                    {
                        // We're installed in Holdfast directory
                        return currentDir;
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Uninstalls the launcher from the Holdfast directory.
        /// Removes the HoldfastModdingLauncher folder and all its contents.
        /// </summary>
        public static bool UninstallFromHoldfastDirectory(string holdfastPath)
        {
            try
            {
                if (string.IsNullOrEmpty(holdfastPath) || !Directory.Exists(holdfastPath))
                {
                    Logger.LogError("Invalid Holdfast path for uninstallation");
                    return false;
                }

                string launcherDir = Path.Combine(holdfastPath, "HoldfastModdingLauncher");
                
                if (!Directory.Exists(launcherDir))
                {
                    Logger.LogInfo("Launcher directory not found - nothing to uninstall");
                    return true; // Not an error if it doesn't exist
                }

                // Remove the entire directory
                Directory.Delete(launcherDir, true);
                Logger.LogInfo($"Uninstalled launcher from: {launcherDir}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to uninstall launcher from Holdfast directory: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Performs a complete uninstall:
        /// - Removes launcher from Holdfast directory (if installed there)
        /// - Removes desktop shortcut
        /// - Optionally removes launcher logs and config
        /// </summary>
        public static UninstallResult Uninstall(bool removeLogs = false)
        {
            var result = new UninstallResult();
            
            try
            {
                // Find where we're installed
                string? installedLocation = FindInstalledLocation();
                
                // Remove from Holdfast directory if installed there
                if (!string.IsNullOrEmpty(installedLocation))
                {
                    string? holdfastPath = Directory.GetParent(installedLocation)?.FullName;
                    if (!string.IsNullOrEmpty(holdfastPath))
                    {
                        bool uninstalled = UninstallFromHoldfastDirectory(holdfastPath);
                        result.LauncherRemoved = uninstalled;
                        result.LauncherPath = installedLocation;
                    }
                }
                else
                {
                    result.LauncherRemoved = false;
                    result.Message = "Launcher not found in Holdfast directory";
                }

                // Remove desktop shortcut
                result.ShortcutRemoved = RemoveDesktopShortcut();

                // Remove logs if requested
                if (removeLogs)
                {
                    try
                    {
                        string logDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "HoldfastModdingLauncher",
                            "Logs"
                        );
                        if (Directory.Exists(logDir))
                        {
                            Directory.Delete(logDir, true);
                            result.LogsRemoved = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to remove logs: {ex.Message}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Uninstall failed: {ex}");
                result.Message = $"Uninstall failed: {ex.Message}";
                return result;
            }
        }
    }

    /// <summary>
    /// Result of an uninstall operation.
    /// </summary>
    public class UninstallResult
    {
        public bool LauncherRemoved { get; set; }
        public bool ShortcutRemoved { get; set; }
        public bool LogsRemoved { get; set; }
        public string? LauncherPath { get; set; }
        public string? Message { get; set; }
    }
}

