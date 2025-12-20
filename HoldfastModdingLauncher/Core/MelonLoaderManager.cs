using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Compression;

namespace HoldfastModdingLauncher.Core
{
    public class MelonLoaderManager
    {
        private readonly HttpClient _httpClient;
        private const string MELONLOADER_VERSION = "0.6.1"; // Update as needed
        private const string MELONLOADER_DOWNLOAD_URL = $"https://github.com/LavaGang/MelonLoader/releases/download/v{MELONLOADER_VERSION}/MelonLoader.x64.zip";

        public MelonLoaderManager()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Ensures MelonLoader is installed in the Holdfast directory.
        /// Downloads and installs if missing.
        /// </summary>
        public async Task<bool> EnsureInstalled(string holdfastPath)
        {
            try
            {
                string melonLoaderPath = Path.Combine(holdfastPath, "MelonLoader");
                string melonLoaderDll = Path.Combine(melonLoaderPath, "net6", "MelonLoader.dll");

                // Check if already installed
                if (File.Exists(melonLoaderDll))
                {
                    Logger.LogInfo("MelonLoader already installed.");
                    return true;
                }

                Logger.LogInfo("MelonLoader not found. Installing...");

                // Download MelonLoader installer
                string tempZip = Path.Combine(Path.GetTempPath(), $"MelonLoader_{MELONLOADER_VERSION}.zip");
                
                if (!File.Exists(tempZip))
                {
                    Logger.LogInfo($"Downloading MelonLoader v{MELONLOADER_VERSION}...");
                    await DownloadMelonLoader(tempZip);
                }

                // Extract to Holdfast directory
                Logger.LogInfo("Extracting MelonLoader...");
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, holdfastPath, true);

                // Verify installation
                if (File.Exists(melonLoaderDll))
                {
                    Logger.LogInfo("MelonLoader installed successfully.");
                    return true;
                }
                else
                {
                    Logger.LogError("MelonLoader installation verification failed.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to install MelonLoader: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Ensures the MelonLoader Mods directory exists.
        /// Note: Mod copying is now handled by ModManager.
        /// </summary>
        public void EnsureModsDirectory(string holdfastPath)
        {
            try
            {
                string modsPath = Path.Combine(holdfastPath, "MelonLoader", "Mods");
                if (!Directory.Exists(modsPath))
                {
                    Directory.CreateDirectory(modsPath);
                    Logger.LogInfo("Created MelonLoader Mods directory");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to ensure Mods directory: {ex}");
            }
        }

        private async Task DownloadMelonLoader(string outputPath)
        {
            try
            {
                using (var response = await _httpClient.GetAsync(MELONLOADER_DOWNLOAD_URL, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to download MelonLoader: {ex}");
                throw;
            }
        }
    }
}

