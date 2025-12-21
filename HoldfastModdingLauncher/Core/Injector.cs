using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace HoldfastModdingLauncher.Core
{
    /// <summary>
    /// Handles launching Holdfast with mods.
    /// Uses BepInEx for mod loading - installs it temporarily when launching modded,
    /// removes it for vanilla play.
    /// </summary>
    public class Injector
    {
        private readonly string _launcherDir;
        private readonly string _modsDir;
        private readonly string _bepInExCacheDir;
        
        private const string BEPINEX_DOWNLOAD_URL = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.22/BepInEx_x64_5.4.22.0.zip";

        public Injector()
        {
            _launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            _modsDir = Path.Combine(_launcherDir, "Mods");
            _bepInExCacheDir = Path.Combine(_launcherDir, "BepInExCache");
        }

        /// <summary>
        /// Launches Holdfast with mods enabled.
        /// Installs BepInEx temporarily and copies mods to the game.
        /// Returns the Process object so caller can monitor it, or null on failure.
        /// </summary>
        public async Task<Process?> LaunchWithModsAsync(string holdfastPath, List<string> enabledMods, bool showConsole = false, Action<string>? statusCallback = null)
        {
            string holdfastExe = Path.Combine(holdfastPath, "Holdfast NaW.exe");
            
            if (!File.Exists(holdfastExe))
            {
                Logger.LogError($"Holdfast executable not found: {holdfastExe}");
                return null;
            }

            try
            {
                Logger.LogInfo($"Launching Holdfast with {enabledMods.Count} mod(s)...");

                // Install BepInEx to game folder
                statusCallback?.Invoke("Setting up mod loader...");
                await InstallBepInExAsync(holdfastPath, statusCallback);

                // Copy mods to BepInEx plugins folder
                statusCallback?.Invoke("Copying mods...");
                string pluginsDir = Path.Combine(holdfastPath, "BepInEx", "plugins");
                Directory.CreateDirectory(pluginsDir);

                // Clean and copy mods
                CleanPluginsFolder(pluginsDir);
                foreach (string modPath in enabledMods)
                {
                    if (File.Exists(modPath))
                    {
                        string destPath = Path.Combine(pluginsDir, Path.GetFileName(modPath));
                        File.Copy(modPath, destPath, true);
                        Logger.LogInfo($"Loaded mod: {Path.GetFileName(modPath)}");
                    }
                }

                // Configure BepInEx
                ConfigureBepInEx(holdfastPath, showConsole);
                
                // Enable doorstop so BepInEx loads
                EnableDoorstop(holdfastPath);

                // Launch Holdfast
                statusCallback?.Invoke("Launching game...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = holdfastExe,
                    WorkingDirectory = holdfastPath,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                Logger.LogInfo("Holdfast launched with mods!");
                
                // Note: We keep doorstop enabled while game is running
                // It will be disabled when launching vanilla or by user choice
                return process;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to launch with mods: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Launches Holdfast in vanilla mode (no mods).
        /// Disables BepInEx doorstop so mods don't load.
        /// </summary>
        public bool LaunchVanilla(string holdfastPath)
        {
            string holdfastExe = Path.Combine(holdfastPath, "Holdfast NaW.exe");
            
            if (!File.Exists(holdfastExe))
            {
                Logger.LogError($"Holdfast executable not found: {holdfastExe}");
                return false;
            }

            try
            {
                Logger.LogInfo("Launching Holdfast in vanilla mode...");

                // Disable BepInEx doorstop (keeps files but doesn't load)
                DisableDoorstop(holdfastPath);

                // Launch Holdfast
                var startInfo = new ProcessStartInfo
                {
                    FileName = holdfastExe,
                    WorkingDirectory = holdfastPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                Logger.LogInfo("Holdfast launched in vanilla mode!");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to launch vanilla: {ex}");
                return false;
            }
        }
        
        /// <summary>
        /// Ensures BepInEx is disabled so direct Holdfast.exe launches run vanilla.
        /// Call this after the game exits or when the launcher closes.
        /// </summary>
        public void EnsureVanillaByDefault(string holdfastPath)
        {
            if (string.IsNullOrEmpty(holdfastPath) || !Directory.Exists(holdfastPath))
                return;
                
            DisableDoorstop(holdfastPath);
        }

        /// <summary>
        /// Gets all enabled mod paths from the Mods folder.
        /// </summary>
        public List<string> GetEnabledModPaths(ModManager modManager)
        {
            var enabledMods = modManager.GetEnabledMods();
            return enabledMods.Select(m => m.FullPath).ToList();
        }

        /// <summary>
        /// Installs BepInEx to the game folder from our cache.
        /// Downloads if not cached.
        /// </summary>
        private async Task InstallBepInExAsync(string holdfastPath, Action<string>? statusCallback = null)
        {
            string winhttpDll = Path.Combine(holdfastPath, "winhttp.dll");
            string doorstopConfig = Path.Combine(holdfastPath, "doorstop_config.ini");
            string bepInExDir = Path.Combine(holdfastPath, "BepInEx");

            // Check if already installed
            if (File.Exists(winhttpDll) && Directory.Exists(bepInExDir))
            {
                Logger.LogInfo("BepInEx already installed in game folder");
                return;
            }

            // Check cache
            string cachedWinhttp = Path.Combine(_bepInExCacheDir, "winhttp.dll");
            string cachedDoorstop = Path.Combine(_bepInExCacheDir, "doorstop_config.ini");
            string cachedBepInEx = Path.Combine(_bepInExCacheDir, "BepInEx");

            if (!Directory.Exists(_bepInExCacheDir) || !File.Exists(cachedWinhttp))
            {
                statusCallback?.Invoke("Downloading mod loader (first time only)...");
                Logger.LogInfo("BepInEx not cached. Downloading...");
                await DownloadAndCacheBepInEx();
            }

            // Copy from cache to game folder
            statusCallback?.Invoke("Installing mod loader...");
            Logger.LogInfo("Installing BepInEx to game folder...");
            
            if (File.Exists(cachedWinhttp))
                File.Copy(cachedWinhttp, winhttpDll, true);
            
            if (File.Exists(cachedDoorstop))
                File.Copy(cachedDoorstop, doorstopConfig, true);
            
            if (Directory.Exists(cachedBepInEx))
                CopyDirectory(cachedBepInEx, bepInExDir);

            Logger.LogInfo("BepInEx installed successfully");
        }

        /// <summary>
        /// Removes BepInEx from the game folder.
        /// </summary>
        private void UninstallBepInEx(string holdfastPath)
        {
            try
            {
                string winhttpDll = Path.Combine(holdfastPath, "winhttp.dll");
                string doorstopConfig = Path.Combine(holdfastPath, "doorstop_config.ini");
                string bepInExDir = Path.Combine(holdfastPath, "BepInEx");

                if (File.Exists(winhttpDll))
                    File.Delete(winhttpDll);
                
                if (File.Exists(doorstopConfig))
                    File.Delete(doorstopConfig);
                
                if (Directory.Exists(bepInExDir))
                    Directory.Delete(bepInExDir, true);

                Logger.LogInfo("BepInEx removed from game folder");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not fully remove BepInEx: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads BepInEx and caches it in our launcher folder.
        /// </summary>
        private async Task DownloadAndCacheBepInEx()
        {
            try
            {
                Directory.CreateDirectory(_bepInExCacheDir);
                
                string zipPath = Path.Combine(_bepInExCacheDir, "BepInEx.zip");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var response = await client.GetAsync(BEPINEX_DOWNLOAD_URL);
                    response.EnsureSuccessStatusCode();
                    
                    using (var fs = new FileStream(zipPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                // Extract to cache
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, _bepInExCacheDir, true);
                
                // Clean up zip
                File.Delete(zipPath);
                
                Logger.LogInfo("BepInEx downloaded and cached");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to download BepInEx: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Configures BepInEx settings.
        /// </summary>
        private void ConfigureBepInEx(string holdfastPath, bool showConsole)
        {
            string configDir = Path.Combine(holdfastPath, "BepInEx", "config");
            Directory.CreateDirectory(configDir);
            
            string configPath = Path.Combine(configDir, "BepInEx.cfg");
            
            string config = $@"[Logging.Console]
Enabled = {(showConsole ? "true" : "false")}

[Logging.Disk]
Enabled = true
";
            File.WriteAllText(configPath, config);
        }
        
        /// <summary>
        /// Enables BepInEx doorstop (allows mods to load).
        /// Call this before launching with mods.
        /// </summary>
        public void EnableDoorstop(string holdfastPath)
        {
            string doorstopConfig = Path.Combine(holdfastPath, "doorstop_config.ini");
            
            if (!File.Exists(doorstopConfig))
            {
                Logger.LogWarning("doorstop_config.ini not found, BepInEx may not be installed");
                return;
            }
            
            try
            {
                string content = File.ReadAllText(doorstopConfig);
                
                // Replace enabled=false with enabled=true
                if (content.Contains("enabled=false"))
                {
                    content = content.Replace("enabled=false", "enabled=true");
                    File.WriteAllText(doorstopConfig, content);
                    Logger.LogInfo("BepInEx doorstop ENABLED - mods will load");
                }
                else if (content.Contains("enabled=true"))
                {
                    Logger.LogInfo("BepInEx doorstop already enabled");
                }
                else
                {
                    // Old format or missing - add/update the setting
                    if (!content.Contains("[General]"))
                    {
                        content = "[General]\nenabled=true\n" + content;
                    }
                    else
                    {
                        content = content.Replace("[General]", "[General]\nenabled=true");
                    }
                    File.WriteAllText(doorstopConfig, content);
                    Logger.LogInfo("BepInEx doorstop enabled (added setting)");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to enable doorstop: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Disables BepInEx doorstop (vanilla mode - no mods load).
        /// This allows Holdfast.exe to run vanilla when launched directly.
        /// </summary>
        public void DisableDoorstop(string holdfastPath)
        {
            string doorstopConfig = Path.Combine(holdfastPath, "doorstop_config.ini");
            
            if (!File.Exists(doorstopConfig))
            {
                return; // Nothing to disable
            }
            
            try
            {
                string content = File.ReadAllText(doorstopConfig);
                
                // Replace enabled=true with enabled=false
                if (content.Contains("enabled=true"))
                {
                    content = content.Replace("enabled=true", "enabled=false");
                    File.WriteAllText(doorstopConfig, content);
                    Logger.LogInfo("BepInEx doorstop DISABLED - Holdfast.exe will run vanilla");
                }
                else
                {
                    Logger.LogInfo("BepInEx doorstop already disabled");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to disable doorstop: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks if BepInEx doorstop is currently enabled.
        /// </summary>
        public bool IsDoorstopEnabled(string holdfastPath)
        {
            string doorstopConfig = Path.Combine(holdfastPath, "doorstop_config.ini");
            
            if (!File.Exists(doorstopConfig))
                return false;
            
            try
            {
                string content = File.ReadAllText(doorstopConfig);
                return content.Contains("enabled=true");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes all DLL files from the plugins folder.
        /// </summary>
        private void CleanPluginsFolder(string pluginsDir)
        {
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                return;
            }

            try
            {
                foreach (string file in Directory.GetFiles(pluginsDir, "*.dll"))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not clean plugins folder: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively copies a directory.
        /// </summary>
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
    }
}

