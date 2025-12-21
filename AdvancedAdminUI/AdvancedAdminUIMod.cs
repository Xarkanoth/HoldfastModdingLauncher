using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using AdvancedAdminUI.Features;
using AdvancedAdminUI.Utils;

namespace AdvancedAdminUI
{
    /// <summary>
    /// Separate runner MonoBehaviour that lives on its own GameObject.
    /// This ensures the mod keeps running even if BepInEx_Manager gets deactivated.
    /// Uses static AdvancedAdminUIMod.Instance to avoid Unity's "fake null" on disabled MonoBehaviours.
    /// </summary>
    public class PluginRunner : MonoBehaviour
    {
        private float _startTime;
        private bool _hasLoggedFirstUpdate = false;
        
        public void Initialize()
        {
            _startTime = Time.realtimeSinceStartup;
            AdvancedAdminUIMod.Log?.LogInfo("[PluginRunner] Initialized - will use static Instance reference");
        }
        
        void Start()
        {
            var instance = AdvancedAdminUIMod.GetInstanceSafe();
            AdvancedAdminUIMod.Log?.LogInfo($"[PluginRunner] Start() called - GetInstanceSafe()={(!ReferenceEquals(instance, null) ? "valid" : "null")}");
        }
        
        void OnEnable()
        {
            AdvancedAdminUIMod.Log?.LogInfo("[PluginRunner] OnEnable - Runner is ACTIVE");
        }
        
        void OnDisable()
        {
            AdvancedAdminUIMod.Log?.LogInfo("[PluginRunner] OnDisable - Runner was DISABLED!");
        }
        
        void OnDestroy()
        {
            AdvancedAdminUIMod.Log?.LogInfo("[PluginRunner] OnDestroy - Runner is being DESTROYED!");
        }
        
