using System;
using System.IO;
using Microsoft.Win32;

namespace HoldfastModdingLauncher.Core
{
    public class HoldfastManager
    {
        /// <summary>
        /// Finds the Holdfast: Nations At War installation directory.
        /// Checks common Steam installation paths and registry.
        /// </summary>
        public string FindHoldfastInstallation()
        {
            // Check current directory (if launcher is in Mods folder)
            string currentDir = Directory.GetCurrentDirectory();
            if (IsValidHoldfastDirectory(currentDir))
            {
                return currentDir;
            }

            // Check parent directory (common case: Mods folder is inside Holdfast directory)
            string parentDir = Directory.GetParent(currentDir)?.FullName;
            if (!string.IsNullOrEmpty(parentDir) && IsValidHoldfastDirectory(parentDir))
            {
                return parentDir;
            }

            // Check Steam common installation paths
            string[] steamPaths = {
                @"C:\Program Files (x86)\Steam\steamapps\common\Holdfast Nations At War",
                @"C:\Program Files\Steam\steamapps\common\Holdfast Nations At War",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Holdfast Nations At War"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", "Holdfast Nations At War")
            };

            foreach (string path in steamPaths)
            {
                if (IsValidHoldfastDirectory(path))
                {
                    return path;
                }
            }

            // Try to find via Steam registry
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 589290"))
                {
                    if (key != null)
                    {
                        string installLocation = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installLocation) && IsValidHoldfastDirectory(installLocation))
                        {
                            return installLocation;
                        }
                    }
                }
            }
            catch
            {
                // Registry access may fail, continue with other methods
            }

            // Check all Steam library folders
            try
            {
                string steamPath = GetSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFoldersPath))
                    {
                        string[] libraryFolders = ParseLibraryFolders(libraryFoldersPath);
                        foreach (string libraryFolder in libraryFolders)
                        {
                            string holdfastPath = Path.Combine(libraryFolder, "steamapps", "common", "Holdfast Nations At War");
                            if (IsValidHoldfastDirectory(holdfastPath))
                            {
                                return holdfastPath;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Continue if library folder parsing fails
            }

            return null;
        }

        public bool IsValidHoldfastDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return false;
            }

            // Check for Holdfast executable
            string exePath = Path.Combine(path, "Holdfast NaW.exe");
            if (!File.Exists(exePath))
            {
                return false;
            }

            // Check for Holdfast NaW_Data folder
            string dataPath = Path.Combine(path, "Holdfast NaW_Data");
            if (!Directory.Exists(dataPath))
            {
                return false;
            }

            return true;
        }

        private string GetSteamPath()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                    {
                        return key.GetValue("InstallPath") as string;
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        return key.GetValue("SteamPath") as string;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private string[] ParseLibraryFolders(string libraryFoldersPath)
        {
            var folders = new System.Collections.Generic.List<string>();
            
            try
            {
                string content = File.ReadAllText(libraryFoldersPath);
                // Simple parsing - libraryfolders.vdf format is key-value pairs
                // Look for paths like "path" "C:\\..."
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("\"path\""))
                    {
                        int startIdx = line.IndexOf('"', line.IndexOf("\"path\"") + 6) + 1;
                        int endIdx = line.IndexOf('"', startIdx);
                        if (startIdx > 0 && endIdx > startIdx)
                        {
                            string path = line.Substring(startIdx, endIdx - startIdx);
                            path = path.Replace("\\\\", "\\"); // Unescape backslashes
                            folders.Add(path);
                        }
                    }
                }
            }
            catch
            {
            }

            return folders.ToArray();
        }
    }
}

