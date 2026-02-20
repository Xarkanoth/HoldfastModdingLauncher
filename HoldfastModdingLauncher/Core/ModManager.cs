using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace HoldfastModdingLauncher.Core
{
    public class ModInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
        public string Version { get; set; } = string.Empty;
        public bool HasUpdate { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public bool IsCoreMod { get; set; } = false; // Core mods cannot be disabled
        
        // Extended info from ModVersions.json
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Requirements { get; set; } = string.Empty;
    }
    
    // JSON structure for ModVersions.json
    public class ModVersionsFile
    {
        public Dictionary<string, ModVersionEntry> Mods { get; set; } = new();
        public LauncherVersionEntry? Launcher { get; set; }
    }
    
    public class ModVersionEntry
    {
        public string Version { get; set; } = "1.0.0";
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Requirements { get; set; } = string.Empty;
        public string DllName { get; set; } = string.Empty;
        public string ProjectFolder { get; set; } = string.Empty;
    }
    
    public class LauncherVersionEntry
    {
        public string Version { get; set; } = "1.0.0";
    }

    public class ModManager
    {
        private const string MODS_FOLDER = "Mods";
        private const string MODS_CONFIG_FILE = "mods.json";
        private const string MOD_VERSIONS_FILE = "ModVersions.json";
        private const string INSTALLED_VERSIONS_FILE = "installed-versions.json";
        private Dictionary<string, bool> _modStates = new Dictionary<string, bool>();
        private ModVersionsFile? _modVersions = null;
        private Dictionary<string, string>? _installedVersions = null;

        /// <summary>
        /// Clears the cached mod versions, forcing a reload from disk on next access.
        /// Call this after downloading/updating mods.
        /// </summary>
        public void ClearVersionCache()
        {
            _modVersions = null;
            _installedVersions = null;
            Logger.LogInfo("Mod versions cache cleared");
        }

        /// <summary>
        /// Records the version of a mod that was downloaded/installed from the server.
        /// Persists to disk so the version is remembered across launches.
        /// </summary>
        public void SetInstalledModVersion(string dllFileName, string version)
        {
            LoadInstalledVersions();
            _installedVersions![dllFileName] = version;
            SaveInstalledVersions();
            Logger.LogInfo($"Tracked installed version: {dllFileName} = v{version}");
        }

        public string? GetTrackedInstalledVersion(string dllFileName)
        {
            LoadInstalledVersions();
            return _installedVersions!.TryGetValue(dllFileName, out var v) ? v : null;
        }

        private string GetInstalledVersionsPath()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoldfastModding");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            return Path.Combine(appData, INSTALLED_VERSIONS_FILE);
        }

        private void LoadInstalledVersions()
        {
            if (_installedVersions != null) return;
            try
            {
                string path = GetInstalledVersionsPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _installedVersions = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _installedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                _installedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveInstalledVersions()
        {
            try
            {
                string path = GetInstalledVersionsPath();
                var json = JsonSerializer.Serialize(_installedVersions, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to save installed versions: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the Mods folder path. 
        /// If launcher is in Holdfast directory, uses that. Otherwise uses launcher directory.
        /// </summary>
        public string GetModsFolderPath()
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Check if we're in Holdfast directory structure
            // If launcher is in Holdfast/HoldfastModdingLauncher/, use that
            // Otherwise, use launcher directory
            string parentDir = Directory.GetParent(launcherDir)?.FullName ?? "";
            if (!string.IsNullOrEmpty(parentDir))
            {
                // Check if parent is Holdfast directory (has Holdfast NaW.exe)
                string holdfastExe = Path.Combine(parentDir, "Holdfast NaW.exe");
                if (File.Exists(holdfastExe))
                {
                    // We're in Holdfast directory, use launcher's Mods folder
                    return Path.Combine(launcherDir, MODS_FOLDER);
                }
            }
            
            // Default: use launcher directory
            return Path.Combine(launcherDir, MODS_FOLDER);
        }

        /// <summary>
        /// Gets the path to the mods configuration file.
        /// </summary>
        private string GetConfigFilePath()
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(launcherDir, MODS_CONFIG_FILE);
        }

        /// <summary>
        /// Discovers all mod DLL files in the Mods folder.
        /// </summary>
        public List<ModInfo> DiscoverMods()
        {
            var mods = new List<ModInfo>();
            string modsFolder = GetModsFolderPath();

            try
            {
                if (!Directory.Exists(modsFolder))
                {
                    Directory.CreateDirectory(modsFolder);
                    Logger.LogInfo($"Created Mods folder: {modsFolder}");
                    return mods;
                }

                // Find all DLL files in the Mods folder
                var dllFiles = Directory.GetFiles(modsFolder, "*.dll", SearchOption.TopDirectoryOnly);

                foreach (string dllFile in dllFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(dllFile);
                        var fileName = Path.GetFileName(dllFile);
                        
                        // Get version and display info from ModVersions.json
                        var versionEntry = GetModVersionEntry(fileName);
                        var displayInfo = GetModDisplayInfo(fileName);
                        string resolvedVersion = versionEntry?.Version
                            ?? GetTrackedInstalledVersion(fileName)
                            ?? GetModVersion(dllFile);
                        
                        var modInfo = new ModInfo
                        {
                            FileName = fileName,
                            FullPath = dllFile,
                            LastModified = fileInfo.LastWriteTime,
                            FileSize = fileInfo.Length,
                            Version = resolvedVersion,
                            DisplayName = displayInfo.DisplayName,
                            Description = displayInfo.Description,
                            Requirements = displayInfo.Requirements,
                            IsCoreMod = IsCoreMod(fileName)
                        };

                        // Load saved state (default to enabled if not found)
                        // Core mods are always enabled
                        modInfo.Enabled = GetModState(modInfo.FileName, true);
                        mods.Add(modInfo);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to process mod file {dllFile}: {ex.Message}");
                    }
                }

                Logger.LogInfo($"Discovered {mods.Count} mod(s) in Mods folder");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to discover mods: {ex}");
            }

            return mods.OrderBy(m => m.FileName).ToList();
        }

        /// <summary>
        /// Gets the version of a mod DLL by reading its assembly version.
        /// Uses ReflectionOnlyLoadFrom to avoid locking the file.
        /// Returns only major.minor.patch (3 parts), not the full 4-part version.
        /// </summary>
        private string GetModVersion(string dllPath)
        {
            try
            {
                // Read file bytes to avoid locking the DLL
                byte[] assemblyBytes = File.ReadAllBytes(dllPath);
                var assembly = Assembly.Load(assemblyBytes);
                var version = assembly.GetName().Version;
                if (version != null)
                {
                    // Return only 3 parts: major.minor.patch (not revision)
                    return $"{version.Major}.{version.Minor}.{version.Build}";
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not read version from {dllPath}: {ex.Message}");
                return "Unknown";
            }
        }

        // Core mods that are always enabled (cannot be disabled by user)
        private static readonly HashSet<string> CoreMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LauncherCoreMod.dll"  // Server browser filter, always runs for all users
        };
        
        /// <summary>
        /// Checks if a mod is a core mod (always enabled, cannot be disabled)
        /// </summary>
        public bool IsCoreMod(string fileName) => CoreMods.Contains(fileName);
        
        /// <summary>
        /// Gets the saved state of a mod (enabled/disabled).
        /// Core mods are always enabled.
        /// </summary>
        private bool GetModState(string fileName, bool defaultValue)
        {
            // Core mods are always enabled
            if (IsCoreMod(fileName))
                return true;
            
            LoadModStates();
            return _modStates.ContainsKey(fileName) ? _modStates[fileName] : defaultValue;
        }
        
        /// <summary>
        /// Loads mod version info from ModVersions.json
        /// </summary>
        private void LoadModVersions()
        {
            if (_modVersions != null) return; // Already loaded
            
            try
            {
                string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
                
                // Try multiple possible locations
                string[] possiblePaths = new[]
                {
                    Path.Combine(launcherDir, MOD_VERSIONS_FILE),
                    Path.Combine(launcherDir, "..", MOD_VERSIONS_FILE),
                    Path.Combine(launcherDir, "..", "..", MOD_VERSIONS_FILE)
                };
                
                string? foundPath = null;
                foreach (string path in possiblePaths)
                {
                    string fullPath = Path.GetFullPath(path);
                    Logger.LogInfo($"Checking for ModVersions.json at: {fullPath}");
                    if (File.Exists(fullPath))
                    {
                        foundPath = fullPath;
                        break;
                    }
                }
                
                if (foundPath != null)
                {
                    string json = File.ReadAllText(foundPath);
                    _modVersions = JsonSerializer.Deserialize<ModVersionsFile>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (_modVersions?.Mods != null)
                    {
                        Logger.LogInfo($"Loaded {_modVersions.Mods.Count} mod(s) from {foundPath}");
                        foreach (var kvp in _modVersions.Mods)
                        {
                            Logger.LogInfo($"  - {kvp.Key}: v{kvp.Value.Version}, DisplayName='{kvp.Value.DisplayName}'");
                        }
                    }
                    else
                    {
                        Logger.LogWarning($"ModVersions.json loaded but Mods dictionary is null");
                    }
                }
                else
                {
                    _modVersions = new ModVersionsFile();
                    Logger.LogWarning($"{MOD_VERSIONS_FILE} not found in any expected location");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load mod versions: {ex.Message}");
                _modVersions = new ModVersionsFile();
            }
        }
        
        /// <summary>
        /// Gets version entry for a mod by its DLL filename
        /// </summary>
        private ModVersionEntry? GetModVersionEntry(string dllFileName)
        {
            LoadModVersions();
            if (_modVersions?.Mods == null) return null;
            
            string modName = Path.GetFileNameWithoutExtension(dllFileName);
            
            // Try to find by exact mod name
            if (_modVersions.Mods.TryGetValue(modName, out var entry))
            {
                return entry;
            }
            
            // Try to find by DllName property
            foreach (var kvp in _modVersions.Mods)
            {
                if (kvp.Value.DllName?.Equals(dllFileName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return kvp.Value;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets mod info (display name, description, requirements) from ModVersions.json
        /// </summary>
        public (string DisplayName, string Description, string Requirements) GetModDisplayInfo(string dllFileName)
        {
            var entry = GetModVersionEntry(dllFileName);
            if (entry != null)
            {
                Logger.LogInfo($"Found mod entry for {dllFileName}: DisplayName='{entry.DisplayName}', Desc='{entry.Description?.Substring(0, Math.Min(30, entry.Description?.Length ?? 0))}...'");
                return (
                    string.IsNullOrEmpty(entry.DisplayName) ? Path.GetFileNameWithoutExtension(dllFileName) : entry.DisplayName,
                    entry.Description ?? "",
                    entry.Requirements ?? ""
                );
            }
            Logger.LogWarning($"No mod entry found for {dllFileName} in ModVersions.json");
            return (Path.GetFileNameWithoutExtension(dllFileName), "", "");
        }

        /// <summary>
        /// Sets the enabled state of a mod and saves it.
        /// Core mods cannot be disabled.
        /// </summary>
        public void SetModEnabled(string fileName, bool enabled)
        {
            // Core mods cannot be disabled
            if (IsCoreMod(fileName) && !enabled)
            {
                Logger.LogInfo($"Cannot disable core mod '{fileName}'");
                return;
            }
            
            LoadModStates();
            _modStates[fileName] = enabled;
            SaveModStates();
            Logger.LogInfo($"Mod '{fileName}' {(enabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Gets all enabled mods.
        /// </summary>
        public List<ModInfo> GetEnabledMods()
        {
            var allMods = DiscoverMods();
            return allMods.Where(m => m.Enabled).ToList();
        }

        /// <summary>
        /// Copies enabled mods to the Holdfast MelonLoader Mods folder.
        /// </summary>
        public void CopyEnabledMods(string holdfastPath)
        {
            try
            {
                string targetModsPath = Path.Combine(holdfastPath, "MelonLoader", "Mods");
                if (!Directory.Exists(targetModsPath))
                {
                    Directory.CreateDirectory(targetModsPath);
                }

                var enabledMods = GetEnabledMods();
                
                // First, remove all mods from target (clean slate)
                var existingMods = Directory.GetFiles(targetModsPath, "*.dll", SearchOption.TopDirectoryOnly);
                foreach (string existingMod in existingMods)
                {
                    try
                    {
                        File.Delete(existingMod);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Could not delete existing mod {existingMod}: {ex.Message}");
                    }
                }

                // Copy enabled mods
                foreach (var mod in enabledMods)
                {
                    try
                    {
                        string targetPath = Path.Combine(targetModsPath, mod.FileName);
                        File.Copy(mod.FullPath, targetPath, true);
                        Logger.LogInfo($"Copied enabled mod: {mod.FileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to copy mod {mod.FileName}: {ex}");
                    }
                }

                Logger.LogInfo($"Copied {enabledMods.Count} enabled mod(s) to Holdfast");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to copy enabled mods: {ex}");
            }
        }

        /// <summary>
        /// Loads mod states from the configuration file.
        /// </summary>
        private void LoadModStates()
        {
            try
            {
                string configPath = GetConfigFilePath();
                if (!File.Exists(configPath))
                {
                    _modStates = new Dictionary<string, bool>();
                    return;
                }

                string json = File.ReadAllText(configPath);
                _modStates = JsonSerializer.Deserialize<Dictionary<string, bool>>(json) 
                    ?? new Dictionary<string, bool>();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load mod states: {ex}");
                _modStates = new Dictionary<string, bool>();
            }
        }

        /// <summary>
        /// Saves mod states to the configuration file.
        /// </summary>
        private void SaveModStates()
        {
            try
            {
                string configPath = GetConfigFilePath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_modStates, options);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save mod states: {ex}");
            }
        }
    }
}

