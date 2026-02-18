using System;
using System.IO;
using System.Text.Json;

namespace HoldfastModdingLauncher.Core
{
    public class LauncherSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.json");

        public bool AutoUpdateEnabled { get; set; } = true;
        public bool CheckForUpdatesOnStartup { get; set; } = true;
        public string LastSkippedVersion { get; set; } = string.Empty;
        public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
        
        // Mod registry settings
        public string ModRegistryUrl { get; set; } = string.Empty;
        public DateTime LastModRegistryCheck { get; set; } = DateTime.MinValue;
        public bool CheckForModUpdatesOnStartup { get; set; } = true;

        // Self-hosted mod server
        public string ModServerUrl { get; set; } = string.Empty;

        private static LauncherSettings? _instance;
        public static LauncherSettings Instance => _instance ??= Load();

        public static LauncherSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                    if (settings != null)
                    {
                        _instance = settings;
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to load launcher settings: {ex.Message}");
            }

            return new LauncherSettings();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
                _instance = this;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save launcher settings: {ex.Message}");
            }
        }
    }
}

