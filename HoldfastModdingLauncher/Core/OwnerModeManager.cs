using System;
using System.IO;

namespace HoldfastModdingLauncher.Core
{
    public class OwnerModeManager
    {
        private const string OWNER_KEY_FILE = "Owner.key";

        /// <summary>
        /// Determines if owner mode should be enabled.
        /// Checks for: Owner.key file, --debug flag, or environment variable.
        /// </summary>
        public bool IsOwnerMode(bool debugFlag = false)
        {
            // Check for --debug flag
            if (debugFlag)
            {
                return true;
            }

            // Check for Owner.key file in current directory
            string currentDir = Directory.GetCurrentDirectory();
            string ownerKeyPath = Path.Combine(currentDir, OWNER_KEY_FILE);
            if (File.Exists(ownerKeyPath))
            {
                return true;
            }

            // Check for Owner.key file in application directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string appOwnerKeyPath = Path.Combine(appDir, OWNER_KEY_FILE);
            if (File.Exists(appOwnerKeyPath))
            {
                return true;
            }

            // Check environment variable (for advanced users)
            string envOwner = Environment.GetEnvironmentVariable("HOLDFAST_MODDING_OWNER");
            if (!string.IsNullOrEmpty(envOwner) && envOwner.ToLower() == "true")
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates an Owner.key file in the current directory (for owner use only).
        /// </summary>
        public void CreateOwnerKey()
        {
            try
            {
                string currentDir = Directory.GetCurrentDirectory();
                string ownerKeyPath = Path.Combine(currentDir, OWNER_KEY_FILE);
                
                // Create a simple marker file
                File.WriteAllText(ownerKeyPath, $"Owner mode enabled\nCreated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                
                Logger.LogInfo("Owner.key file created. Owner mode will be enabled on next launch.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create Owner.key: {ex}");
                throw;
            }
        }
    }
}

