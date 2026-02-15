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

        [JsonPropertyName("releasesApiUrl")]
        public string ReleasesApiUrl { get; set; } = string.Empty;

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

    public class DownloadProgressInfo
    {
        public int PercentComplete { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public string Status { get; set; } = string.Empty;

        public string FormattedProgress
        {
            get
            {
                if (TotalBytes <= 0)
                    return Status;

                string downloaded = FormatBytes(BytesDownloaded);
                string total = FormatBytes(TotalBytes);
                string speed = FormatBytes((long)BytesPerSecond) + "/s";
                string eta = EstimatedTimeRemaining.TotalSeconds > 0 
                    ? $"~{EstimatedTimeRemaining:mm\\:ss}" 
                    : "";

                return $"{downloaded} / {total} ({speed}) {eta}";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
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
                        
                        // Resolve latest versions dynamically from GitHub Releases API
                        await ResolveLatestVersionsAsync(registry);
                        
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
        /// Resolves the latest version and download URL for each mod by querying the GitHub Releases API.
        /// This eliminates the need to hardcode version numbers and download URLs in mod-registry.json.
        /// </summary>
        private async Task ResolveLatestVersionsAsync(ModRegistry registry)
        {
            string releasesApiUrl = registry.ReleasesApiUrl;

            // If no API URL in registry, try to derive from the first mod's repository URL
            if (string.IsNullOrEmpty(releasesApiUrl))
            {
                var firstMod = registry.Mods.FirstOrDefault(m => !string.IsNullOrEmpty(m.RepositoryUrl));
                if (firstMod != null)
                {
                    releasesApiUrl = firstMod.RepositoryUrl
                        .Replace("https://github.com/", "https://api.github.com/repos/") + "/releases";
                    Logger.LogInfo($"Derived releases API URL from repository: {releasesApiUrl}");
                }
                else
                {
                    Logger.LogWarning("No releases API URL configured and no repository URL found on any mod");
                    return;
                }
            }

            try
            {
                Logger.LogInfo($"Resolving mod versions from GitHub Releases API: {releasesApiUrl}");

                // Track which mods still need resolution
                var unresolvedMods = new HashSet<string>(
                    registry.Mods.Select(m => m.Id),
                    StringComparer.OrdinalIgnoreCase);

                int page = 1;
                int maxPages = 3; // Safety limit: 3 pages × 100 = 300 releases max

                while (unresolvedMods.Count > 0 && page <= maxPages)
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                    client.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
                    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                    string url = $"{releasesApiUrl}?per_page=100&page={page}";
                    string json = await client.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(json);

                    var releases = doc.RootElement;
                    if (releases.GetArrayLength() == 0)
                        break; // No more releases

                    foreach (var release in releases.EnumerateArray())
                    {
                        // Skip drafts and prereleases
                        if (release.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean())
                            continue;
                        if (release.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean())
                            continue;

                        if (!release.TryGetProperty("tag_name", out var tagElement))
                            continue;

                        string tag = tagElement.GetString() ?? string.Empty;

                        // Parse tag format: "ModId-vVersion" (e.g., "AdvancedAdminUI-v1.0.57")
                        int vIndex = tag.LastIndexOf("-v");
                        if (vIndex < 0) continue;

                        string modId = tag.Substring(0, vIndex);
                        string version = tag.Substring(vIndex + 2); // Skip "-v"

                        // Only process if this mod is in our registry and not yet resolved
                        if (!unresolvedMods.Contains(modId))
                            continue;

                        var mod = registry.Mods.FirstOrDefault(m =>
                            m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));
                        if (mod == null) continue;

                        // Get HTML release URL
                        string releaseHtmlUrl = string.Empty;
                        if (release.TryGetProperty("html_url", out var htmlUrlEl))
                        {
                            releaseHtmlUrl = htmlUrlEl.GetString() ?? string.Empty;
                        }

                        // Find download URL from release assets
                        string downloadUrl = string.Empty;
                        if (release.TryGetProperty("assets", out var assetsElement))
                        {
                            foreach (var asset in assetsElement.EnumerateArray())
                            {
                                if (!asset.TryGetProperty("name", out var nameElement))
                                    continue;

                                string assetName = nameElement.GetString() ?? string.Empty;
                                if (assetName.Equals(mod.DllName, StringComparison.OrdinalIgnoreCase) ||
                                    assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (asset.TryGetProperty("browser_download_url", out var dlElement))
                                    {
                                        downloadUrl = dlElement.GetString() ?? string.Empty;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(downloadUrl))
                        {
                            mod.Version = version;
                            mod.DownloadUrl = downloadUrl;
                            mod.ReleaseUrl = releaseHtmlUrl;
                            unresolvedMods.Remove(modId);
                            Logger.LogInfo($"Resolved {mod.Id}: v{version}");
                        }
                    }

                    page++;
                }

                if (unresolvedMods.Count > 0)
                {
                    foreach (var modId in unresolvedMods)
                    {
                        Logger.LogWarning($"No GitHub release found for mod: {modId}");
                    }
                }

                Logger.LogInfo($"Version resolution complete: {registry.Mods.Count - unresolvedMods.Count}/{registry.Mods.Count} mods resolved");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to resolve mod versions from GitHub Releases API: {ex.Message}");
                // Non-fatal: mods will show without version info but the browser will still work
            }
        }

        /// <summary>
        /// Gets the download URL for a mod. Uses the pre-resolved URL from batch resolution,
        /// with an individual fallback query to the GitHub Releases API if needed.
        /// </summary>
        private async Task<string?> GetDownloadUrlAsync(RemoteModInfo mod)
        {
            // If already resolved (from batch resolution), use it directly
            if (!string.IsNullOrEmpty(mod.DownloadUrl))
            {
                return mod.DownloadUrl;
            }

            // Fallback: try to resolve individually from GitHub Releases API
            Logger.LogInfo($"Download URL not pre-resolved for {mod.Name}, attempting individual lookup...");

            string repoUrl = mod.RepositoryUrl;
            if (string.IsNullOrEmpty(repoUrl))
            {
                Logger.LogWarning($"No repository URL configured for mod: {mod.Name}");
                return null;
            }

            try
            {
                // Convert GitHub URL to API URL: https://github.com/owner/repo -> https://api.github.com/repos/owner/repo/releases
                string apiUrl = repoUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases";
                string tagPrefix = $"{mod.Id}-v";

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                string json = await client.GetStringAsync($"{apiUrl}?per_page=50");
                using var doc = JsonDocument.Parse(json);

                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    // Skip drafts and prereleases
                    if (release.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean())
                        continue;
                    if (release.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean())
                        continue;

                    if (!release.TryGetProperty("tag_name", out var tagElement))
                        continue;

                    string tag = tagElement.GetString() ?? string.Empty;
                    if (!tag.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Found the latest release for this mod
                    string version = tag.Substring(tagPrefix.Length);
                    mod.Version = version;

                    // Find the DLL or ZIP asset
                    if (release.TryGetProperty("assets", out var assetsElement))
                    {
                        foreach (var asset in assetsElement.EnumerateArray())
                        {
                            if (!asset.TryGetProperty("name", out var nameElement))
                                continue;

                            string assetName = nameElement.GetString() ?? string.Empty;
                            if (assetName.Equals(mod.DllName, StringComparison.OrdinalIgnoreCase) ||
                                assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                if (asset.TryGetProperty("browser_download_url", out var dlElement))
                                {
                                    string downloadUrl = dlElement.GetString() ?? string.Empty;
                                    mod.DownloadUrl = downloadUrl;
                                    Logger.LogInfo($"Individually resolved {mod.Name}: v{version}");
                                    return downloadUrl;
                                }
                            }
                        }
                    }

                    // Found the release but no matching asset
                    Logger.LogWarning($"Release found for {mod.Name} (v{version}) but no matching asset ({mod.DllName})");
                    return null;
                }

                Logger.LogWarning($"No GitHub release found matching tag prefix '{tagPrefix}' for mod: {mod.Name}");
                return null;
            }
            catch (HttpRequestException httpEx)
            {
                string friendlyError = GetUserFriendlyHttpError(httpEx);
                Logger.LogError($"Failed to resolve download URL for {mod.Name}: {friendlyError}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to resolve download URL for {mod.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the latest version info for a mod. Returns pre-resolved data if available,
        /// otherwise attempts individual resolution from GitHub Releases API.
        /// </summary>
        public async Task<(string Version, string DownloadUrl)?> GetLatestReleaseInfoAsync(RemoteModInfo mod)
        {
            // If already resolved, return immediately
            if (!string.IsNullOrEmpty(mod.Version) && !string.IsNullOrEmpty(mod.DownloadUrl))
            {
                return (mod.Version, mod.DownloadUrl);
            }

            // Try to resolve individually
            string? downloadUrl = await GetDownloadUrlAsync(mod);
            if (!string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(mod.Version))
            {
                return (mod.Version, downloadUrl);
            }

            return null;
        }

        /// <summary>
        /// Downloads and installs a mod with detailed progress reporting.
        /// </summary>
        public async Task<ModDownloadResult> DownloadAndInstallModAsync(RemoteModInfo mod, Action<DownloadProgressInfo>? detailedProgressCallback = null, Action<int>? progressCallback = null)
        {
            var result = new ModDownloadResult { ModInfo = mod };

            void ReportProgress(int percent, long downloaded = 0, long total = 0, double speed = 0, TimeSpan? eta = null, string status = "")
            {
                progressCallback?.Invoke(percent);
                detailedProgressCallback?.Invoke(new DownloadProgressInfo
                {
                    PercentComplete = percent,
                    BytesDownloaded = downloaded,
                    TotalBytes = total,
                    BytesPerSecond = speed,
                    EstimatedTimeRemaining = eta ?? TimeSpan.Zero,
                    Status = status
                });
            }

            try
            {
                ReportProgress(5, status: "Preparing download...");

                // Get the download URL
                string? downloadUrl = await GetDownloadUrlAsync(mod);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    result.Message = "Could not find download URL for this mod.";
                    return result;
                }

                ReportProgress(10, status: "Connecting...");
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
                    // Download the file with detailed progress
                    using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        var buffer = new byte[8192];
                        var bytesRead = 0L;
                        var startTime = DateTime.Now;
                        var lastReportTime = DateTime.Now;

                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var downloadStream = await response.Content.ReadAsStreamAsync())
                        {
                            int read;
                            while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                bytesRead += read;

                                // Report progress every 100ms or so
                                if ((DateTime.Now - lastReportTime).TotalMilliseconds > 100 || bytesRead == totalBytes)
                                {
                                    lastReportTime = DateTime.Now;
                                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                                    var speed = elapsed > 0 ? bytesRead / elapsed : 0;
                                    var remainingBytes = totalBytes - bytesRead;
                                    var eta = speed > 0 ? TimeSpan.FromSeconds(remainingBytes / speed) : TimeSpan.Zero;

                                    if (totalBytes > 0)
                                    {
                                        int progress = (int)(10 + (bytesRead * 70 / totalBytes));
                                        ReportProgress(progress, bytesRead, totalBytes, speed, eta, "Downloading...");
                                    }
                                }
                            }
                        }
                    }

                    ReportProgress(80, status: "Installing...");

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

                    ReportProgress(100, status: "Complete!");

                    result.Success = true;
                    result.Message = $"Successfully installed {mod.Name} v{mod.Version}";
                    Logger.LogInfo($"Mod installed: {mod.Name} v{mod.Version}");

                    // Clear caches so the new version is picked up
                    ClearCache();
                    _modManager.ClearVersionCache();
                    
                    // Update ModVersions.json with the new version
                    UpdateModVersionsFile(mod);
                }
                finally
                {
                    // Cleanup temp file
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
            catch (HttpRequestException httpEx)
            {
                // Provide user-friendly messages for common HTTP errors
                string userMessage = GetUserFriendlyHttpError(httpEx);
                result.Message = userMessage;
                Logger.LogError($"Failed to install mod {mod.Name}: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                result.Message = $"Failed to install mod: {ex.Message}";
                Logger.LogError($"Failed to install mod {mod.Name}: {ex.Message}");
            }

            return result;
        }
        
        /// <summary>
        /// Converts HTTP exceptions to user-friendly error messages.
        /// </summary>
        private static string GetUserFriendlyHttpError(HttpRequestException ex)
        {
            string message = ex.Message.ToLower();
            
            // Check for common status codes in the exception message
            if (message.Contains("503") || message.Contains("service unavailable"))
            {
                return "GitHub is temporarily unavailable (503 error). This is a temporary GitHub outage - please try again in a few minutes.";
            }
            if (message.Contains("502") || message.Contains("bad gateway"))
            {
                return "GitHub server error (502). This is a temporary issue - please try again in a few minutes.";
            }
            if (message.Contains("504") || message.Contains("gateway timeout"))
            {
                return "GitHub request timed out (504). This is a temporary issue - please try again in a few minutes.";
            }
            if (message.Contains("429") || message.Contains("too many requests"))
            {
                return "Too many requests to GitHub (rate limited). Please wait a minute and try again.";
            }
            if (message.Contains("404") || message.Contains("not found"))
            {
                return "Mod not found on GitHub (404). The release may not exist or the URL may be incorrect.";
            }
            if (message.Contains("403") || message.Contains("forbidden"))
            {
                return "Access denied by GitHub (403). You may be rate limited - please try again later.";
            }
            if (message.Contains("timeout") || message.Contains("timed out"))
            {
                return "Connection to GitHub timed out. Please check your internet connection and try again.";
            }
            if (message.Contains("network") || message.Contains("connection"))
            {
                return "Network error connecting to GitHub. Please check your internet connection and try again.";
            }
            
            // Default message with original error
            return $"Download failed: {ex.Message}";
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

        /// <summary>
        /// Updates the ModVersions.json file with the new mod version after installation.
        /// </summary>
        private void UpdateModVersionsFile(RemoteModInfo mod)
        {
            try
            {
                string modsFolder = _modManager.GetModsFolderPath();
                string modVersionsPath = Path.Combine(modsFolder, "ModVersions.json");
                
                // Also check in launcher directory
                string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
                string launcherModVersionsPath = Path.Combine(launcherDir, "ModVersions.json");
                
                // Use whichever path exists, prefer launcher directory
                string targetPath = File.Exists(launcherModVersionsPath) ? launcherModVersionsPath : modVersionsPath;
                if (!File.Exists(targetPath))
                {
                    targetPath = launcherModVersionsPath; // Default to launcher directory
                }

                // Read existing file or create new
                ModVersionsFile versionsFile;
                if (File.Exists(targetPath))
                {
                    string json = File.ReadAllText(targetPath);
                    versionsFile = JsonSerializer.Deserialize<ModVersionsFile>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    }) ?? new ModVersionsFile();
                }
                else
                {
                    versionsFile = new ModVersionsFile();
                }

                if (versionsFile.Mods == null)
                {
                    versionsFile.Mods = new Dictionary<string, ModVersionEntry>();
                }

                // Update the mod entry
                string modKey = Path.GetFileNameWithoutExtension(mod.DllName);
                versionsFile.Mods[modKey] = new ModVersionEntry
                {
                    Version = mod.Version,
                    DllName = mod.DllName,
                    DisplayName = mod.Name,
                    Description = mod.Description,
                    Requirements = mod.Requirements
                };

                // Save back to file
                string updatedJson = JsonSerializer.Serialize(versionsFile, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(targetPath, updatedJson);
                Logger.LogInfo($"Updated ModVersions.json: {modKey} -> v{mod.Version}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to update ModVersions.json: {ex.Message}");
                // Non-critical, continue anyway
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

