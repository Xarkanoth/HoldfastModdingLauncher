using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace HoldfastModdingLauncher.Core
{
    public class PreferencesManager
    {
        /// <summary>
        /// Configures MelonLoader preferences, enforcing console visibility setting.
        /// </summary>
        public void ConfigurePreferences(string preferencesPath, bool enableConsole)
        {
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(preferencesPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // CRITICAL: Configure LaunchOptions.ini to hide/show console
                // MelonLoader uses --melonloader.hideconsole launch argument to control console visibility
                ConfigureLaunchOptions(directory, enableConsole);

                // Read existing preferences or create new
                Dictionary<string, string> preferences = new Dictionary<string, string>();
                
                if (File.Exists(preferencesPath))
                {
                    preferences = ReadPreferences(preferencesPath);
                }

                // Set console and logging preferences
                preferences["Console_Enabled"] = enableConsole ? "true" : "false";
                preferences["Log_Enabled"] = "true"; // Always enable logging to file
                preferences["Log_File_Enabled"] = "true"; // Always enable file logging
                preferences["Log_File_Append"] = "false"; // Start fresh log each time
                preferences["Log_File_Console_Enabled"] = "false"; // Never log console to file (redundant)
                
                // Ensure console is truly disabled for non-owner mode
                if (!enableConsole)
                {
                    preferences["Console_Title"] = ""; // Clear console title
                    preferences["Console_AlwaysOnTop"] = "false";
                }
                
                // Remove read-only attribute if it exists (so we can write)
                try
                {
                    File.SetAttributes(preferencesPath, FileAttributes.Normal);
                }
                catch
                {
                    // Ignore if file doesn't exist yet or we can't set attributes
                }

                // Write preferences
                WritePreferences(preferencesPath, preferences);

                // Verify the file was written correctly
                if (File.Exists(preferencesPath))
                {
                    string content = File.ReadAllText(preferencesPath);
                    bool hasConsoleEnabled = content.Contains("Console_Enabled");
                    bool consoleSetCorrectly = !enableConsole 
                        ? content.Contains("Console_Enabled = false") 
                        : content.Contains("Console_Enabled = true");
                    
                    if (!hasConsoleEnabled || !consoleSetCorrectly)
                    {
                        Logger.LogWarning($"Preferences file may not have been written correctly. Re-writing...");
                        WritePreferences(preferencesPath, preferences);
                    }
                    
                    Logger.LogInfo($"Preferences configured - Console: {(enableConsole ? "ENABLED" : "DISABLED")}, File verified: {preferencesPath}");
                }
                else
                {
                    Logger.LogError($"Preferences file was not created at: {preferencesPath}");
                    throw new FileNotFoundException($"Failed to create preferences file: {preferencesPath}");
                }

                // Make read-only to prevent user modification (soft lock)
                // We'll remove this attribute next time we need to update it
                try
                {
                    File.SetAttributes(preferencesPath, FileAttributes.ReadOnly);
                    Logger.LogInfo($"Preferences file set to read-only: {preferencesPath}");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Could not set preferences file to read-only: {ex.Message}");
                    // Continue - read-only is a nice-to-have, not critical
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to configure preferences: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Configures LaunchOptions.ini to control console visibility via --melonloader.hideconsole
        /// </summary>
        private void ConfigureLaunchOptions(string melonLoaderDirectory, bool enableConsole)
        {
            try
            {
                string launchOptionsPath = Path.Combine(melonLoaderDirectory, "LaunchOptions.ini");
                
                // Read existing launch options
                List<string> lines = new List<string>();
                if (File.Exists(launchOptionsPath))
                {
                    lines.AddRange(File.ReadAllLines(launchOptionsPath));
                }

                // Remove any existing hideconsole entries
                lines.RemoveAll(line => line.Trim().StartsWith("--melonloader.hideconsole", StringComparison.OrdinalIgnoreCase));

                // Add hideconsole if console should be disabled
                if (!enableConsole)
                {
                    lines.Add("--melonloader.hideconsole");
                    Logger.LogInfo("Added --melonloader.hideconsole to LaunchOptions.ini");
                }
                else
                {
                    Logger.LogInfo("Removed --melonloader.hideconsole from LaunchOptions.ini (console enabled)");
                }

                // Write launch options
                File.WriteAllLines(launchOptionsPath, lines);
                Logger.LogInfo($"LaunchOptions.ini configured - Console: {(enableConsole ? "ENABLED" : "DISABLED")}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to configure LaunchOptions.ini: {ex.Message}");
                // Don't throw - LaunchOptions.ini is important but not critical if it fails
            }
        }

        private Dictionary<string, string> ReadPreferences(string path)
        {
            var preferences = new Dictionary<string, string>();
            
            try
            {
                if (!File.Exists(path))
                {
                    return preferences;
                }

                // Remove read-only attribute if present
                File.SetAttributes(path, FileAttributes.Normal);

                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    {
                        continue; // Skip empty lines and comments
                    }

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        string key = trimmed.Substring(0, equalsIndex).Trim();
                        string value = trimmed.Substring(equalsIndex + 1).Trim();
                        preferences[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read preferences: {ex}");
            }

            return preferences;
        }

        private void WritePreferences(string path, Dictionary<string, string> preferences)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# MelonLoader Preferences");
                sb.AppendLine("# This file is automatically managed by Holdfast Modding Launcher");
                sb.AppendLine("# Do not edit manually - your changes will be overwritten");
                sb.AppendLine();

                // Write common preferences (in order of importance)
                WritePreference(sb, "Console_Enabled", preferences);
                WritePreference(sb, "Console_Title", preferences);
                WritePreference(sb, "Console_AlwaysOnTop", preferences);
                WritePreference(sb, "Log_Enabled", preferences);
                WritePreference(sb, "Log_File_Enabled", preferences);
                WritePreference(sb, "Log_File_Append", preferences);
                WritePreference(sb, "Log_File_Path", preferences, "Logs");
                WritePreference(sb, "Log_File_Console_Enabled", preferences, "false");
                
                // Write any other preferences that were in the original file
                foreach (var kvp in preferences)
                {
                    if (!IsCommonPreference(kvp.Key))
                    {
                        WritePreference(sb, kvp.Key, preferences);
                    }
                }

                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to write preferences: {ex}");
                throw;
            }
        }

        private void WritePreference(StringBuilder sb, string key, Dictionary<string, string> preferences, string? defaultValue = null)
        {
            string value = preferences.ContainsKey(key) ? preferences[key] : defaultValue;
            if (value != null)
            {
                sb.AppendLine($"{key} = {value}");
            }
        }

        private bool IsCommonPreference(string key)
        {
            return key == "Console_Enabled" ||
                   key == "Console_Title" ||
                   key == "Console_AlwaysOnTop" ||
                   key == "Log_Enabled" ||
                   key == "Log_File_Enabled" ||
                   key == "Log_File_Append" ||
                   key == "Log_File_Path" ||
                   key == "Log_File_Console_Enabled";
        }
        
        /// <summary>
        /// Checks if master login is currently active
        /// </summary>
        public bool IsMasterLoggedIn()
        {
            try
            {
                string appDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HoldfastModding");
                string tokenPath = Path.Combine(appDataFolder, "master_login.token");
                
                if (File.Exists(tokenPath))
                {
                    string token = File.ReadAllText(tokenPath).Trim();
                    return token == "MASTER_ACCESS_GRANTED" || VerifyMasterToken(token);
                }
            }
            catch
            {
                // Ignore errors
            }
            
            return false;
        }
        
        private bool VerifyMasterToken(string token)
        {
            // Same verification logic as mods use
            const string HASH_SALT = "HF_MODDING_2024_XARK";
            
            try
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    string machineId = Environment.MachineName + Environment.UserName;
                    string today = DateTime.UtcNow.ToString("yyyyMMdd");
                    string yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyyMMdd");
                    
                    string tokenDataToday = $"MASTER_ACCESS|{machineId}|{today}";
                    string tokenDataYesterday = $"MASTER_ACCESS|{machineId}|{yesterday}";
                    
                    byte[] hashToday = sha256.ComputeHash(Encoding.UTF8.GetBytes(tokenDataToday + HASH_SALT));
                    byte[] hashYesterday = sha256.ComputeHash(Encoding.UTF8.GetBytes(tokenDataYesterday + HASH_SALT));
                    
                    var sbToday = new StringBuilder();
                    var sbYesterday = new StringBuilder();
                    foreach (byte b in hashToday)
                        sbToday.Append(b.ToString("x2"));
                    foreach (byte b in hashYesterday)
                        sbYesterday.Append(b.ToString("x2"));
                    
                    string hashTodayStr = sbToday.ToString();
                    string hashYesterdayStr = sbYesterday.ToString();
                    
                    return token.Equals(hashTodayStr, StringComparison.OrdinalIgnoreCase) ||
                           token.Equals(hashYesterdayStr, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

