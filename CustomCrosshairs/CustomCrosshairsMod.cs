using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HoldfastSharedMethods;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CustomCrosshairs
{
    /// <summary>
    /// Dedicated runner MonoBehaviour to ensure reliable Update() calls.
    /// BepInEx's BaseUnityPlugin.Update() is unreliable in some scenarios.
    /// </summary>
    public class CustomCrosshairsRunner : MonoBehaviour
    {
        private bool _firstUpdate = true;
        
        void Update()
        {
            if (_firstUpdate)
            {
                _firstUpdate = false;
                CustomCrosshairsMod.Log?.LogInfo("[CustomCrosshairsRunner] First Update() call!");
            }
            CustomCrosshairsMod.DoUpdate();
        }
    }
    
    /// <summary>
    /// Attached directly to the main camera. OnPostRender is guaranteed to fire
    /// on MonoBehaviours attached to a Camera, unlike Camera.onPostRender (static)
    /// or OnRenderObject (requires renderer).
    /// </summary>
    public class TrajectoryRenderer : MonoBehaviour
    {
        private bool _firstRender = true;
        
        void OnPostRender()
        {
            if (_firstRender)
            {
                _firstRender = false;
                CustomCrosshairsMod.Log?.LogInfo("[TrajectoryRenderer] First OnPostRender() call on camera!");
            }
            CustomCrosshairsMod.DoRenderTrajectory(GetComponent<Camera>());
        }
    }
    
    [BepInPlugin("com.xarkanoth.customcrosshairs", "Custom Crosshairs", "1.0.40")]
    [BepInDependency("com.xarkanoth.launchercoremod", BepInDependency.DependencyFlags.HardDependency)]
    public class CustomCrosshairsMod : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        private static CustomCrosshairsMod _instance;
        
        // Crosshair paths
        private const string CROSSHAIR_PANEL_PATH = "Main Canvas/Game Elements Panel/Crosshair Panel";
        private const string MUSKET_CROSSHAIR_NAME = "Musket Crosshair";
        private const string BLUNDERBUSS_CROSSHAIR_NAME = "Blunderbuss Crosshair";
        private const string PISTOL_CROSSHAIR_NAME = "Pistol Crosshair";
        private const string RIFLE_CROSSHAIR_NAME = "Rifle Crosshair";
        private const string CUSTOM_CROSSHAIR_NAME = "Custom Crosshair";
        
        // Crosshair image component name
        private const string CROSSHAIR_IMAGE_NAME = "Crosshair Image";
        
        // Configuration
        private CrosshairConfig _config;
        private string _configPath;
        private string _crosshairsFolder;
        private Dictionary<string, Sprite> _loadedSprites = new Dictionary<string, Sprite>();
        private Dictionary<string, Image> _crosshairImages = new Dictionary<string, Image>();
        private Dictionary<string, Text> _rangefinderTexts = new Dictionary<string, Text>();
        
        // Master login
        private bool _isMasterLoggedIn = false;
        
        // Rangefinder
        private Camera _mainCamera;
        private float _lastRaycastTime = 0f;
        private const float RAYCAST_INTERVAL = 0.1f;
        private float _currentDistance = 0f;
        private int _raycastLayerMask = -1;
        
        // Trajectory line rendering
        private Material _trajectoryMaterial;
        private const int TRAJECTORY_SEGMENTS = 60;
        private const float TRAJECTORY_MAX_RANGE = 500f;
        private const float TRAJECTORY_START_OFFSET = 5f;
        // Per-shot values are randomized by the game:
        //   Velocity: ~267-345 m/s, Gravity: ~15.2-16.7
        // Using midpoints for best average prediction
        private float _muzzleVelocity = 305f;
        private float _bulletGravity = 15.9f;
        private bool _trajectoryLoggedOnce = false;
        private bool _trajectoryHooked = false;
        
        // Enemy tracking for aim guidance
        private Dictionary<int, TrackedPlayer> _trackedPlayers = new Dictionary<int, TrackedPlayer>();
        private FactionCountry _localFaction = 0;
        private int _localPlayerId = -1;
        private const float AIM_CONE_DEGREES = 10f;
        private const float AIM_CONE_COS = 0.9848f; // cos(10 degrees) precomputed
        private const float MIN_DROP_DISPLAY = 0.3f;
        
        // State tracking
        private bool _isInGame = false;
        private bool _hasSpawned = false;
        private bool _crosshairsReplaced = false;
        private bool _rangefinderCreated = false;
        private float _setupDelayTimer = 0f;
        private const float SETUP_DELAY_AFTER_SPAWN = 0.5f;
        private static bool _runnerCreated = false;
        
        private class TrackedPlayer
        {
            public int PlayerId;
            public FactionCountry Faction;
            public GameObject PlayerObject;
        }
        
        void Awake()
        {
            _instance = this;
            Log = Logger;
            Log.LogInfo("Custom Crosshairs mod loaded!");
            
            // Set up paths
            string pluginFolder = Path.GetDirectoryName(Info.Location);
            string bepInExFolder = Path.GetDirectoryName(pluginFolder);
            
            // Config is stored in BepInEx/config
            _configPath = Path.Combine(bepInExFolder, "config", "com.xarkanoth.customcrosshairs.json");
            
            // Crosshairs are stored in BepInEx/CustomCrosshairs (downloaded by launcher)
            _crosshairsFolder = Path.Combine(bepInExFolder, "CustomCrosshairs");
            
            if (!Directory.Exists(_crosshairsFolder))
            {
                Directory.CreateDirectory(_crosshairsFolder);
            }
            
            // Load configuration
            LoadConfig();
            
            // Subscribe to game events from LauncherCoreMod
            SubscribeToGameEvents();
            
            // Defer runner creation until first scene loads (creating during chainloader is too early)
            SceneManager.sceneLoaded += OnSceneLoadedCreateRunner;
            Log.LogInfo("[CustomCrosshairs] Subscribed to SceneManager.sceneLoaded - will create runner after first scene loads");
        }
        
        private void OnSceneLoadedCreateRunner(Scene scene, LoadSceneMode mode)
        {
            if (_runnerCreated) return;
            _runnerCreated = true;
            
            var go = new GameObject("CustomCrosshairsRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CustomCrosshairsRunner>();
            Log.LogInfo($"[CustomCrosshairs] Runner created on scene '{scene.name}' and marked DontDestroyOnLoad");
        }
        
        private void SubscribeToGameEvents()
        {
            try
            {
                // Use reflection to subscribe to LauncherCoreMod's events
                // This avoids a hard compile-time dependency while still using the event system
                var coreModAssembly = System.Reflection.Assembly.Load("LauncherCoreMod");
                if (coreModAssembly == null)
                {
                    Log.LogWarning("[CustomCrosshairs] LauncherCoreMod assembly not found - will use fallback polling");
                    return;
                }
                
                var gameEventsType = coreModAssembly.GetType("LauncherCoreMod.GameEvents");
                if (gameEventsType == null)
                {
                    Log.LogWarning("[CustomCrosshairs] GameEvents type not found - will use fallback polling");
                    return;
                }
                
                // Subscribe to OnConnectedToServer
                var connectedEvent = gameEventsType.GetEvent("OnConnectedToServer");
                if (connectedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        connectedEvent.EventHandlerType,
                        this,
                        typeof(CustomCrosshairsMod).GetMethod(nameof(HandleConnectedToServer), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    connectedEvent.AddEventHandler(null, handler);
                }
                
                // Subscribe to OnDisconnectedFromServer
                var disconnectedEvent = gameEventsType.GetEvent("OnDisconnectedFromServer");
                if (disconnectedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        disconnectedEvent.EventHandlerType,
                        this,
                        typeof(CustomCrosshairsMod).GetMethod(nameof(HandleDisconnectedFromServer), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    disconnectedEvent.AddEventHandler(null, handler);
                }
                
                // Subscribe to OnLocalPlayerSpawned
                var spawnedEvent = gameEventsType.GetEvent("OnLocalPlayerSpawned");
                if (spawnedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        spawnedEvent.EventHandlerType,
                        this,
                        typeof(CustomCrosshairsMod).GetMethod(nameof(HandleLocalPlayerSpawned), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    spawnedEvent.AddEventHandler(null, handler);
                }
                
                // Also subscribe to general OnPlayerSpawned as fallback
                var generalSpawnedEvent = gameEventsType.GetEvent("OnPlayerSpawned");
                if (generalSpawnedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        generalSpawnedEvent.EventHandlerType,
                        this,
                        typeof(CustomCrosshairsMod).GetMethod(nameof(HandlePlayerSpawned), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    generalSpawnedEvent.AddEventHandler(null, handler);
                }
                
                // Check master login status
                var masterLoginType = coreModAssembly.GetType("LauncherCoreMod.MasterLoginManager");
                if (masterLoginType != null)
                {
                    var isMasterLoggedInMethod = masterLoginType.GetMethod("IsMasterLoggedIn", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (isMasterLoggedInMethod != null)
                    {
                        _isMasterLoggedIn = (bool)isMasterLoggedInMethod.Invoke(null, null);
                    }
                }
                
                Log.LogInfo($"[CustomCrosshairs] Subscribed to game events. Master login: {_isMasterLoggedIn}, RangefinderEnabled: {_config?.RangefinderEnabled}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[CustomCrosshairs] Error subscribing to events: {ex.Message} - will use fallback polling");
            }
        }
        
        // Event handlers
        private void HandleConnectedToServer(ulong steamId)
        {
            Log.LogInfo($"[CustomCrosshairs] ═══════════════════════════════════════════");
            Log.LogInfo($"[CustomCrosshairs] ★ EVENT: Connected to server!");
            Log.LogInfo($"[CustomCrosshairs]   Steam ID: {steamId}");
            Log.LogInfo($"[CustomCrosshairs] ═══════════════════════════════════════════");
            _isInGame = true;
            _hasSpawned = false;
            ResetState();
        }
        
        private void HandleDisconnectedFromServer()
        {
            Log.LogInfo("[CustomCrosshairs] EVENT: Disconnected from server - resetting state");
            _isInGame = false;
            _hasSpawned = false;
            ResetState();
        }
        
        private void HandleLocalPlayerSpawned(int playerId, FactionCountry faction, PlayerClass playerClass)
        {
            Log.LogInfo($"[CustomCrosshairs] ═══════════════════════════════════════════");
            Log.LogInfo($"[CustomCrosshairs] ★ EVENT: Local player spawned!");
            Log.LogInfo($"[CustomCrosshairs]   Player ID: {playerId}");
            Log.LogInfo($"[CustomCrosshairs]   Class: {playerClass}");
            Log.LogInfo($"[CustomCrosshairs]   Faction: {faction}");
            Log.LogInfo($"[CustomCrosshairs]   → Scheduling rangefinder setup in {SETUP_DELAY_AFTER_SPAWN}s");
            Log.LogInfo($"[CustomCrosshairs] ═══════════════════════════════════════════");
            _localPlayerId = playerId;
            _localFaction = faction;
            _hasSpawned = true;
            _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
            
            // Reset crosshair/rangefinder state so they get recreated
            _crosshairsReplaced = false;
            _rangefinderCreated = false;
        }
        
        // Handles ALL player spawns - tracks positions for aim guidance and local player fallback
        private void HandlePlayerSpawned(int playerId, int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            // Track all spawned players for aim guidance (master-only feature)
            if (playerObject != null && _isMasterLoggedIn)
            {
                _trackedPlayers[playerId] = new TrackedPlayer
                {
                    PlayerId = playerId,
                    Faction = faction,
                    PlayerObject = playerObject
                };
            }
            
            // If we haven't spawned yet and this spawn has a playerObject (likely us), trigger setup
            if (!_hasSpawned && playerObject != null)
            {
                Log.LogInfo($"[CustomCrosshairs] ═══════════════════════════════════════════");
                Log.LogInfo($"[CustomCrosshairs] ★ EVENT: Player spawned (fallback trigger)!");
                Log.LogInfo($"[CustomCrosshairs]   Player ID: {playerId}");
                Log.LogInfo($"[CustomCrosshairs]   Class: {playerClass}");
                Log.LogInfo($"[CustomCrosshairs]   → Scheduling rangefinder setup in {SETUP_DELAY_AFTER_SPAWN}s");
                Log.LogInfo($"[CustomCrosshairs] ═══════════════════════════════════════════");
                _localFaction = faction;
                _localPlayerId = playerId;
                _hasSpawned = true;
                _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
                _crosshairsReplaced = false;
                _rangefinderCreated = false;
            }
        }
        
        private void ResetState()
        {
            _crosshairsReplaced = false;
            _rangefinderCreated = false;
            _crosshairImages.Clear();
            
            // Destroy tracked rangefinder text GameObjects before clearing refs
            foreach (var kvp in _rangefinderTexts)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value.gameObject);
                }
            }
            _rangefinderTexts.Clear();
            _trackedPlayers.Clear();
            _localPlayerId = -1;
            
            _mainCamera = null;
            _trajectoryLoggedOnce = false;
            UnhookTrajectoryRenderer();
        }
        
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = ParseConfig(json);
                    Log.LogInfo($"Loaded config: CrosshairId={_config?.SelectedCrosshairId}, Enabled={_config?.Enabled}, RangefinderEnabled={_config?.RangefinderEnabled}");
                }
                else
                {
                    _config = new CrosshairConfig
                    {
                        SelectedCrosshairId = "default",
                        Enabled = true,
                        RangefinderEnabled = true
                    };
                    Log.LogInfo("No config file found, using defaults");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error loading config: {ex.Message}");
                _config = new CrosshairConfig
                {
                    SelectedCrosshairId = "default",
                    Enabled = true,
                    RangefinderEnabled = true
                };
            }
        }
        
        private CrosshairConfig ParseConfig(string json)
        {
            var config = new CrosshairConfig();
            
            try
            {
                // Simple JSON parsing without external library
                if (json.Contains("\"SelectedCrosshairId\""))
                {
                    int startIndex = json.IndexOf("\"SelectedCrosshairId\"") + "\"SelectedCrosshairId\"".Length;
                    startIndex = json.IndexOf("\"", startIndex) + 1;
                    int endIndex = json.IndexOf("\"", startIndex);
                    config.SelectedCrosshairId = json.Substring(startIndex, endIndex - startIndex);
                }
                
                if (json.Contains("\"Enabled\""))
                {
                    config.Enabled = json.ToLower().Contains("\"enabled\": true") || 
                                    json.ToLower().Contains("\"enabled\":true");
                }
                
                if (json.Contains("\"RangefinderEnabled\""))
                {
                    config.RangefinderEnabled = json.ToLower().Contains("\"rangefinderenabled\": true") || 
                                               json.ToLower().Contains("\"rangefinderenabled\":true");
                }
            }
            catch
            {
                config.SelectedCrosshairId = "default";
                config.Enabled = true;
                config.RangefinderEnabled = true;
            }
            
            return config;
        }
        
        /// <summary>
        /// Called by the dedicated runner MonoBehaviour every frame.
        /// Uses ReferenceEquals to bypass Unity's overloaded == operator
        /// which treats destroyed MonoBehaviours as null even when the
        /// C# reference is still valid.
        /// </summary>
        public static void DoUpdate()
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance.DoUpdateInternal();
        }
        
        private void DoUpdateInternal()
        {
            try
            {
                // F9 to reload crosshairs
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    Log.LogInfo("F9 pressed - Reloading crosshairs...");
                    ResetState();
                    ClearSpriteCache();
                    LoadConfig();
                    CheckMasterLoginStatus();
                    _hasSpawned = true;
                    _setupDelayTimer = 0.1f; // Trigger setup
                }
                
                // After spawn delay, try to set up crosshairs/rangefinder
                if (_hasSpawned && _setupDelayTimer > 0)
                {
                    _setupDelayTimer -= Time.deltaTime;
                    if (_setupDelayTimer <= 0)
                    {
                        SetupCrosshairsAndRangefinder();
                    }
                }
                
                // Update rangefinder if active
                if (_isMasterLoggedIn && _config != null && _config.RangefinderEnabled && _rangefinderCreated)
                {
                    UpdateRangefinder();
                }
            }
            catch (Exception ex)
            {
                // Only log errors occasionally to avoid spam
                if (Time.frameCount % 600 == 1)
                {
                    Log.LogError($"[CustomCrosshairs] Update() exception: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Attaches a TrajectoryRenderer component to the main camera.
        /// OnPostRender is guaranteed to fire on MonoBehaviours attached to a Camera.
        /// </summary>
        private void HookTrajectoryRenderer()
        {
            // Actual attachment happens in UpdateRangefinder once _mainCamera is found
            _trajectoryHooked = false;
            Log.LogInfo("[CustomCrosshairs] Trajectory renderer will attach to main camera when found");
        }
        
        private void AttachTrajectoryToCamera()
        {
            if (_trajectoryHooked || _mainCamera == null) return;
            
            // Remove any existing TrajectoryRenderer (from previous rounds)
            var existing = _mainCamera.GetComponent<TrajectoryRenderer>();
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing);
            }
            
            _mainCamera.gameObject.AddComponent<TrajectoryRenderer>();
            _trajectoryHooked = true;
            Log.LogInfo($"[CustomCrosshairs] TrajectoryRenderer attached to camera '{_mainCamera.name}'");
        }
        
        private void UnhookTrajectoryRenderer()
        {
            _trajectoryHooked = false;
            // Component will be destroyed with the camera or on next attach
        }
        
        /// <summary>
        /// Called by TrajectoryRenderer.OnPostRender on the camera.
        /// </summary>
        public static void DoRenderTrajectory(Camera cam)
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance.RenderTrajectoryInternal(cam);
        }
        
        private void RenderTrajectoryInternal(Camera cam)
        {
            try
            {
                if (!_isMasterLoggedIn || _config == null || !_config.RangefinderEnabled)
                    return;
                if (!_hasSpawned || !_rangefinderCreated)
                    return;
                if (cam == null) return;
                if (_mainCamera != null && cam != _mainCamera) return;
                
                if (!_trajectoryLoggedOnce)
                {
                    _trajectoryLoggedOnce = true;
                    Log.LogInfo($"[CustomCrosshairs] Trajectory renderer active (muzzleV={_muzzleVelocity}, gravity={_bulletGravity})");
                }
                
                // Create material for GL rendering if needed
                if (_trajectoryMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null)
                    {
                        Log.LogWarning("[CustomCrosshairs] Hidden/Internal-Colored shader not found");
                        return;
                    }
                    
                    _trajectoryMaterial = new Material(shader);
                    _trajectoryMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _trajectoryMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    _trajectoryMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    _trajectoryMaterial.SetInt("_Cull", (int)CullMode.Off);
                    _trajectoryMaterial.SetInt("_ZWrite", 0);
                    _trajectoryMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                }
                
                Vector3 camPos = cam.transform.position;
                Vector3 forward = cam.transform.forward;
                
                // Use rangefinder distance or max range
                float maxRange = (_currentDistance > 10f) ? _currentDistance : TRAJECTORY_MAX_RANGE;
                float maxTime = maxRange / _muzzleVelocity;
                
                // Start a few meters ahead to avoid line clipping through player model
                float startTime = TRAJECTORY_START_OFFSET / _muzzleVelocity;
                
                _trajectoryMaterial.SetPass(0);
                GL.PushMatrix();
                
                // Draw the ballistic arc
                GL.Begin(GL.LINES);
                
                Vector3 prevPoint = camPos 
                    + forward * (_muzzleVelocity * startTime) 
                    + Vector3.down * (0.5f * _bulletGravity * startTime * startTime);
                
                for (int i = 1; i <= TRAJECTORY_SEGMENTS; i++)
                {
                    float fraction = (float)i / TRAJECTORY_SEGMENTS;
                    float t = startTime + fraction * (maxTime - startTime);
                    
                    Vector3 point = camPos 
                        + forward * (_muzzleVelocity * t) 
                        + Vector3.down * (0.5f * _bulletGravity * t * t);
                    
                    // Fade from bright to dim along the arc
                    float alpha = Mathf.Lerp(0.9f, 0.15f, fraction);
                    GL.Color(new Color(0.2f, 1f, 0.4f, alpha));
                    GL.Vertex(prevPoint);
                    GL.Vertex(point);
                    
                    prevPoint = point;
                }
                
                GL.End();
                
                // Draw a small impact cross at the endpoint
                Vector3 impactPoint = camPos 
                    + forward * (_muzzleVelocity * maxTime) 
                    + Vector3.down * (0.5f * _bulletGravity * maxTime * maxTime);
                
                Vector3 camRight = cam.transform.right;
                Vector3 camUp = cam.transform.up;
                float crossSize = Mathf.Clamp(maxRange * 0.003f, 0.15f, 1.5f);
                
                GL.Begin(GL.LINES);
                GL.Color(new Color(1f, 0.3f, 0.3f, 0.9f));
                
                // Horizontal bar
                GL.Vertex(impactPoint - camRight * crossSize);
                GL.Vertex(impactPoint + camRight * crossSize);
                // Vertical bar
                GL.Vertex(impactPoint - camUp * crossSize);
                GL.Vertex(impactPoint + camUp * crossSize);
                
                GL.End();
                GL.PopMatrix();
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 1)
                {
                    Log.LogError($"[CustomCrosshairs] Trajectory render error: {ex.Message}");
                }
            }
        }
        
        private void CheckMasterLoginStatus()
        {
            try
            {
                var coreModAssembly = System.Reflection.Assembly.Load("LauncherCoreMod");
                if (coreModAssembly != null)
                {
                    var masterLoginType = coreModAssembly.GetType("LauncherCoreMod.MasterLoginManager");
                    if (masterLoginType != null)
                    {
                        var isMasterLoggedInMethod = masterLoginType.GetMethod("IsMasterLoggedIn", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (isMasterLoggedInMethod != null)
                        {
                            _isMasterLoggedIn = (bool)isMasterLoggedInMethod.Invoke(null, null);
                        }
                    }
                }
            }
            catch { }
        }
        
        private void ClearSpriteCache()
        {
            foreach (var sprite in _loadedSprites.Values)
            {
                if (sprite != null && sprite.texture != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                }
                if (sprite != null)
                {
                    UnityEngine.Object.Destroy(sprite);
                }
            }
            _loadedSprites.Clear();
        }
        
        private void SetupCrosshairsAndRangefinder()
        {
            Log.LogInfo("[CustomCrosshairs] Setting up crosshairs and rangefinder...");
            
            // Find the crosshair panel
            GameObject crosshairPanel = GameObject.Find(CROSSHAIR_PANEL_PATH);
            if (crosshairPanel == null)
            {
                crosshairPanel = GameObject.Find("Crosshair Panel");
            }
            
            if (crosshairPanel == null)
            {
                Log.LogWarning("[CustomCrosshairs] Crosshair panel not found - will retry on next spawn");
                return;
            }
            
            Log.LogInfo($"[CustomCrosshairs] Found crosshair panel: {GetFullPath(crosshairPanel.transform)}");
            
            // Try to replace custom crosshairs if configured
            if (!_crosshairsReplaced && _config != null && _config.Enabled)
            {
                TryReplaceCrosshairs(crosshairPanel);
            }
            
            // Create rangefinder if master logged in and enabled
            if (!_rangefinderCreated && _isMasterLoggedIn && _config != null && _config.RangefinderEnabled)
            {
                CreateRangefinderTexts(crosshairPanel);
            }
        }
        
        private void TryReplaceCrosshairs(GameObject crosshairPanel)
        {
            if (string.IsNullOrEmpty(_config.SelectedCrosshairId) || _config.SelectedCrosshairId == "default")
            {
                Log.LogInfo("[CustomCrosshairs] Using default crosshairs");
                _crosshairsReplaced = true;
                return;
            }
            
            try
            {
                Log.LogInfo($"[CustomCrosshairs] Attempting to replace crosshairs with pack: {_config.SelectedCrosshairId}");
                
                string packFolder = Path.Combine(_crosshairsFolder, _config.SelectedCrosshairId);
                if (!Directory.Exists(packFolder))
                {
                    Log.LogWarning($"[CustomCrosshairs] Crosshair pack not found: {packFolder}");
                    _crosshairsReplaced = true;
                    return;
                }
                
                string[] crosshairNames = {
                    MUSKET_CROSSHAIR_NAME,
                    BLUNDERBUSS_CROSSHAIR_NAME,
                    PISTOL_CROSSHAIR_NAME,
                    RIFLE_CROSSHAIR_NAME,
                    CUSTOM_CROSSHAIR_NAME
                };
                
                int replacedCount = 0;
                foreach (string crosshairName in crosshairNames)
                {
                    if (ReplaceCrosshair(crosshairPanel, crosshairName, packFolder))
                    {
                        replacedCount++;
                    }
                }
                
                Log.LogInfo($"[CustomCrosshairs] Replaced {replacedCount} crosshairs");
                _crosshairsReplaced = true;
            }
            catch (Exception ex)
            {
                Log.LogError($"[CustomCrosshairs] Error replacing crosshairs: {ex.Message}");
                _crosshairsReplaced = true;
            }
        }
        
        private bool ReplaceCrosshair(GameObject crosshairPanel, string crosshairName, string packFolder)
        {
            try
            {
                Transform crosshairTransform = FindChildByName(crosshairPanel.transform, crosshairName);
                if (crosshairTransform == null) return false;
                
                Transform imageTransform = FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME);
                if (imageTransform == null) return false;
                
                Image imageComponent = imageTransform.GetComponent<Image>();
                if (imageComponent == null) return false;
                
                // Find image file
                string imageName = crosshairName.Replace(" ", "_").ToLower();
                string[] extensions = { ".png", ".jpg", ".jpeg" };
                
                foreach (string ext in extensions)
                {
                    string imagePath = Path.Combine(packFolder, imageName + ext);
                    if (File.Exists(imagePath))
                    {
                        Sprite sprite = LoadSpriteFromFile(imagePath);
                        if (sprite != null)
                        {
                            imageComponent.sprite = sprite;
                            _crosshairImages[crosshairName] = imageComponent;
                            Log.LogInfo($"[CustomCrosshairs] Replaced {crosshairName}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[CustomCrosshairs] Error replacing {crosshairName}: {ex.Message}");
            }
            
            return false;
        }
        
        private void CreateRangefinderTexts(GameObject crosshairPanel)
        {
            Log.LogInfo("[CustomCrosshairs] Creating rangefinder text elements...");
            
            try
            {
                // Destroy any existing rangefinder texts first to prevent doubling
                DestroyExistingRangefinderTexts(crosshairPanel);
                _rangefinderTexts.Clear();
                
                string[] crosshairNames = {
                    MUSKET_CROSSHAIR_NAME,
                    BLUNDERBUSS_CROSSHAIR_NAME,
                    PISTOL_CROSSHAIR_NAME,
                    RIFLE_CROSSHAIR_NAME,
                    CUSTOM_CROSSHAIR_NAME
                };
                
                // Find a font from the scene
                Font font = null;
                Text[] allTexts = UnityEngine.Object.FindObjectsOfType<Text>();
                if (allTexts.Length > 0 && allTexts[0].font != null)
                {
                    font = allTexts[0].font;
                }
                
                if (font == null)
                {
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                
                foreach (string crosshairName in crosshairNames)
                {
                    Transform crosshairTransform = FindChildByName(crosshairPanel.transform, crosshairName);
                    if (crosshairTransform == null) continue;
                    
                    // Find the image transform or use crosshair transform as parent
                    Transform parentTransform = FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME);
                    if (parentTransform == null)
                    {
                        parentTransform = crosshairTransform;
                    }
                    
                    // Create new text GameObject
                    GameObject textObj = new GameObject("RangefinderText");
                    textObj.transform.SetParent(parentTransform, false);
                    
                    // Position to the right of the crosshair
                    RectTransform rectTransform = textObj.AddComponent<RectTransform>();
                    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    rectTransform.pivot = new Vector2(0f, 0.5f);
                    rectTransform.anchoredPosition = new Vector2(50f, 0f);
                    rectTransform.sizeDelta = new Vector2(150f, 40f);
                    
                    // Add Text component
                    Text textComponent = textObj.AddComponent<Text>();
                    textComponent.text = "---";
                    textComponent.font = font;
                    textComponent.fontSize = 18;
                    textComponent.color = new Color(1f, 1f, 0f, 1f); // Bright yellow
                    textComponent.alignment = TextAnchor.MiddleLeft;
                    textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                    textComponent.verticalOverflow = VerticalWrapMode.Overflow;
                    textComponent.raycastTarget = false;
                    
                    // Add outline for visibility
                    Outline outline = textObj.AddComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(1f, -1f);
                    
                    _rangefinderTexts[crosshairName] = textComponent;
                    Log.LogInfo($"[CustomCrosshairs] Created rangefinder for {crosshairName}");
                }
                
                if (_rangefinderTexts.Count > 0)
                {
                    _rangefinderCreated = true;
                    HookTrajectoryRenderer();
                    Log.LogInfo($"[CustomCrosshairs] ✓ Rangefinder active with {_rangefinderTexts.Count} text elements");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[CustomCrosshairs] Error creating rangefinder texts: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Walks the crosshair panel hierarchy and destroys any existing RangefinderText GameObjects.
        /// Prevents text doubling when the panel persists across respawns/round changes.
        /// </summary>
        private void DestroyExistingRangefinderTexts(GameObject crosshairPanel)
        {
            try
            {
                int destroyed = 0;
                string[] crosshairNames = {
                    MUSKET_CROSSHAIR_NAME,
                    BLUNDERBUSS_CROSSHAIR_NAME,
                    PISTOL_CROSSHAIR_NAME,
                    RIFLE_CROSSHAIR_NAME,
                    CUSTOM_CROSSHAIR_NAME
                };
                
                foreach (string crosshairName in crosshairNames)
                {
                    Transform crosshairTransform = FindChildByName(crosshairPanel.transform, crosshairName);
                    if (crosshairTransform == null) continue;
                    
                    // Check both possible parents: the image child and the crosshair itself
                    Transform[] parents = new Transform[] {
                        FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME),
                        crosshairTransform
                    };
                    
                    foreach (Transform parent in parents)
                    {
                        if (parent == null) continue;
                        
                        // Iterate children in reverse to safely destroy
                        for (int i = parent.childCount - 1; i >= 0; i--)
                        {
                            Transform child = parent.GetChild(i);
                            if (child.name == "RangefinderText")
                            {
                                UnityEngine.Object.Destroy(child.gameObject);
                                destroyed++;
                            }
                        }
                    }
                }
                
                if (destroyed > 0)
                {
                    Log.LogInfo($"[CustomCrosshairs] Cleaned up {destroyed} old rangefinder text(s)");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[CustomCrosshairs] Error cleaning up old rangefinder texts: {ex.Message}");
            }
        }
        
        private void UpdateRangefinder()
        {
            if (Time.time - _lastRaycastTime < RAYCAST_INTERVAL)
                return;
            
            _lastRaycastTime = Time.time;
            
            try
            {
                // Get main camera
                if (_mainCamera == null)
                {
                    _mainCamera = Camera.main;
                    if (_mainCamera == null)
                    {
                        GameObject cameraObj = GameObject.FindGameObjectWithTag("MainCamera");
                        if (cameraObj != null)
                        {
                            _mainCamera = cameraObj.GetComponent<Camera>();
                        }
                    }
                    if (_mainCamera == null)
                    {
                        Camera[] cameras = Camera.allCameras;
                        if (cameras.Length > 0)
                        {
                            _mainCamera = cameras[0];
                        }
                    }
                }
                
                if (_mainCamera == null)
                    return;
                
                // Attach trajectory renderer to camera if not yet done
                if (!_trajectoryHooked && _isMasterLoggedIn)
                {
                    AttachTrajectoryToCamera();
                }
                
                // Calculate layer mask on first use
                if (_raycastLayerMask == -1)
                {
                    _raycastLayerMask = ~0;
                    string[] layersToIgnore = { "UI", "Ignore Raycast", "TransparentFX", "Water", "LocalPlayer", "Player" };
                    foreach (string layerName in layersToIgnore)
                    {
                        int layer = LayerMask.NameToLayer(layerName);
                        if (layer >= 0)
                        {
                            _raycastLayerMask &= ~(1 << layer);
                        }
                    }
                }
                
                // Raycast from camera center forward
                Ray ray = _mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
                RaycastHit hit;
                
                float distance = 0f;
                if (Physics.Raycast(ray, out hit, 2000f, _raycastLayerMask, QueryTriggerInteraction.Ignore))
                {
                    distance = hit.distance;
                }
                
                _currentDistance = distance;
                
                // Build rangefinder display text
                string distanceText = distance > 0f ? $"{distance:F0}m" : "---";
                
                // Scan for nearest enemy in aim direction
                string aimGuidance = FindEnemyAimGuidance(ray.direction);
                if (!string.IsNullOrEmpty(aimGuidance))
                {
                    distanceText += "\n" + aimGuidance;
                }
                
                // Update all rangefinder texts
                foreach (var kvp in _rangefinderTexts)
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.text = distanceText;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[CustomCrosshairs] Error updating rangefinder: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Scans tracked enemy players, finds the closest one within the aim cone,
        /// then simulates where the bullet would land at that distance given the
        /// CURRENT aim direction. Shows the remaining vertical offset so the
        /// number decreases as the player raises/lowers their crosshair.
        /// </summary>
        private string FindEnemyAimGuidance(Vector3 aimDirection)
        {
            if (_mainCamera == null || _localPlayerId < 0 || _trackedPlayers.Count == 0)
                return null;
            
            Vector3 camPos = _mainCamera.transform.position;
            Vector3 aimDir = aimDirection.normalized;
            float nearestDist = float.MaxValue;
            int nearestId = -1;
            Vector3 nearestEnemyCenter = Vector3.zero;
            
            var deadKeys = new List<int>();
            
            foreach (var kvp in _trackedPlayers)
            {
                var tp = kvp.Value;
                
                if (tp.PlayerId == _localPlayerId) continue;
                if (tp.Faction == _localFaction) continue;
                
                if (tp.PlayerObject == null)
                {
                    deadKeys.Add(kvp.Key);
                    continue;
                }
                
                Vector3 enemyPos = tp.PlayerObject.transform.position + Vector3.up * 1.3f;
                Vector3 toEnemy = enemyPos - camPos;
                float dist = toEnemy.magnitude;
                
                if (dist > 500f || dist < 3f) continue;
                
                float dot = Vector3.Dot(aimDir, toEnemy.normalized);
                if (dot < AIM_CONE_COS) continue;
                
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestId = tp.PlayerId;
                    nearestEnemyCenter = enemyPos;
                }
            }
            
            foreach (int key in deadKeys)
            {
                _trackedPlayers.Remove(key);
            }
            
            if (nearestId < 0) return null;
            
            // Simulate where the bullet would be at the enemy's distance
            // given the CURRENT aim direction.
            // bullet_pos(t) = camPos + aimDir * muzzleVelocity * t + down * 0.5 * g * t²
            // At the enemy's range: t = distance / muzzleVelocity
            float timeToTarget = nearestDist / _muzzleVelocity;
            Vector3 bulletAtTarget = camPos 
                + aimDir * nearestDist 
                + Vector3.down * (0.5f * _bulletGravity * timeToTarget * timeToTarget);
            
            // How far does the bullet miss the enemy's center mass vertically?
            float verticalMiss = bulletAtTarget.y - nearestEnemyCenter.y;
            // Positive = bullet goes OVER enemy → aim lower
            // Negative = bullet falls SHORT → aim higher
            
            // Elevation indicator (enemy above/below you)
            float heightDiff = nearestEnemyCenter.y - camPos.y;
            string elevText = "";
            if (Mathf.Abs(heightDiff) > 2f)
            {
                elevText = heightDiff > 0f ? "\u25B2" : "\u25BC";
            }
            
            // Format based on miss direction and magnitude
            float absMiss = Mathf.Abs(verticalMiss);
            
            if (absMiss < MIN_DROP_DISPLAY)
            {
                // On target - turn green
                return $"<color=#44FF44>E:{nearestDist:F0}m{elevText} \u25CF</color>";
            }
            else if (verticalMiss < 0f)
            {
                // Bullet falls short - aim higher
                return $"<color=#FF6666>E:{nearestDist:F0}m{elevText} \u2191{absMiss:F1}m</color>";
            }
            else
            {
                // Bullet goes over - aim lower
                return $"<color=#FFAA44>E:{nearestDist:F0}m{elevText} \u2193{absMiss:F1}m</color>";
            }
        }
        
        private string GetFullPath(Transform t)
        {
            if (t == null) return "null";
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
        
        private Transform FindChildByName(Transform parent, string name)
        {
            if (parent == null) return null;
            
            // Check direct children first
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }
            
            // Recursively search grandchildren
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildByName(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }
        
        private Sprite LoadSpriteFromFile(string filePath)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                if (_loadedSprites.ContainsKey(fileName) && _loadedSprites[fileName] != null)
                {
                    return _loadedSprites[fileName];
                }
                
                if (!File.Exists(filePath))
                {
                    return null;
                }
                
                byte[] fileData = File.ReadAllBytes(filePath);
                
                Texture2D texture = new Texture2D(2, 2);
                if (!UnityEngine.ImageConversion.LoadImage(texture, fileData))
                {
                    Log.LogError($"Failed to load image: {filePath}");
                    return null;
                }
                
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
                
                sprite.name = Path.GetFileNameWithoutExtension(filePath);
                _loadedSprites[fileName] = sprite;
                
                Log.LogInfo($"Loaded sprite: {filePath} ({texture.width}x{texture.height})");
                return sprite;
            }
            catch (Exception ex)
            {
                Log.LogError($"Error loading sprite from {filePath}: {ex.Message}");
                return null;
            }
        }
        
        void OnDestroy()
        {
            UnhookTrajectoryRenderer();
            ClearSpriteCache();
            if (_trajectoryMaterial != null)
            {
                UnityEngine.Object.Destroy(_trajectoryMaterial);
                _trajectoryMaterial = null;
            }
        }
    }
    
    public class CrosshairConfig
    {
        public string SelectedCrosshairId { get; set; } = "default";
        public bool Enabled { get; set; } = true;
        public bool RangefinderEnabled { get; set; } = true;
    }
}
