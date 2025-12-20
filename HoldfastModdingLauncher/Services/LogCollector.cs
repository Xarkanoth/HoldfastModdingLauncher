using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher.Services
{
    public class LogCollector
    {
        /// <summary>
        /// Creates a support bundle containing logs, configs, and system info.
        /// </summary>
        public async Task<string> CreateSupportBundle(string holdfastPath)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string bundleName = $"HoldfastModding_Support_{timestamp}.zip";
                string bundlePath = Path.Combine(Path.GetTempPath(), bundleName);

                using (var zipArchive = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
                {
                    // Collect MelonLoader logs
                    await CollectMelonLoaderLogs(zipArchive, holdfastPath);

                    // Collect mod logs
                    await CollectModLogs(zipArchive, holdfastPath);

                    // Collect launcher logs
                    await CollectLauncherLogs(zipArchive);

                    // Collect config files
                    await CollectConfigFiles(zipArchive, holdfastPath);

                    // Collect system info
                    await CollectSystemInfo(zipArchive, holdfastPath);
                }

                Logger.LogInfo($"Support bundle created: {bundlePath}");
                return bundlePath;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create support bundle: {ex}");
                throw;
            }
        }

        private Task CollectMelonLoaderLogs(ZipArchive zipArchive, string holdfastPath)
        {
            try
            {
                string logsPath = Path.Combine(holdfastPath, "MelonLoader", "Logs");
                if (Directory.Exists(logsPath))
                {
                    var logFiles = Directory.GetFiles(logsPath, "*.txt", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .Take(10); // Get last 10 log files

                    foreach (string logFile in logFiles)
                    {
                        string entryName = $"Logs/MelonLoader/{Path.GetFileName(logFile)}";
                        zipArchive.CreateEntryFromFile(logFile, entryName);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to collect MelonLoader logs: {ex}");
            }
            return Task.CompletedTask;
        }

        private Task CollectModLogs(ZipArchive zipArchive, string holdfastPath)
        {
            try
            {
                // Look for mod-specific log files
                string modsPath = Path.Combine(holdfastPath, "MelonLoader", "Mods");
                if (Directory.Exists(modsPath))
                {
                    var logFiles = Directory.GetFiles(modsPath, "*.log", SearchOption.AllDirectories);
                    foreach (string logFile in logFiles)
                    {
                        string relativePath = Path.GetRelativePath(modsPath, logFile);
                        string entryName = $"Logs/Mods/{relativePath.Replace('\\', '/')}";
                        zipArchive.CreateEntryFromFile(logFile, entryName);
                    }
                }

                // Also check for logs in the mod directory itself
                string currentDir = Directory.GetCurrentDirectory();
                var localLogs = Directory.GetFiles(currentDir, "*.log", SearchOption.TopDirectoryOnly);
                foreach (string logFile in localLogs)
                {
                    string entryName = $"Logs/Local/{Path.GetFileName(logFile)}";
                    zipArchive.CreateEntryFromFile(logFile, entryName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to collect mod logs: {ex}");
            }
            return Task.CompletedTask;
        }

        private Task CollectConfigFiles(ZipArchive zipArchive, string holdfastPath)
        {
            try
            {
                // Collect MelonLoader preferences
                string preferencesPath = Path.Combine(holdfastPath, "MelonLoader", "Preferences.cfg");
                if (File.Exists(preferencesPath))
                {
                    zipArchive.CreateEntryFromFile(preferencesPath, "Config/MelonLoader_Preferences.cfg");
                }

                // Collect mod config files if any
                string modsPath = Path.Combine(holdfastPath, "MelonLoader", "Mods");
                if (Directory.Exists(modsPath))
                {
                    var configFiles = Directory.GetFiles(modsPath, "*.cfg", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(modsPath, "*.json", SearchOption.AllDirectories));

                    foreach (string configFile in configFiles)
                    {
                        string relativePath = Path.GetRelativePath(modsPath, configFile);
                        string entryName = $"Config/Mods/{relativePath.Replace('\\', '/')}";
                        zipArchive.CreateEntryFromFile(configFile, entryName);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to collect config files: {ex}");
            }
            return Task.CompletedTask;
        }

        private Task CollectLauncherLogs(ZipArchive zipArchive)
        {
            try
            {
                string launcherLogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HoldfastModdingLauncher",
                    "Logs"
                );

                if (Directory.Exists(launcherLogDir))
                {
                    var logFiles = Directory.GetFiles(launcherLogDir, "*.log", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .Take(5); // Get last 5 launcher log files

                    foreach (string logFile in logFiles)
                    {
                        string entryName = $"Logs/Launcher/{Path.GetFileName(logFile)}";
                        zipArchive.CreateEntryFromFile(logFile, entryName);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to collect launcher logs: {ex}");
            }
            return Task.CompletedTask;
        }

        private async Task CollectSystemInfo(ZipArchive zipArchive, string holdfastPath)
        {
            try
            {
                var systemInfo = new System.Text.StringBuilder();
                systemInfo.AppendLine("=== System Information ===");
                systemInfo.AppendLine($"OS: {Environment.OSVersion}");
                systemInfo.AppendLine($"OS Version String: {Environment.OSVersion.VersionString}");
                systemInfo.AppendLine($"Framework: {Environment.Version}");
                systemInfo.AppendLine($"Machine Name: {Environment.MachineName}");
                systemInfo.AppendLine($"User Name: {Environment.UserName}");
                systemInfo.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                systemInfo.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
                systemInfo.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
                systemInfo.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // Add .NET version info
                systemInfo.AppendLine($"\n=== .NET Information ===");
                systemInfo.AppendLine($"Runtime Version: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                systemInfo.AppendLine($"OS Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
                systemInfo.AppendLine($"Process Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

                // Add Holdfast installation info
                systemInfo.AppendLine($"\n=== Holdfast Installation ===");
                systemInfo.AppendLine($"Holdfast Path: {holdfastPath}");
                systemInfo.AppendLine($"Holdfast Exists: {Directory.Exists(holdfastPath)}");
                
                if (Directory.Exists(holdfastPath))
                {
                    string exePath = Path.Combine(holdfastPath, "Holdfast NaW.exe");
                    if (File.Exists(exePath))
                    {
                        var fileInfo = new FileInfo(exePath);
                        systemInfo.AppendLine($"Executable Size: {fileInfo.Length / (1024 * 1024)} MB");
                        systemInfo.AppendLine($"Executable Modified: {fileInfo.LastWriteTime}");
                    }

                    string melonLoaderPath = Path.Combine(holdfastPath, "MelonLoader");
                    systemInfo.AppendLine($"MelonLoader Installed: {Directory.Exists(melonLoaderPath)}");
                    
                    if (Directory.Exists(melonLoaderPath))
                    {
                        string preferencesPath = Path.Combine(melonLoaderPath, "Preferences.cfg");
                        systemInfo.AppendLine($"Preferences File Exists: {File.Exists(preferencesPath)}");
                        if (File.Exists(preferencesPath))
                        {
                            var prefsInfo = new FileInfo(preferencesPath);
                            systemInfo.AppendLine($"Preferences Read-Only: {(prefsInfo.Attributes & FileAttributes.ReadOnly) != 0}");
                        }
                    }
                }

                // Create entry in zip
                var entry = zipArchive.CreateEntry("SystemInfo.txt");
                using (var entryStream = entry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    await writer.WriteAsync(systemInfo.ToString());
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to collect system info: {ex}");
            }
        }
    }
}

