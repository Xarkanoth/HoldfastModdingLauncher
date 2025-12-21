using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher.Services
{
    public class ModVersionInfo
    {
        public string ModName { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string UpdateUrl { get; set; } = string.Empty;
        public bool HasUpdate { get; set; }
    }

    public class ModManifest
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string UpdateUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class ModVersionChecker
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, ModVersionInfo> _versionCache = new Dictionary<string, ModVersionInfo>();

        public ModVersionChecker()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        /// <summary>
        /// Gets the current version of a mod DLL by reading its assembly version.
        /// Uses byte array loading to avoid locking the file.
        /// </summary>
        public string GetModVersion(string dllPath)
        {
            try
            {
                // Read file bytes to avoid locking the DLL
                byte[] assemblyBytes = File.ReadAllBytes(dllPath);
                var assembly = Assembly.Load(assemblyBytes);
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not read version from {dllPath}: {ex.Message}");
                return "Unknown";
            }
        }

        /// <summary>
        /// Reads mod manifest from a JSON file (modname.json) if it exists.
        /// </summary>
        public ModManifest? ReadModManifest(string dllPath)
        {
            try
            {
                string manifestPath = Path.ChangeExtension(dllPath, ".json");
                if (!File.Exists(manifestPath))
                {
                    Logger.LogInfo($"No manifest file found at: {manifestPath}");
                    return null;
                }

                string json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (manifest != null)
                {
                    Logger.LogInfo($"Loaded manifest for {Path.GetFileName(dllPath)}: Name='{manifest.Name}', Desc='{(manifest.Description?.Length > 30 ? manifest.Description.Substring(0, 30) + "..." : manifest.Description)}'");
                }
                
                return manifest;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not read manifest for {dllPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks for updates for a mod by fetching the latest version from the update URL.
        /// </summary>
        public async Task<ModVersionInfo> CheckForUpdateAsync(string modFileName, string dllPath, string? updateUrl = null)
        {
            var versionInfo = new ModVersionInfo
            {
                ModName = modFileName,
                CurrentVersion = GetModVersion(dllPath)
            };

            try
            {
                // Try to read manifest first
                var manifest = ReadModManifest(dllPath);
                if (manifest != null && !string.IsNullOrEmpty(manifest.UpdateUrl))
                {
                    updateUrl = manifest.UpdateUrl;
                    versionInfo.CurrentVersion = manifest.Version ?? versionInfo.CurrentVersion;
                }

                // If no update URL provided, skip check
                if (string.IsNullOrEmpty(updateUrl))
                {
                    versionInfo.LatestVersion = versionInfo.CurrentVersion;
                    versionInfo.HasUpdate = false;
                    return versionInfo;
                }

                // Fetch latest version from update URL
                // Expected format: JSON with "version" field, or plain text version number
                string response = await _httpClient.GetStringAsync(updateUrl);
                
                // Try to parse as JSON first
                try
                {
                    var jsonDoc = JsonDocument.Parse(response);
                    if (jsonDoc.RootElement.TryGetProperty("version", out var versionElement))
                    {
                        versionInfo.LatestVersion = versionElement.GetString() ?? versionInfo.CurrentVersion;
                    }
                    else if (jsonDoc.RootElement.TryGetProperty("Version", out var versionElement2))
                    {
                        versionInfo.LatestVersion = versionElement2.GetString() ?? versionInfo.CurrentVersion;
                    }
                    else
                    {
                        versionInfo.LatestVersion = response.Trim();
                    }
                }
                catch
                {
                    // Not JSON, treat as plain text version
                    versionInfo.LatestVersion = response.Trim();
                }

                // Compare versions (simple string comparison - can be improved with SemVer parsing)
                versionInfo.HasUpdate = !string.Equals(versionInfo.CurrentVersion, versionInfo.LatestVersion, StringComparison.OrdinalIgnoreCase);
                versionInfo.UpdateUrl = updateUrl;

                // Cache the result
                _versionCache[modFileName] = versionInfo;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to check for updates for {modFileName}: {ex.Message}");
                versionInfo.LatestVersion = versionInfo.CurrentVersion;
                versionInfo.HasUpdate = false;
            }

            return versionInfo;
        }

        /// <summary>
        /// Checks for updates for all mods in the Mods folder.
        /// </summary>
        public async Task<List<ModVersionInfo>> CheckAllModsForUpdatesAsync(List<Core.ModInfo> mods)
        {
            var updateTasks = mods.Select(mod => CheckForUpdateAsync(mod.FileName, mod.FullPath));
            var results = await Task.WhenAll(updateTasks);
            return results.ToList();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

