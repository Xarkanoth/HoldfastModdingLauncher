using System;
using System.IO;

namespace HoldfastModdingLauncher.Core
{
    public static class Logger
    {
        private static readonly string LogDirectory;
        private static readonly string LogFile;

        static Logger()
        {
            try
            {
                LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoldfastModdingLauncher", "Logs");
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd");
                LogFile = Path.Combine(LogDirectory, $"Launcher_{timestamp}.log");
            }
            catch
            {
                // If we can't set up logging, continue without it
                LogDirectory = null;
                LogFile = null;
            }
        }

        public static void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public static void LogError(string message)
        {
            WriteLog("ERROR", message);
        }

        public static void LogWarning(string message)
        {
            WriteLog("WARNING", message);
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(LogFile))
                {
                    return;
                }

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                
                // Also write to console if running in debug mode
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.WriteLine(logEntry);
                }

                File.AppendAllText(LogFile, logEntry + Environment.NewLine);
            }
            catch
            {
                // Silently fail if logging fails
            }
        }
    }
}