        void Update()
        {
            // Log once to confirm Update is running
            if (!_hasLoggedFirstUpdate && Time.realtimeSinceStartup - _startTime > 1f)
            {
                _hasLoggedFirstUpdate = true;
                AdvancedAdminUIMod.Log?.LogInfo("[PluginRunner] First Update() confirmed - calling DoUpdate via GetInstanceSafe()");
            }
            
            // Use GetInstanceSafe() + ReferenceEquals to fully bypass Unity's "fake null"
            var instance = AdvancedAdminUIMod.GetInstanceSafe();
            if (!ReferenceEquals(instance, null))
            {
                try
                {
                    instance.DoUpdate();
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log?.LogError($"[PluginRunner] Exception in DoUpdate: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
        
        void OnGUI()
        {
            var instance = AdvancedAdminUIMod.GetInstanceSafe();
            if (!ReferenceEquals(instance, null))
            {
                try
                {
                    instance.DoOnGUI();
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log?.LogError($"[PluginRunner] Exception in DoOnGUI: {ex.Message}");
                }
            }
        }
        
        void OnApplicationQuit()
        {
            var instance = AdvancedAdminUIMod.GetInstanceSafe();
            if (!ReferenceEquals(instance, null))
            {
                instance.DoOnApplicationQuit();
            }
        }
    }

    [BepInPlugin("com.xarkanoth.advancedadminui", "Advanced Admin UI", "1.0.24")]
    public class AdvancedAdminUIMod : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static AdvancedAdminUIMod Instance { get; private set; }
        
        // Store as object to bypass Unity's == operator override on MonoBehaviour
        // Unity's "fake null" check makes disabled MonoBehaviours appear null
        private static object _instanceRef;
        
        /// <summary>
        /// Gets the instance without Unity's fake-null check.
        /// Use this from the runner to avoid issues when BepInEx_Manager is deactivated.
        /// </summary>
        public static AdvancedAdminUIMod GetInstanceSafe()
        {
            return _instanceRef as AdvancedAdminUIMod;
        }
        
        private readonly Dictionary<string, IAdminFeature> _features = new Dictionary<string, IAdminFeature>();
        private readonly object _featuresLock = new object();
        private CavalryVisualizationFeature _cavalryFeature;
        private RamboIndicatorFeature _ramboFeature;
        private AFKIndicatorFeature _afkFeature;
        private MinimapFeature _minimapFeature;
        private TeleportFeature _teleportFeature;
        private AdminUIFeature _uiFeature;
        private ServerBrowserFilterFeature _serverFilterFeature;
        
        // Hot reload support (manual only via F11)
        private string _dllPath;
        private bool _isReloading = false;
        
        // Master shutdown - completely disables all mod processing
        private static bool _modActive = true;
        public static bool IsModActive => _modActive;
        
        // Global cleanup support
        private static AdvancedAdminUIMod _instance;
        private float _lastCleanupRequest = 0f;
        
        // Registration tracking
        private float _lastRegistrationAttempt = 0f;
        private bool _scriptModRegistered = false;
        
        // RC login message tracking - only show once
        private bool _hasShownRCLoginMessage = false;
        
        // Separate runner GameObject to ensure mod keeps running
        private GameObject _runnerObject;
        private PluginRunner _runner;

        void Awake()
        {
            Log = Logger;
            Instance = this;
            _instance = this;
            _instanceRef = this; // Store as object to bypass Unity's fake-null check
            
            // Initialize colored console output
            ColoredLogger.Initialize(Log);
            
            ColoredLogger.Log(ColoredLogger.BrightCyan, "═══════════════════════════════════════════");
            ColoredLogger.Log(ColoredLogger.BrightCyan, "       Advanced Admin UI v1.0.0");
            ColoredLogger.Log(ColoredLogger.BrightCyan, "═══════════════════════════════════════════");
            
            try
            {
                // Create a separate persistent GameObject for running Update/OnGUI
                // This ensures the mod keeps running even if BepInEx_Manager gets deactivated
                _runnerObject = new GameObject("AdvancedAdminUI_Runner");
                _runnerObject.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(_runnerObject);
                _runner = _runnerObject.AddComponent<PluginRunner>();
                _runner.Initialize();
                
                Log.LogInfo("[Awake] Created persistent runner GameObject");
                
                // Get DLL path for hot reload
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                _dllPath = !string.IsNullOrEmpty(assemblyLocation) ? assemblyLocation : "AdvancedAdminUI.dll";
                
                // Initialize PlayerEventManager to receive events
                AdvancedAdminUI.Utils.PlayerEventManager.Initialize();
                
                // Register as a Holdfast script mod
                AdvancedAdminUI.Utils.HoldfastScriptMod.Register();
                
                InitializeFeatures();
            }
            catch (Exception ex)
            {
                Log.LogError($"[Awake] Exception during initialization: {ex}");
            }
        }
        
        void Start()
        {
            Log.LogInfo($"[Start] Called! GameObject: {gameObject.name}, Active: {gameObject.activeInHierarchy}, Enabled: {enabled}");
        }
        
        void OnEnable()
        {
            Log.LogInfo("[OnEnable] Plugin enabled!");
        }
        
        void OnDisable()
        {
            Log.LogInfo("[OnDisable] Plugin component disabled (BepInEx_Manager may be inactive)");
            Log.LogInfo("[OnDisable] Runner GameObject should keep mod running independently");
        }
        
        /// <summary>
        /// Resets the RC login prompt flag so it can show again (called on disconnect)
        /// </summary>
        public void ResetRCLoginPromptFlag()
        {
            _hasShownRCLoginMessage = false;
        }
        
        /// <summary>
        /// Completely shuts down the mod - no processing, no logging, no resource usage
        /// </summary>
        public static void ShutdownMod()
        {
            if (!_modActive)
                return;
                
            _modActive = false;
            Log.LogInfo("[Advanced Admin UI] ═══ MOD SHUTDOWN ═══");
            
            if (_instance != null)
            {
                // Disable all features and clean up resources
                List<IAdminFeature> featuresSnapshot;
                lock (_instance._featuresLock)
                {
                    featuresSnapshot = new List<IAdminFeature>(_instance._features.Values);
                }
                
                foreach (var feature in featuresSnapshot)
                {
                    try
                    {
                        if (feature.IsEnabled)
                            feature.Disable();
                        feature.OnApplicationQuit();
                    }
                    catch { }
                }
                
                // Clean up UI
                if (_instance._uiFeature != null)
                {
                    try
                    {
                        if (_instance._uiFeature.IsEnabled)
                            _instance._uiFeature.Disable();
                        _instance._uiFeature.OnApplicationQuit();
                    }
                    catch { }
                }
            }
            
            Log.LogInfo("[Advanced Admin UI] Mod is now INACTIVE. Press F12 to reactivate.");
        }
        
        /// <summary>
        /// Reactivates the mod after shutdown
        /// </summary>
        public static void ActivateMod()
        {
            if (_modActive)
                return;
                
            _modActive = true;
            Log.LogInfo("[Advanced Admin UI] ═══ MOD ACTIVATED ═══");
            
            if (_instance != null)
            {
                // Re-enable features
                if (_instance._cavalryFeature != null)
                    _instance._cavalryFeature.Enable();
                if (_instance._ramboFeature != null)
                    _instance._ramboFeature.Enable();
                if (_instance._afkFeature != null)
                    _instance._afkFeature.Enable();
                if (_instance._minimapFeature != null)
                    _instance._minimapFeature.Enable();
                if (_instance._uiFeature != null)
                    _instance._uiFeature.Enable();
            }
            
            Log.LogInfo("[Advanced Admin UI] Mod is now ACTIVE.");
        }
        
        /// <summary>
        /// Called by PlayerTracker when all players are lost (map change, etc.)
        /// Triggers cleanup of all visual GameObjects across all features
        /// </summary>
        public static void RequestGlobalCleanup()
        {
            if (_instance == null)
                return;
            
            // Prevent multiple cleanup requests in quick succession
            if (Time.time - _instance._lastCleanupRequest < 2.0f)
                return;
            
            _instance._lastCleanupRequest = Time.time;
            _instance.PerformGlobalCleanup();
        }
        
        private void PerformGlobalCleanup()
        {
            List<IAdminFeature> featuresSnapshot;
            lock (_featuresLock)
            {
                featuresSnapshot = new List<IAdminFeature>(_features.Values);
            }
            
            // Clean up each feature
            foreach (var feature in featuresSnapshot)
            {
                try
                {
                    if (feature is RamboIndicatorFeature rambo)
                    {
                        rambo.CleanupAllVisualObjects();
                    }
                    else if (feature is AFKIndicatorFeature afk)
                    {
                        afk.CleanupAllVisualObjects();
                    }
                    else if (feature is CavalryVisualizationFeature cavalry)
                    {
                        cavalry.CleanupAllRings();
                    }
                    else if (feature is MinimapFeature minimap)
                    {
                        minimap.OnNewRound();
                    }
                }
                catch { }
            }
            
            // Reset script mod registration flag so it will re-register if needed
            _scriptModRegistered = false;
        }
        
        private void InitializeFeatures()
        {
            // Initialize persistent player tracking (always active) - only on first init
            // Note: PlayerTracker.Initialize() has a guard, so it's safe to call multiple times
            PlayerTracker.Initialize();
            
            // Register features
            _cavalryFeature = new CavalryVisualizationFeature();
            RegisterFeature("cavalry", _cavalryFeature);
            
            _ramboFeature = new RamboIndicatorFeature();
            RegisterFeature("rambo", _ramboFeature);
            
            _afkFeature = new AFKIndicatorFeature();
            RegisterFeature("afk", _afkFeature);
            
            _minimapFeature = new MinimapFeature();
            RegisterFeature("minimap", _minimapFeature);
            
            _teleportFeature = new TeleportFeature();
            RegisterFeature("teleport", _teleportFeature);
            
            // Server browser filter - hides official servers unless master logged in
            // This runs even without RC login to protect official servers
            _serverFilterFeature = new ServerBrowserFilterFeature();
            RegisterFeature("serverfilter", _serverFilterFeature);
            
            // Register UI feature (always enabled, controls visibility separately)
            _uiFeature = new AdminUIFeature();
            _uiFeature.SetFeatures(_features);
            _uiFeature.Enable();
            
            // Enable server filter first (protects official servers, runs without RC login)
            _serverFilterFeature.Enable();
            
            // Enable all features by default
            _cavalryFeature.Enable();
            _ramboFeature.Enable();
            _afkFeature.Enable();
            _minimapFeature.Enable();
            _teleportFeature.Enable();
            
            Log.LogInfo($"Loaded {_features.Count} features | F3=UI | F9=Rescan Players | F11=Reload | F12=Toggle Mod");
        }
        
        // File watcher disabled - only manual reload via F11
        
        private void ReloadMod()
        {
            if (_isReloading)
                return;
                
            _isReloading = true;
            StartCoroutine(ReloadModCoroutine());
        }
        
        private IEnumerator ReloadModCoroutine()
        {
            Log.LogInfo("[Hot Reload] Reloading...");
            
            // CRITICAL: Clear all event callbacks FIRST to prevent duplicate registrations
            PlayerEventManager.ClearAllCallbacks();
            
            // Disable all features and clean up
            List<IAdminFeature> featuresSnapshot;
            lock (_featuresLock)
            {
                featuresSnapshot = new List<IAdminFeature>(_features.Values);
            }
            
            foreach (var feature in featuresSnapshot)
            {
                try
                {
                    if (feature.IsEnabled)
                        feature.Disable();
                    feature.OnApplicationQuit();
                }
                catch { }
            }
            
            if (_uiFeature != null)
            {
                try
                {
                    if (_uiFeature.IsEnabled)
                        _uiFeature.Disable();
                    _uiFeature.OnApplicationQuit();
                }
                catch { }
            }
            
            // Clear all feature references
            _cavalryFeature = null;
            _ramboFeature = null;
            _afkFeature = null;
            _minimapFeature = null;
            _teleportFeature = null;
            _uiFeature = null;
            lock (_featuresLock)
            {
                _features.Clear();
            }
            
            // Small delay to ensure cleanup completes (non-blocking)
            yield return new WaitForSeconds(0.1f);
            
            // Force HoldfastScriptMod to re-register with the game
            HoldfastScriptMod.ForceReRegister();
            _scriptModRegistered = false; // Reset so Update will retry registration
            
            // Reinitialize features (creates new instances)
            InitializeFeatures();
            
            Log.LogInfo("[Hot Reload] Complete");
            
            _isReloading = false;
        }

        /// <summary>
        /// Called by PluginRunner from its own persistent GameObject
        /// </summary>
        public void DoUpdate()
        {
            // F12 toggles mod on/off - always check this even when inactive
            if (Input.GetKeyDown(KeyCode.F12))
            {
                if (_modActive)
                    ShutdownMod();
                else
                    ActivateMod();
                return;
            }
            
            // If mod is inactive, do nothing else
            if (!_modActive)
                return;
            
            // Check for re-registration flag (set when changing servers)
            // This should be checked more frequently than the periodic check
            if (AdvancedAdminUI.Utils.HoldfastScriptMod.NeedsReRegistration())
            {
                _scriptModRegistered = false;
                Log.LogInfo("[AdvancedAdminUI] Re-registration requested (server change detected)");
            }
            
            // Check registration status periodically (every 5 seconds)
            // This must run even without RC login so we can receive the OnRCLogin event
            if (Time.time - _lastRegistrationAttempt > 5.0f)
            {
                _lastRegistrationAttempt = Time.time;
                
                // If we think we're registered, verify we're still in the list
                if (_scriptModRegistered)
                {
                    if (!AdvancedAdminUI.Utils.HoldfastScriptMod.IsStillRegistered())
                    {
                        _scriptModRegistered = false;
                        Log.LogInfo("[AdvancedAdminUI] Registration check failed - will re-register");
                    }
                }
                
                // Try to register if not registered
                if (!_scriptModRegistered)
                {
                    _scriptModRegistered = AdvancedAdminUI.Utils.HoldfastScriptMod.Register();
                    if (_scriptModRegistered)
                    {
                        Log.LogInfo("[AdvancedAdminUI] Successfully (re)registered with game");
                    }
                }
            }
            
            // Server browser filter runs WITHOUT RC login requirement
            // This ensures official servers are always filtered for non-master users
            if (_serverFilterFeature != null && _serverFilterFeature.IsEnabled)
            {
                try
                {
                    _serverFilterFeature.OnUpdate();
                }
                catch { }
            }
            
            // F3 to open UI - show message once if not RC logged in
            if (Input.GetKeyDown(KeyCode.F3))
            {
                if (!HoldfastScriptMod.IsRCLoggedIn() && !_hasShownRCLoginMessage)
                {
                    _hasShownRCLoginMessage = true;
                    Log.LogInfo("[Advanced Admin UI] RC login required. Type 'rc login <password>' in console.");
                }
            }
            
            // Everything below requires RC login
            if (!HoldfastScriptMod.IsRCLoggedIn())
                return;
            
            // Reset the message flag once logged in so it can show again if they disconnect
            _hasShownRCLoginMessage = false;

            // Check for manual reload (F11) - only manual reload, no automatic file watching
            if (Input.GetKeyDown(KeyCode.F11) && !_isReloading)
            {
                ReloadMod();
            }
            
            // F9 = Rescan for existing players (use when joining late or after hot reload)
            if (Input.GetKeyDown(KeyCode.F9))
            {
                PlayerTracker.RescanForExistingPlayers();
            }
            
            // Always update player tracker (regardless of feature state)
            PlayerTracker.Update();

            // Update UI feature (handles F3 toggle internally)
            if (_uiFeature != null)
            {
                try
                {
                    _uiFeature.OnUpdate();
                }
                catch { }
            }

            // Update all enabled features (create snapshot to avoid modification during enumeration)
            List<IAdminFeature> featuresSnapshot;
            lock (_featuresLock)
            {
                featuresSnapshot = new List<IAdminFeature>(_features.Values);
            }
            
            foreach (var feature in featuresSnapshot)
            {
                if (feature.IsEnabled)
                {
                    try
                    {
                        feature.OnUpdate();
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Called by PluginRunner from its own persistent GameObject
        /// </summary>
        public void DoOnGUI()
        {
            // If mod is inactive or not RC logged in, do nothing
            if (!_modActive || !HoldfastScriptMod.IsRCLoggedIn())
                return;
            
            // Draw UI feature first (always enabled, controls its own visibility)
            if (_uiFeature != null)
            {
                try
                {
                    _uiFeature.OnGUI();
                }
                catch { }
            }
            
            // Forward GUI calls to enabled features (create snapshot to avoid modification during enumeration)
            List<IAdminFeature> featuresSnapshot;
            lock (_featuresLock)
            {
                featuresSnapshot = new List<IAdminFeature>(_features.Values);
            }
            
            foreach (var feature in featuresSnapshot)
            {
                if (feature.IsEnabled)
                {
                    try
                    {
                        feature.OnGUI();
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Called by PluginRunner from its own persistent GameObject
        /// </summary>
        public void DoOnApplicationQuit()
        {
            List<IAdminFeature> featuresSnapshot;
            lock (_featuresLock)
            {
                featuresSnapshot = new List<IAdminFeature>(_features.Values);
            }
            
            foreach (var feature in featuresSnapshot)
            {
                try
                {
                    feature.OnApplicationQuit();
                }
                catch { }
            }
            
            // Unhook from Unity log messages
            ColoredLogger.Unhook();
            
            // Clean up the runner
            if (_runnerObject != null)
            {
                Destroy(_runnerObject);
                _runnerObject = null;
                _runner = null;
            }
        }

        private void RegisterFeature(string key, IAdminFeature feature)
        {
            if (feature == null)
                return;
            
            lock (_featuresLock)
            {
                _features[key] = feature;
            }
        }

        private void ShowFeatureList()
        {
            Log.LogInfo("=== Advanced Admin UI Features ===");
            lock (_featuresLock)
            {
                foreach (var kvp in _features)
                {
                    string status = kvp.Value.IsEnabled ? "ENABLED" : "DISABLED";
                    Log.LogInfo($"  [{kvp.Key}] {kvp.Value.FeatureName}: {status}");
                }
            }
            Log.LogInfo("=====================================");
        }

    }
}

