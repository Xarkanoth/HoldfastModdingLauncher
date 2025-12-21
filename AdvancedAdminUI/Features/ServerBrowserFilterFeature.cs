using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Filters official "Anvil Game Studios Official" servers from the server browser
    /// unless the user is master logged in via the launcher.
    /// </summary>
    public class ServerBrowserFilterFeature : IAdminFeature
    {
        public string FeatureName => "Server Browser Filter";
        public bool IsEnabled { get; private set; }
        
        private static ManualLogSource _log;
        private static bool _isMasterLoggedIn = false;
        private static float _lastTokenCheck = 0f;
        private const float TOKEN_CHECK_INTERVAL = 5f; // Check every 5 seconds
        
        // Token validation constants (must match launcher)
        private const string HASH_SALT = "HF_MODDING_2024_XARK";
        private const string LOGIN_TOKEN_FILE = "master_login.token";
        
        public ServerBrowserFilterFeature()
        {
            _log = AdvancedAdminUIMod.Log;
        }
        
        public void Enable()
        {
            if (IsEnabled) return;
            IsEnabled = true;
            
            _log?.LogInfo("[ServerBrowserFilter] Enabled - Official servers will be hidden for non-master users");
            _log?.LogInfo("[ServerBrowserFilter] Scanning: " + SERVER_BROWSER_PATH);
            
            // Initial token check
            CheckMasterLoginToken();
        }
        
        public void Disable()
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            _log?.LogInfo("[ServerBrowserFilter] Disabled");
        }
        
        private static float _lastServerScan = 0f;
        private const float SERVER_SCAN_INTERVAL = 0.5f; // Scan every 0.5 seconds when in server browser
        
        // UI Path to server browser content
        private const string SERVER_BROWSER_PATH = "MainCanvas/Main Menu Panels/Panel Container/Play/Server Browser/Server Browser Container/Browser Container/Scroll View/Viewport/Content";
        
        public void OnUpdate()
        {
            // Periodically check if master login token is valid
            if (Time.time - _lastTokenCheck > TOKEN_CHECK_INTERVAL)
            {
                _lastTokenCheck = Time.time;
                CheckMasterLoginToken();
            }
            
            // If master logged in, don't filter anything
            if (_isMasterLoggedIn)
                return;
            
            // Periodically scan and filter server browser items
            if (Time.time - _lastServerScan > SERVER_SCAN_INTERVAL)
            {
                _lastServerScan = Time.time;
                FilterServerBrowserItems();
            }
        }
        
        /// <summary>
        /// Scans the server browser and hides/destroys official server entries
        /// </summary>
        private void FilterServerBrowserItems()
        {
            try
            {
                // Find the server browser content container
                GameObject contentObj = GameObject.Find(SERVER_BROWSER_PATH);
                if (contentObj == null)
                    return; // Server browser not open
                
                Transform content = contentObj.transform;
                int filteredCount = 0;
                
                // Iterate through all server items
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    Transform serverItem = content.GetChild(i);
                    
                    // Check if this is a HarperServerBrowserItem
                    if (!serverItem.name.Contains("HarperServerBrowserItem"))
                        continue;
                    
                    // Try to find the server name text
                    string serverName = GetServerName(serverItem);
                    
                    if (ShouldFilterServer(serverName))
                    {
                        // Destroy or hide the official server entry
                        serverItem.gameObject.SetActive(false);
                        filteredCount++;
                    }
                }
                
                if (filteredCount > 0)
                {
                    _log?.LogDebug($"[ServerBrowserFilter] Filtered {filteredCount} official server(s)");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ServerBrowserFilter] Error filtering servers: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extracts the server name from a HarperServerBrowserItem
        /// </summary>
        private string GetServerName(Transform serverItem)
        {
            // Try common patterns for finding server name text
            // Pattern 1: Direct child with Text component
            Text[] texts = serverItem.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                // Look for the server name text (usually the first/largest text, or has specific name)
                string parentName = text.transform.parent?.name?.ToLower() ?? "";
                string objName = text.name.ToLower();
                
                // Common naming patterns for server name
                if (objName.Contains("name") || objName.Contains("server") || 
                    objName.Contains("title") || parentName.Contains("name"))
                {
                    return text.text;
                }
            }
            
            // Fallback: Return the first text that looks like a server name
            foreach (var text in texts)
            {
                if (!string.IsNullOrEmpty(text.text) && text.text.Length > 5)
                {
                    // Skip player counts, ping, etc.
                    if (int.TryParse(text.text, out _))
                        continue;
                    if (text.text.Contains("ms") || text.text.Contains("/"))
                        continue;
                    
                    return text.text;
                }
            }
            
            // Also try TMPro text (TextMeshPro)
            var tmpTexts = serverItem.GetComponentsInChildren<Component>(true)
                .Where(c => c.GetType().Name.Contains("TMP_Text") || c.GetType().Name.Contains("TextMeshPro"));
            
            foreach (var tmp in tmpTexts)
            {
                var textProp = tmp.GetType().GetProperty("text");
                if (textProp != null)
                {
                    string tmpText = textProp.GetValue(tmp) as string;
                    if (!string.IsNullOrEmpty(tmpText) && tmpText.Length > 5)
                    {
                        if (int.TryParse(tmpText, out _))
                            continue;
                        if (tmpText.Contains("ms") || tmpText.Contains("/"))
                            continue;
                        
                        return tmpText;
                    }
                }
            }
            
            return null;
        }
        
        public void OnGUI() { }
        public void OnApplicationQuit() { }
        
        /// <summary>
        /// Checks if the master login token file exists and is valid for this machine
        /// </summary>
        private void CheckMasterLoginToken()
        {
            bool wasLoggedIn = _isMasterLoggedIn;
            _isMasterLoggedIn = false;
            
            try
            {
                // Check multiple possible token locations
                string[] tokenPaths = GetPossibleTokenPaths();
                
                foreach (string tokenPath in tokenPaths)
                {
                    if (File.Exists(tokenPath))
                    {
                        string token = File.ReadAllText(tokenPath).Trim();
                        
                        // Verify the token is valid for this machine
                        if (VerifySecureToken(token))
                        {
                            _isMasterLoggedIn = true;
                            break;
                        }
                    }
                }
                
                // Log state change
                if (_isMasterLoggedIn != wasLoggedIn)
                {
                    if (_isMasterLoggedIn)
                        _log?.LogInfo("[ServerBrowserFilter] Master login detected - official servers visible");
                    else
                        _log?.LogInfo("[ServerBrowserFilter] No valid master login - official servers hidden");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ServerBrowserFilter] Error checking token: {ex.Message}");
            }
        }
        
        private string[] GetPossibleTokenPaths()
        {
            var paths = new List<string>();
            
            // AppData location (primary)
            string appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoldfastModding");
            paths.Add(Path.Combine(appDataFolder, LOGIN_TOKEN_FILE));
            
            // BepInEx plugins folder
            string pluginsFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(pluginsFolder))
            {
                paths.Add(Path.Combine(pluginsFolder, LOGIN_TOKEN_FILE));
                paths.Add(Path.Combine(pluginsFolder, "Mods", LOGIN_TOKEN_FILE));
            }
            
            return paths.ToArray();
        }
        
        /// <summary>
        /// Verifies a token is valid for this machine (matches launcher logic)
        /// </summary>
        private bool VerifySecureToken(string token)
        {
            // Check today's token
            if (token == CreateExpectedToken(DateTime.UtcNow))
                return true;
            
            // Check yesterday's token (for timezone issues)
            if (token == CreateExpectedToken(DateTime.UtcNow.AddDays(-1)))
                return true;
            
            return false;
        }
        
        private string CreateExpectedToken(DateTime date)
        {
            string machineId = Environment.MachineName + Environment.UserName;
            string timestamp = date.ToString("yyyyMMdd");
            string tokenData = $"MASTER_ACCESS|{machineId}|{timestamp}";
            
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(tokenData + HASH_SALT));
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
        
        /// <summary>
        /// Public method for patches to check if master logged in
        /// </summary>
        public static bool IsMasterLoggedIn()
        {
            return _isMasterLoggedIn;
        }
        
        /// <summary>
        /// Checks if a server name should be filtered (hidden)
        /// Returns true if the server should be HIDDEN
        /// </summary>
        public static bool ShouldFilterServer(string serverName)
        {
            if (string.IsNullOrEmpty(serverName))
                return false;
            
            // If master logged in, never filter
            if (_isMasterLoggedIn)
                return false;
            
            // Filter official servers
            if (serverName.StartsWith("Anvil Game Studios Official", StringComparison.OrdinalIgnoreCase))
            {
                _log?.LogDebug($"[ServerBrowserFilter] Filtering server: {serverName}");
                return true;
            }
            
            return false;
        }
    }
    
}

