using System;
using BepInEx.Logging;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// Helper class for colored console output in BepInEx console.
    /// Uses ANSI escape codes which work on Windows 10+ with virtual terminal enabled.
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
    }
}

