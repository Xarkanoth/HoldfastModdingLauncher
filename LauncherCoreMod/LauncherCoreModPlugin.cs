using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LauncherCoreMod
{
    /// <summary>
    /// Core mod for the Holdfast Modding Launcher
    /// Provides essential features for all launcher users (not just admins)
    /// - Server browser filtering (hides official servers for non-master users)
    /// </summary>
    [BepInPlugin("com.xarkanoth.launchercoremod", "Launcher Core Mod", "1.0.0")]
    public class LauncherCoreModPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        
        private ServerBrowserFilter _serverBrowserFilter;
        
        void Awake()
        {
            Log = Logger;
            Log.LogInfo("Launcher Core Mod loaded!");
            
            // Initialize server browser filter
            _serverBrowserFilter = new ServerBrowserFilter();
            _serverBrowserFilter.Initialize();
        }
        
        void Update()
        {
            _serverBrowserFilter?.OnUpdate();
        }
        
        void OnApplicationQuit()
        {
            _serverBrowserFilter?.Shutdown();
        }
    }
    
    /// <summary>
    /// Filters official "Anvil Game Studios Official" servers from the server browser
    /// unless the user is master logged in via the launcher.
    /// </summary>
    public class ServerBrowserFilter
    {
        private static ManualLogSource _log => LauncherCoreModPlugin.Log;
        private static bool _isMasterLoggedIn = false;
        private static float _lastTokenCheck = 0f;
        private const float TOKEN_CHECK_INTERVAL = 5f;
        
        // Token validation (must match launcher)
        private const string HASH_SALT = "HF_MODDING_2024_XARK";
        private const string LOGIN_TOKEN_FILE = "master_login.token";
        
        // Harmony
        private static Harmony _harmony;
        private static bool _patchesApplied = false;
        
        // UI paths
        private const string SERVER_BROWSER_PATH = "MainCanvas/Main Menu Panels/Panel Container/Play/Server Browser/Server Browser Container/Browser Container/Scroll View/Viewport/Content";
        private const string CUSTOM_SERVER_BUTTON_PATH = "MainCanvas/Main Menu Panels/Panel Container/Play/Server Browser/Server Browser Container/Bottom Buttons Layout/Custom Server Button";
        
        // Tracking
        private static HashSet<int> _hiddenServerInstanceIds = new HashSet<int>();
        private static bool _customServerButtonHidden = false;
        private static float _lastBrowserOpenTime = 0f;
        private static float _lastServerScan = 0f;
        private const float SERVER_SCAN_INTERVAL = 0.5f;
        
        public void Initialize()
        {
            _log?.LogInfo("[ServerBrowserFilter] Initializing...");
            CheckMasterLoginToken();
            ApplyHarmonyPatches();
            _log?.LogInfo($"[ServerBrowserFilter] Ready (Master login: {_isMasterLoggedIn})");
        }
        
        public void Shutdown()
        {
            RemoveHarmonyPatches();
        }
        
        private void ApplyHarmonyPatches()
        {
            if (_patchesApplied) return;
            
            try
            {
                _harmony = new Harmony("com.xarkanoth.launchercoremod.serverbrowserfilter");
                
                // Find HarperServerBrowserItem.Bind method
                Type browserItemType = FindType("HarperServerBrowserItem");
                if (browserItemType == null)
                {
                    _log?.LogWarning("[ServerBrowserFilter] HarperServerBrowserItem not found - using fallback filtering");
                    return;
                }
                
                MethodInfo bindMethod = browserItemType.GetMethod("Bind", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (bindMethod != null)
                {
                    var prefix = typeof(ServerBrowserFilter).GetMethod(nameof(Prefix_OnBind), 
                        BindingFlags.Static | BindingFlags.NonPublic);
                    
                    _harmony.Patch(bindMethod, prefix: new HarmonyMethod(prefix));
                    _patchesApplied = true;
                    _log?.LogInfo("[ServerBrowserFilter] ✓ Harmony patch applied to HarperServerBrowserItem.Bind");
                }
                else
                {
                    _log?.LogWarning("[ServerBrowserFilter] Bind method not found - using fallback filtering");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[ServerBrowserFilter] Failed to apply patches: {ex.Message}");
            }
        }
        
        private void RemoveHarmonyPatches()
        {
            try
            {
                if (_harmony != null && _patchesApplied)
                {
                    _harmony.UnpatchSelf();
                    _patchesApplied = false;
                    _log?.LogInfo("[ServerBrowserFilter] Harmony patches removed");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ServerBrowserFilter] Error removing patches: {ex.Message}");
            }
        }
        
        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
        
        /// <summary>
        /// Harmony PREFIX - SKIP binding official servers entirely
        /// </summary>
        private static bool Prefix_OnBind(object __instance, object serverTarget, int itemIndex)
        {
            try
            {
                if (_isMasterLoggedIn) return true;
                
                // Extract server name from ValueTuple<Int32, String, Int32> - Item2 is server name
                string serverName = ExtractServerNameFromTuple(serverTarget);
                
                if (ShouldFilterServer(serverName))
                {
                    var mb = __instance as MonoBehaviour;
                    if (mb != null)
                    {
                        HideServerItem(mb.transform);
                    }
                    return false; // Skip Bind entirely
                }
                else
                {
                    var mb = __instance as MonoBehaviour;
                    if (mb != null)
                    {
                        ShowServerItem(mb.transform);
                    }
                }
            }
            catch { }
            
            return true;
        }
        
        private static string ExtractServerNameFromTuple(object serverTarget)
        {
            if (serverTarget == null) return null;
            
            try
            {
                var item2Field = serverTarget.GetType().GetField("Item2");
                if (item2Field != null)
                {
                    return item2Field.GetValue(serverTarget) as string;
                }
            }
            catch { }
            
            return null;
        }
        
        private static void HideServerItem(Transform serverItem)
        {
            try
            {
                int instanceId = serverItem.gameObject.GetInstanceID();
                
                CanvasGroup canvasGroup = serverItem.gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = serverItem.gameObject.AddComponent<CanvasGroup>();
                
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                
                LayoutElement layoutElement = serverItem.gameObject.GetComponent<LayoutElement>();
                if (layoutElement == null)
                    layoutElement = serverItem.gameObject.AddComponent<LayoutElement>();
                
                layoutElement.preferredHeight = 0f;
                layoutElement.minHeight = 0f;
                layoutElement.flexibleHeight = 0f;
                layoutElement.ignoreLayout = true;
                
                serverItem.localScale = Vector3.zero;
                
                _hiddenServerInstanceIds.Add(instanceId);
            }
            catch { }
        }
        
        private static void ShowServerItem(Transform serverItem)
        {
            try
            {
                int instanceId = serverItem.gameObject.GetInstanceID();
                
                if (!_hiddenServerInstanceIds.Contains(instanceId)) return;
                
                CanvasGroup canvasGroup = serverItem.gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f;
                    canvasGroup.blocksRaycasts = true;
                    canvasGroup.interactable = true;
                }
                
                LayoutElement layoutElement = serverItem.gameObject.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredHeight = -1f;
                    layoutElement.minHeight = -1f;
                    layoutElement.flexibleHeight = -1f;
                    layoutElement.ignoreLayout = false;
                }
                
                serverItem.localScale = Vector3.one;
                
                _hiddenServerInstanceIds.Remove(instanceId);
            }
            catch { }
        }
        
        public void OnUpdate()
        {
            // Periodic token check
            if (Time.time - _lastTokenCheck > TOKEN_CHECK_INTERVAL)
            {
                _lastTokenCheck = Time.time;
                CheckMasterLoginToken();
            }
            
            if (_isMasterLoggedIn) return;
            
            // Fallback UI filtering
            if (Time.time - _lastServerScan > SERVER_SCAN_INTERVAL)
            {
                _lastServerScan = Time.time;
                FallbackFilterServerBrowser();
            }
        }
        
        private void FallbackFilterServerBrowser()
        {
            try
            {
                GameObject contentObj = GameObject.Find(SERVER_BROWSER_PATH);
                if (contentObj == null)
                {
                    if (_hiddenServerInstanceIds.Count > 0)
                    {
                        _hiddenServerInstanceIds.Clear();
                        _customServerButtonHidden = false;
                        _lastBrowserOpenTime = 0f;
                    }
                    return;
                }
                
                if (_lastBrowserOpenTime == 0f)
                {
                    _lastBrowserOpenTime = Time.time;
                    _hiddenServerInstanceIds.Clear();
                    _customServerButtonHidden = false;
                }
                
                // Hide Custom Server Button
                if (!_customServerButtonHidden)
                {
                    GameObject customBtn = GameObject.Find(CUSTOM_SERVER_BUTTON_PATH);
                    if (customBtn != null)
                    {
                        customBtn.SetActive(false);
                        _customServerButtonHidden = true;
                        _log?.LogInfo("[ServerBrowserFilter] Hidden Custom Server Button");
                    }
                }
            }
            catch { }
        }
        
        private void CheckMasterLoginToken()
        {
            bool wasLoggedIn = _isMasterLoggedIn;
            _isMasterLoggedIn = false;
            
            try
            {
                foreach (string tokenPath in GetPossibleTokenPaths())
                {
                    if (File.Exists(tokenPath))
                    {
                        string token = File.ReadAllText(tokenPath).Trim();
                        if (VerifySecureToken(token))
                        {
                            _isMasterLoggedIn = true;
                            break;
                        }
                    }
                }
                
                if (_isMasterLoggedIn != wasLoggedIn)
                {
                    _log?.LogInfo(_isMasterLoggedIn 
                        ? "[ServerBrowserFilter] Master login detected - official servers visible"
                        : "[ServerBrowserFilter] No master login - official servers hidden");
                }
            }
            catch { }
        }
        
        private string[] GetPossibleTokenPaths()
        {
            var paths = new List<string>();
            
            // AppData location
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
        
        private bool VerifySecureToken(string token)
        {
            if (token == "MASTER_ACCESS_GRANTED") return true;
            if (token == CreateExpectedToken(DateTime.UtcNow)) return true;
            if (token == CreateExpectedToken(DateTime.UtcNow.AddDays(-1))) return true;
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
        
        public static bool ShouldFilterServer(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return false;
            if (_isMasterLoggedIn) return false;
            
            return serverName.StartsWith("Anvil Game Studios Official", StringComparison.OrdinalIgnoreCase);
        }
        
        public static bool IsMasterLoggedIn() => _isMasterLoggedIn;
    }
}

