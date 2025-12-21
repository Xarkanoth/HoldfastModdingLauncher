using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher.Services
{
    #region Data Models

    public class ModRegistry
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("registryUrl")]
        public string RegistryUrl { get; set; } = string.Empty;

        [JsonPropertyName("mods")]
        public List<RemoteModInfo> Mods { get; set; } = new();

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();
    }

    public class RemoteModInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("minLauncherVersion")]
        public string MinLauncherVersion { get; set; } = string.Empty;

        [JsonPropertyName("requirements")]
        public string Requirements { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("repositoryUrl")]
        public string RepositoryUrl { get; set; } = string.Empty;

        [JsonPropertyName("releaseUrl")]
        public string ReleaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("dllName")]
        public string DllName { get; set; } = string.Empty;

        [JsonPropertyName("iconUrl")]
        public string IconUrl { get; set; } = string.Empty;

        [JsonPropertyName("screenshots")]
        public List<string> Screenshots { get; set; } = new();

        [JsonPropertyName("changelog")]
        public string Changelog { get; set; } = string.Empty;

        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = new();

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        // Local state (not from JSON)
        [JsonIgnore]
        public bool IsInstalled { get; set; }

        [JsonIgnore]
        public string InstalledVersion { get; set; } = string.Empty;

        [JsonIgnore]
        public bool HasUpdate => IsInstalled && !string.IsNullOrEmpty(InstalledVersion) && IsNewerVersion(InstalledVersion, Version);

        private static bool IsNewerVersion(string current, string latest)
        {
            try
            {
                var currentParts = current.Split('.');
                var latestParts = latest.Split('.');

                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    int currentNum = i < currentParts.Length && int.TryParse(currentParts[i], out int c) ? c : 0;
                    int latestNum = i < latestParts.Length && int.TryParse(latestParts[i], out int l) ? l : 0;

                    if (latestNum > currentNum) return true;
                    if (latestNum < currentNum) return false;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ModDownloadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string DownloadedPath { get; set; } = string.Empty;
        public RemoteModInfo? ModInfo { get; set; }
    }

    #endregion

    public class ModDownloader : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ModManager _modManager;
        private ModRegistry? _cachedRegistry;
        private DateTime _lastRegistryFetch = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        // Default registry URL - uses GitHub API (more reliable than raw.githubusercontent.com)
        // With Accept header "application/vnd.github.v3.raw" it returns raw file content
        private const string DEFAULT_REGISTRY_URL = "https://api.github.com/repos/Xarkanoth/HoldfastModdingLauncher/contents/mod-registry.json";

        public ModDownloader(ModManager modManager)
        {
            _modManager = modManager;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
            // Note: Don't set Accept header here - we need different headers for raw content vs API
        }

        /// <summary>
        /// Gets the configured registry URL from settings or uses default.
        /// </summary>
        public string GetRegistryUrl()
        {
            string customUrl = LauncherSettings.Instance.ModRegistryUrl;
            return string.IsNullOrEmpty(customUrl) ? DEFAULT_REGISTRY_URL : customUrl;
        }

        /// <summary>
        /// Fetches the mod registry from GitHub with retry logic.
        /// </summary>
        public async Task<ModRegistry?> FetchRegistryAsync(bool forceRefresh = false)
        {
            // Use cache if available and not expired
            if (!forceRefresh && _cachedRegistry != null && DateTime.Now - _lastRegistryFetch < _cacheExpiry)
            {
                Logger.LogInfo("Using cached registry");
                return _cachedRegistry;
            }

            string registryUrl = GetRegistryUrl();
            int maxRetries = 3;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Logger.LogInfo($"Fetching mod registry from: {registryUrl} (attempt {attempt}/{maxRetries})");

                    // Create a new HttpClient for each request to avoid issues
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    client.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
                    // Use GitHub API raw content header for faster/more reliable access
                    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.raw");
                    
                    string json = await client.GetStringAsync(registryUrl);
                    Logger.LogInfo($"Received {json.Length} bytes from registry");
                    
                    var registry = JsonSerializer.Deserialize<ModRegistry>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (registry != null && registry.Mods != null)
                    {
                        Logger.LogInfo($"Parsed registry: {registry.Mods.Count} mod(s) found");
                        
                        // Check installed status for each mod
                        await UpdateInstalledStatusAsync(registry);
                        
                        _cachedRegistry = registry;
                        _lastRegistryFetch = DateTime.Now;
                        
                        Logger.LogInfo($"Loaded {registry.Mods.Count} mod(s) from registry successfully");
                        return registry;
                    }
                    else
                    {
                        Logger.LogWarning("Registry parsed but Mods list is null or empty");
                    }
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested == false)
                {
                    // This is a timeout, not a cancellation
                    Logger.LogWarning($"Registry fetch timed out (attempt {attempt}/{maxRetries}): {ex.Message}");
                    if (attempt == maxRetries)
                    {
                        Logger.LogError($"Failed to fetch mod registry after {maxRetries} attempts (timeout)");
                        return null;
                    }
                    await Task.Delay(1000); // Wait before retry
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to fetch mod registry (attempt {attempt}/{maxRetries}): {ex.Message}");
                    if (attempt == maxRetries)
                    {
                        return null;
                    }
                    await Task.Delay(1000); // Wait before retry
                }
            }

            return null;
        }

        /// <summary>
        /// Updates the installed status and version for mods in the registry.
        /// </summary>
        private async Task UpdateInstalledStatusAsync(ModRegistry registry)
        {
            var installedMods = _modManager.DiscoverMods();

            foreach (var remoteMod in registry.Mods)
            {
                var installed = installedMods.FirstOrDefault(m => 
                    m.FileName.Equals(remoteMod.DllName, StringComparison.OrdinalIgnoreCase));

                if (installed != null)
                {
                    remoteMod.IsInstalled = true;
                    remoteMod.InstalledVersion = installed.Version;
                }
                else
                {
                    remoteMod.IsInstalled = false;
                    remoteMod.InstalledVersion = string.Empty;
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the download URL for a mod from GitHub releases.
        /// </summary>
        private async Task<string?> GetDownloadUrlAsync(RemoteModInfo mod)
        {
            // If direct download URL is provided, use it
            if (!string.IsNullOrEmpty(mod.DownloadUrl))
            {
                return mod.DownloadUrl;
            }

            // Otherwise, fetch from GitHub releases API
            if (string.IsNullOrEmpty(mod.ReleaseUrl))
            {
                Logger.LogWarning($"No release URL configured for mod: {mod.Name}");
                return null;
            }

            try
            {
                string response = await _httpClient.GetStringAsync(mod.ReleaseUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                // Look for assets in the release
                if (root.TryGetProperty("assets", out var assetsElement))
                {
                    foreach (var asset in assetsElement.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameElement))
                        {
                            string assetName = nameElement.GetString() ?? string.Empty;
                            
                            // Look for the mod DLL or a zip containing it
                            if (assetName.Equals(mod.DllName, StringComparison.OrdinalIgnoreCase) ||
                                assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                if (asset.TryGetProperty("browser_download_url", out var downloadElement))
                                {
                                    return downloadElement.GetString();
                                }
                            }
                        }
                    }
                }

                Logger.LogWarning($"No downloadable asset found for mod: {mod.Name}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to get download URL for {mod.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the latest version info from GitHub releases.
        /// </summary>
        public async Task<(string Version, string DownloadUrl)?> GetLatestReleaseInfoAsync(RemoteModInfo mod)
        {
            if (string.IsNullOrEmpty(mod.ReleaseUrl))
                return null;

            try
            {
                string response = await _httpClient.GetStringAsync(mod.ReleaseUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string version = string.Empty;
                string downloadUrl = string.Empty;

                // Get version from tag
                if (root.TryGetProperty("tag_name", out var tagElement))
                {
                    version = tagElement.GetString()?.TrimStart('v') ?? string.Empty;
                }

                // Get download URL from assets
                if (root.TryGetProperty("assets", out var assetsElement))
                {
                    foreach (var asset in assetsElement.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameElement))
                        {
                            string assetName = nameElement.GetString() ?? string.Empty;
                            if (assetName.Equals(mod.DllName, StringComparison.OrdinalIgnoreCase) ||
                                assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                if (asset.TryGetProperty("browser_download_url", out var downloadElement))
                                {
                                    downloadUrl = downloadElement.GetString() ?? string.Empty;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(downloadUrl))
                {
                    return (version, downloadUrl);
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to get release info for {mod.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads and installs a mod.
        /// </summary>
        public async Task<ModDownloadResult> DownloadAndInstallModAsync(RemoteModInfo mod, Action<int>? progressCallback = null)
        {
            var result = new ModDownloadResult { ModInfo = mod };

            try
            {
                progressCallback?.Invoke(5);

                // Get the download URL
                string? downloadUrl = await GetDownloadUrlAsync(mod);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    result.Message = "Could not find download URL for this mod.";
                    return result;
                }

                progressCallback?.Invoke(10);
                Logger.LogInfo($"Downloading mod from: {downloadUrl}");

                string modsFolder = _modManager.GetModsFolderPath();
                if (!Directory.Exists(modsFolder))
                {
                    Directory.CreateDirectory(modsFolder);
                }

                string tempPath = Path.GetTempFileName();
                bool isZip = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

                try
                {
                    // Download the file
                    using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        var buffer = new byte[8192];
                        var bytesRead = 0L;

                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var downloadStream = await response.Content.ReadAsStreamAsync())
                        {
                            int read;
                            while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                if (totalBytes > 0)
                                {
                                    int progress = (int)(10 + (bytesRead * 70 / totalBytes));
                                    progressCallback?.Invoke(progress);
                                }
                            }
                        }
                    }

                    progressCallback?.Invoke(80);

                    // Install the mod
                    if (isZip)
                    {
                        // Extract from zip
                        string extractPath = Path.Combine(Path.GetTempPath(), $"ModExtract_{mod.Id}");
                        if (Directory.Exists(extractPath))
                            Directory.Delete(extractPath, true);

                        System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, extractPath);

                        // Find the DLL in the extracted files
                        var dllFile = Directory.GetFiles(extractPath, mod.DllName, SearchOption.AllDirectories).FirstOrDefault();
                        if (dllFile != null)
                        {
                            string targetPath = Path.Combine(modsFolder, mod.DllName);
                            File.Copy(dllFile, targetPath, true);
                            result.DownloadedPath = targetPath;

                            // Also copy any accompanying files (like .json manifest)
                            string jsonFile = Path.ChangeExtension(dllFile, ".json");
                            if (File.Exists(jsonFile))
                            {
                                File.Copy(jsonFile, Path.ChangeExtension(targetPath, ".json"), true);
                            }
                        }
                        else
                        {
                            result.Message = $"Could not find {mod.DllName} in the downloaded archive.";
                            return result;
                        }

                        // Cleanup
                        Directory.Delete(extractPath, true);
                    }
                    else
                    {
                        // Direct DLL download
                        string targetPath = Path.Combine(modsFolder, mod.DllName);
                        File.Copy(tempPath, targetPath, true);
                        result.DownloadedPath = targetPath;
                    }

                    progressCallback?.Invoke(100);

                    result.Success = true;
                    result.Message = $"Successfully installed {mod.Name} v{mod.Version}";
                    Logger.LogInfo($"Mod installed: {mod.Name} v{mod.Version}");
                }
                finally
                {
                    // Cleanup temp file
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Failed to install mod: {ex.Message}";
                Logger.LogError($"Failed to install mod {mod.Name}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Uninstalls a mod by removing it from the Mods folder.
        /// </summary>
        public ModDownloadResult UninstallMod(RemoteModInfo mod)
        {
            var result = new ModDownloadResult { ModInfo = mod };

            try
            {
                string modsFolder = _modManager.GetModsFolderPath();
                string dllPath = Path.Combine(modsFolder, mod.DllName);
                string jsonPath = Path.ChangeExtension(dllPath, ".json");

                if (File.Exists(dllPath))
                {
                    File.Delete(dllPath);
                    Logger.LogInfo($"Deleted mod file: {dllPath}");
                }

                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    Logger.LogInfo($"Deleted manifest file: {jsonPath}");
                }

                result.Success = true;
                result.Message = $"Successfully uninstalled {mod.Name}";
            }
            catch (Exception ex)
            {
                result.Message = $"Failed to uninstall mod: {ex.Message}";
                Logger.LogError($"Failed to uninstall mod {mod.Name}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Checks all installed mods for updates.
        /// </summary>
        public async Task<List<RemoteModInfo>> CheckForUpdatesAsync()
        {
            var modsWithUpdates = new List<RemoteModInfo>();

            var registry = await FetchRegistryAsync(true);
            if (registry == null)
                return modsWithUpdates;

            foreach (var mod in registry.Mods)
            {
                if (mod.IsInstalled && mod.HasUpdate)
                {
                    modsWithUpdates.Add(mod);
                }
            }

            return modsWithUpdates;
        }

        /// <summary>
        /// Clears the registry cache to force a fresh fetch.
        /// </summary>
        public void ClearCache()
        {
            _cachedRegistry = null;
            _lastRegistryFetch = DateTime.MinValue;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

