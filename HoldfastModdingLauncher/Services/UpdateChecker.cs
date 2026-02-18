using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher.Services
{
    public class UpdateInfo
    {
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public bool UpdateAvailable { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    public class UpdateChecker : IDisposable
    {
        private readonly HttpClient _httpClient;
        private ApiClient _apiClient;
        private const string LATEST_VERSION_URL = "https://raw.githubusercontent.com/Xarkanoth/HoldfastModdingLauncher/main/latest-version.json";
        private const string GITHUB_API_URL = "https://api.github.com/repos/Xarkanoth/HoldfastModdingLauncher/releases/latest";
        private const string GITHUB_RELEASES_URL = "https://github.com/Xarkanoth/HoldfastModdingLauncher/releases";

        public UpdateChecker()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        }

        public void SetApiClient(ApiClient apiClient) => _apiClient = apiClient;

        /// <summary>
        /// Gets the current version of the launcher from the assembly.
        /// </summary>
        public string GetCurrentVersion()
        {
            try
            {
                // Try to read from version.txt first (more reliable during development)
                string versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                if (File.Exists(versionFile))
                {
                    return File.ReadAllText(versionFile).Trim();
                }

                // Fall back to assembly version
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                if (version != null)
                {
                    return $"{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not determine current version: {ex.Message}");
            }

            return "Unknown";
        }

        /// <summary>
        /// Checks for the latest launcher version using the raw manifest (no API rate limits).
        /// Falls back to GitHub API if the manifest is unavailable.
        /// </summary>
        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            var updateInfo = new UpdateInfo
            {
                CurrentVersion = GetCurrentVersion()
            };

            // Try self-hosted API first
            if (_apiClient != null && _apiClient.IsConfigured)
            {
                try
                {
                    var launcherVersion = await _apiClient.GetLauncherVersionAsync();
                    if (launcherVersion != null && !string.IsNullOrEmpty(launcherVersion.Version))
                    {
                        updateInfo.LatestVersion = launcherVersion.Version.TrimStart('v');
                        updateInfo.DownloadUrl = launcherVersion.DownloadUrl.StartsWith("/")
                            ? $"{_apiClient.BaseUrl}{launcherVersion.DownloadUrl}"
                            : launcherVersion.DownloadUrl;
                        updateInfo.ReleaseNotes = launcherVersion.ReleaseNotes ?? string.Empty;
                        updateInfo.UpdateAvailable = IsNewerVersion(updateInfo.CurrentVersion, updateInfo.LatestVersion);

                        LauncherSettings.Instance.LastUpdateCheck = DateTime.Now;
                        LauncherSettings.Instance.Save();

                        Logger.LogInfo($"Update check complete (self-hosted API). Current: {updateInfo.CurrentVersion}, Latest: {updateInfo.LatestVersion}");
                        return updateInfo;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Self-hosted API update check failed: {ex.Message}, falling back...");
                }
            }

            try
            {
                string response = await _httpClient.GetStringAsync(LATEST_VERSION_URL);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("version", out var versionEl))
                    updateInfo.LatestVersion = versionEl.GetString()?.TrimStart('v') ?? string.Empty;

                if (root.TryGetProperty("downloadUrl", out var dlEl))
                    updateInfo.DownloadUrl = dlEl.GetString() ?? string.Empty;

                if (root.TryGetProperty("releaseUrl", out var relEl))
                    updateInfo.ReleaseUrl = relEl.GetString() ?? GITHUB_RELEASES_URL;

                if (root.TryGetProperty("releaseNotes", out var notesEl))
                    updateInfo.ReleaseNotes = notesEl.GetString() ?? string.Empty;

                updateInfo.UpdateAvailable = IsNewerVersion(updateInfo.CurrentVersion, updateInfo.LatestVersion);

                LauncherSettings.Instance.LastUpdateCheck = DateTime.Now;
                LauncherSettings.Instance.Save();

                Logger.LogInfo($"Update check complete (manifest). Current: {updateInfo.CurrentVersion}, Latest: {updateInfo.LatestVersion}, Update available: {updateInfo.UpdateAvailable}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Manifest update check failed: {ex.Message}, falling back to API...");
                try
                {
                    await CheckForUpdateViaApiAsync(updateInfo);
                }
                catch (Exception apiEx)
                {
                    Logger.LogWarning($"API update check also failed: {apiEx.Message}");
                    updateInfo.LatestVersion = updateInfo.CurrentVersion;
                    updateInfo.UpdateAvailable = false;
                }
            }

            return updateInfo;
        }

        /// <summary>
        /// Fallback: checks GitHub releases API directly.
        /// </summary>
        private async Task CheckForUpdateViaApiAsync(UpdateInfo updateInfo)
        {
            string allReleasesUrl = "https://api.github.com/repos/Xarkanoth/HoldfastModdingLauncher/releases";
            string response = await _httpClient.GetStringAsync(allReleasesUrl);
            using var doc = JsonDocument.Parse(response);
            
            JsonElement? latestLauncherRelease = null;
            
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("tag_name", out var tagElement))
                {
                    string tagName = tagElement.GetString() ?? string.Empty;
                    if (tagName.StartsWith("v") && !tagName.StartsWith("v0") && 
                        char.IsDigit(tagName.Length > 1 ? tagName[1] : '0') &&
                        !tagName.Contains("mod", StringComparison.OrdinalIgnoreCase))
                    {
                        latestLauncherRelease = release;
                        break;
                    }
                }
            }
            
            if (latestLauncherRelease.HasValue)
            {
                var root = latestLauncherRelease.Value;
                
                if (root.TryGetProperty("tag_name", out var tagElement))
                    updateInfo.LatestVersion = (tagElement.GetString() ?? string.Empty).TrimStart('v');

                if (root.TryGetProperty("html_url", out var urlElement))
                    updateInfo.ReleaseUrl = urlElement.GetString() ?? GITHUB_RELEASES_URL;

                if (root.TryGetProperty("body", out var bodyElement))
                    updateInfo.ReleaseNotes = bodyElement.GetString() ?? string.Empty;

                if (root.TryGetProperty("published_at", out var dateElement))
                {
                    if (DateTime.TryParse(dateElement.GetString(), out var publishedAt))
                        updateInfo.PublishedAt = publishedAt;
                }

                if (root.TryGetProperty("assets", out var assetsElement))
                {
                    foreach (var asset in assetsElement.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameElement))
                        {
                            string assetName = nameElement.GetString() ?? string.Empty;
                            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                if (asset.TryGetProperty("browser_download_url", out var downloadElement))
                                {
                                    updateInfo.DownloadUrl = downloadElement.GetString() ?? string.Empty;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            updateInfo.UpdateAvailable = IsNewerVersion(updateInfo.CurrentVersion, updateInfo.LatestVersion);

            LauncherSettings.Instance.LastUpdateCheck = DateTime.Now;
            LauncherSettings.Instance.Save();

            Logger.LogInfo($"Update check complete (API). Current: {updateInfo.CurrentVersion}, Latest: {updateInfo.LatestVersion}, Update available: {updateInfo.UpdateAvailable}");
        }

        /// <summary>
        /// Compares two version strings to determine if the latest is newer.
        /// </summary>
        private bool IsNewerVersion(string current, string latest)
        {
            try
            {
                if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest))
                    return false;

                // Clean version strings
                current = current.TrimStart('v').Trim();
                latest = latest.TrimStart('v').Trim();

                // Parse versions
                var currentParts = current.Split('.');
                var latestParts = latest.Split('.');

                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    int currentNum = i < currentParts.Length && int.TryParse(currentParts[i], out int c) ? c : 0;
                    int latestNum = i < latestParts.Length && int.TryParse(latestParts[i], out int l) ? l : 0;

                    if (latestNum > currentNum)
                        return true;
                    if (latestNum < currentNum)
                        return false;
                }

                return false; // Versions are equal
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Downloads and installs the update.
        /// </summary>
        public async Task<bool> DownloadAndInstallUpdateAsync(UpdateInfo updateInfo, Action<int> progressCallback)
        {
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                Logger.LogError("No download URL available for update");
                return false;
            }

            string tempPath = Path.Combine(Path.GetTempPath(), $"HoldfastModdingLauncher_v{updateInfo.LatestVersion}.zip");
            string extractPath = Path.Combine(Path.GetTempPath(), $"HoldfastModdingLauncher_Update_{updateInfo.LatestVersion}");

            try
            {
                Logger.LogInfo($"Downloading update from: {updateInfo.DownloadUrl}");
                progressCallback(10);

                // Download the ZIP file
                using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var downloadStream = await response.Content.ReadAsStreamAsync())
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        var buffer = new byte[8192];
                        var bytesRead = 0L;
                        int read;

                        while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            bytesRead += read;

                            if (totalBytes > 0)
                            {
                                int progress = (int)(10 + (bytesRead * 50 / totalBytes));
                                progressCallback(progress);
                            }
                        }
                    }
                }

                progressCallback(60);
                Logger.LogInfo("Download complete, extracting...");

                // Extract the ZIP
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                
                System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, extractPath);
                progressCallback(70);
                
                // Check if contents are inside a subfolder (e.g., HoldfastModdingLauncher/)
                string actualSourcePath = extractPath;
                string[] subDirs = Directory.GetDirectories(extractPath);
                if (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0)
                {
                    // All contents are in a single subfolder, use that as the source
                    actualSourcePath = subDirs[0];
                    Logger.LogInfo($"Found nested folder structure, using: {Path.GetFileName(actualSourcePath)}");
                }
                
                progressCallback(80);

                // Create update script that will replace files after launcher closes
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string updateScript = CreateUpdateScript(actualSourcePath, currentDir, updateInfo.LatestVersion);
                
                progressCallback(90);

                // Start the update script and close the launcher
                Logger.LogInfo("Starting update script...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{updateScript}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);

                progressCallback(100);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to download/install update: {ex.Message}");
                
                // Cleanup
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true); } catch { }
                
                return false;
            }
        }

        /// <summary>
        /// Creates a batch script to update the launcher files after it closes.
        /// </summary>
        private string CreateUpdateScript(string sourcePath, string targetPath, string version)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), "update_launcher.bat");
            string exePath = Path.Combine(targetPath, "HoldfastModdingLauncher.exe");

            string script = $@"@echo off
echo Updating Holdfast Modding Launcher to v{version}...
echo Please wait...

REM Wait for launcher to close
timeout /t 2 /nobreak >nul

REM Copy new files
xcopy /s /y /q ""{sourcePath}\*"" ""{targetPath}""

REM Cleanup
rmdir /s /q ""{sourcePath}"" 2>nul

echo Update complete!
echo Restarting launcher...

REM Restart the launcher
start """" ""{exePath}""

REM Delete this script
del ""%~f0""
";

            File.WriteAllText(scriptPath, script);
            return scriptPath;
        }

        /// <summary>
        /// Opens the releases page in the default browser.
        /// </summary>
        public void OpenReleasesPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GITHUB_RELEASES_URL,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to open releases page: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

