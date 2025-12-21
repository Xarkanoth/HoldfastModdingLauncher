using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// Helper class for colored console output in BepInEx console.
    /// Uses ANSI escape codes which work on Windows 10+ with virtual terminal enabled.
    /// Also parses Unity's <color=X> tags and converts them to ANSI codes.
    /// </summary>
    public static class ColoredLogger
    {
        // ANSI color codes
        public const string Reset = "\x1b[0m";
        public const string Bold = "\x1b[1m";
        
        // Foreground colors
        public const string Black = "\x1b[30m";
        public const string Red = "\x1b[31m";
        public const string Green = "\x1b[32m";
        public const string Yellow = "\x1b[33m";
        public const string Blue = "\x1b[34m";
        public const string Magenta = "\x1b[35m";
        public const string Cyan = "\x1b[36m";
        public const string White = "\x1b[37m";
        public const string Gray = "\x1b[90m";
        public const string Grey = "\x1b[90m"; // Alias
        
        // Bright foreground colors
        public const string BrightRed = "\x1b[91m";
        public const string BrightGreen = "\x1b[92m";
        public const string BrightYellow = "\x1b[93m";
        public const string BrightBlue = "\x1b[94m";
        public const string BrightMagenta = "\x1b[95m";
        public const string BrightCyan = "\x1b[96m";
        public const string BrightWhite = "\x1b[97m";
        
        // Background colors
        public const string BgRed = "\x1b[41m";
        public const string BgGreen = "\x1b[42m";
        public const string BgYellow = "\x1b[43m";
        public const string BgBlue = "\x1b[44m";
        
        private static ManualLogSource _log;
        private static bool _colorsEnabled = true;
        
        // Unity color name to ANSI code mapping
        private static readonly Dictionary<string, string> UnityColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "black", Black },
            { "white", BrightWhite },
            { "red", BrightRed },
            { "green", BrightGreen },
            { "blue", BrightBlue },
            { "yellow", BrightYellow },
            { "cyan", BrightCyan },
            { "magenta", BrightMagenta },
            { "grey", Gray },
            { "gray", Gray },
            { "orange", "\x1b[38;5;208m" }, // 256-color orange
            { "purple", "\x1b[38;5;135m" }, // 256-color purple
            { "brown", "\x1b[38;5;130m" }, // 256-color brown
            { "lime", "\x1b[38;5;118m" }, // 256-color lime
            { "olive", "\x1b[38;5;142m" }, // 256-color olive
            { "navy", "\x1b[38;5;17m" }, // 256-color navy
            { "teal", "\x1b[38;5;30m" }, // 256-color teal
            { "aqua", BrightCyan },
            { "fuchsia", BrightMagenta },
            { "silver", "\x1b[38;5;7m" }, // 256-color silver
            { "maroon", "\x1b[38;5;52m" }, // 256-color maroon
        };
        
        // Regex to match Unity color tags: <color=name> or <color=#RRGGBB> or <color=#RRGGBBAA>
        private static readonly Regex UnityColorTagRegex = new Regex(
            @"<color=([^>]+)>(.*?)</color>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );
        
        // Regex for hex color values
        private static readonly Regex HexColorRegex = new Regex(
            @"^#?([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",
            RegexOptions.Compiled
        );
        
        private static bool _unityLogHooked = false;
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            
            // Try to enable virtual terminal processing on Windows
            try
            {
                EnableVirtualTerminal();
            }
            catch
            {
                _colorsEnabled = false;
            }
            
            // Hook into Unity's log messages to colorize them
            HookUnityLogs();
        }
        
        /// <summary>
        /// Hook into Unity's log message system to colorize messages containing Unity color tags
        /// </summary>
        private static void HookUnityLogs()
        {
            if (_unityLogHooked) return;
            
            try
            {
                UnityEngine.Application.logMessageReceived += OnUnityLogMessage;
                _unityLogHooked = true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ColoredLogger] Could not hook Unity logs: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Unhook from Unity's log messages (call on shutdown)
        /// </summary>
        public static void Unhook()
        {
            if (_unityLogHooked)
            {
                try
                {
                    UnityEngine.Application.logMessageReceived -= OnUnityLogMessage;
                    _unityLogHooked = false;
                }
                catch { }
            }
        }
        
        /// <summary>
        /// Called when Unity logs a message. Intercepts messages with color tags
        /// and outputs them with ANSI colors directly to the console.
        /// </summary>
        private static void OnUnityLogMessage(string logString, string stackTrace, UnityEngine.LogType type)
        {
            // Only process messages that have Unity color tags
            if (!HasUnityColorTags(logString))
                return;
            
            // Skip if colors aren't enabled
            if (!_colorsEnabled)
                return;
            
            try
            {
                // Parse and colorize the message
                string coloredMessage = ParseUnityColors(logString);
                
                // Only output if we actually applied colors (message changed)
                if (coloredMessage != logString)
                {
                    // Write directly to console with colors - this avoids BepInEx log formatting
                    // and shows as a clean colored line
                    Console.WriteLine(coloredMessage);
                }
            }
            catch
            {
                // Silently fail if console output fails
            }
        }
        
        private static void EnableVirtualTerminal()
        {
            // Enable ANSI escape codes on Windows 10+
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (handle != IntPtr.Zero)
            {
                GetConsoleMode(handle, out uint mode);
                SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
            }
        }
        
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        
        /// <summary>
        /// Log with custom color
        /// </summary>
        public static void Log(string color, string message)
        {
            if (_log == null) return;
            
            if (_colorsEnabled)
                _log.LogInfo($"{color}{message}{Reset}");
            else
                _log.LogInfo(message);
        }
        
        /// <summary>
        /// Log info in cyan
        /// </summary>
        public static void Info(string message)
        {
            Log(Cyan, message);
        }
        
        /// <summary>
        /// Log success in green
        /// </summary>
        public static void Success(string message)
        {
            Log(BrightGreen, message);
        }
        
        /// <summary>
        /// Log warning in yellow
        /// </summary>
        public static void Warning(string message)
        {
            if (_log == null) return;
            
            if (_colorsEnabled)
                _log.LogWarning($"{Yellow}{message}{Reset}");
            else
                _log.LogWarning(message);
        }
        
        /// <summary>
        /// Log error in red
        /// </summary>
        public static void Error(string message)
        {
            if (_log == null) return;
            
            if (_colorsEnabled)
                _log.LogError($"{BrightRed}{message}{Reset}");
            else
                _log.LogError(message);
        }
        
        /// <summary>
        /// Log RC login status
        /// </summary>
        public static void RCLogin(bool success)
        {
            if (success)
                Success("✓ RC Login successful - Advanced Admin UI enabled");
            else
                Warning("✗ RC Login failed - Advanced Admin UI disabled");
        }
        
        /// <summary>
        /// Log player event
        /// </summary>
        public static void PlayerEvent(string eventType, int playerId, string details = "")
        {
            string detailsStr = string.IsNullOrEmpty(details) ? "" : $" - {details}";
            Log(Magenta, $"[{eventType}] Player {playerId}{detailsStr}");
        }
        
        /// <summary>
        /// Log feature status
        /// </summary>
        public static void FeatureStatus(string featureName, bool enabled)
        {
            if (enabled)
                Log(Green, $"[{featureName}] Enabled");
            else
                Log(Red, $"[{featureName}] Disabled");
        }
        
        /// <summary>
        /// Parse Unity's <color=X> tags and convert to ANSI-colored console output.
        /// Supports color names (Grey, Red, etc.) and hex values (#RRGGBB, #RRGGBBAA).
        /// </summary>
        /// <param name="message">Message containing Unity color tags</param>
        /// <returns>Message with ANSI color codes</returns>
        public static string ParseUnityColors(string message)
        {
            if (string.IsNullOrEmpty(message) || !_colorsEnabled)
                return message;
            
            // Check if message contains color tags
            if (!message.Contains("<color="))
                return message;
            
            try
            {
                // Replace each <color=X>text</color> with ANSI-colored text
                string result = UnityColorTagRegex.Replace(message, match =>
                {
                    string colorValue = match.Groups[1].Value.Trim();
                    string innerText = match.Groups[2].Value;
                    
                    string ansiColor = GetAnsiColorFromUnity(colorValue);
                    return $"{ansiColor}{innerText}{Reset}";
                });
                
                return result;
            }
            catch
            {
                // If parsing fails, return original message
                return message;
            }
        }
        
        /// <summary>
        /// Convert Unity color value to ANSI escape code.
        /// Supports named colors and hex values.
        /// </summary>
        private static string GetAnsiColorFromUnity(string colorValue)
        {
            if (string.IsNullOrEmpty(colorValue))
                return "";
            
            // Check if it's a named color
            if (UnityColorMap.TryGetValue(colorValue, out string ansiCode))
                return ansiCode;
            
            // Check if it's a hex color (#RRGGBB or #RRGGBBAA)
            if (colorValue.StartsWith("#") || HexColorRegex.IsMatch(colorValue))
            {
                return HexToAnsi(colorValue);
            }
            
            // Unknown color - return empty (no color change)
            return "";
        }
        
        /// <summary>
        /// Convert hex color (#RRGGBB or #RRGGBBAA) to ANSI 24-bit color escape code.
        /// </summary>
        private static string HexToAnsi(string hex)
        {
            try
            {
                // Remove # if present
                hex = hex.TrimStart('#');
                
                // Parse RGB values (ignore alpha if present)
                if (hex.Length >= 6)
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                    
                    // Use 24-bit true color ANSI escape: \x1b[38;2;R;G;Bm
                    return $"\x1b[38;2;{r};{g};{b}m";
                }
            }
            catch
            {
                // Invalid hex - return empty
            }
            
            return "";
        }
        
        /// <summary>
        /// Log a message that may contain Unity color tags.
        /// Parses <color=X> tags and displays with appropriate console colors.
        /// </summary>
        public static void LogUnityMessage(string message, LogLevel level = LogLevel.Info)
        {
            if (_log == null) return;
            
            string coloredMessage = ParseUnityColors(message);
            
            switch (level)
            {
                case LogLevel.Error:
                case LogLevel.Fatal:
                    _log.LogError(coloredMessage);
                    break;
                case LogLevel.Warning:
                    _log.LogWarning(coloredMessage);
                    break;
                case LogLevel.Debug:
                    _log.LogDebug(coloredMessage);
                    break;
                default:
                    _log.LogInfo(coloredMessage);
                    break;
            }
        }
        
        /// <summary>
        /// Check if a message contains Unity color tags
        /// </summary>
        public static bool HasUnityColorTags(string message)
        {
            return !string.IsNullOrEmpty(message) && message.Contains("<color=");
        }
    }
}

