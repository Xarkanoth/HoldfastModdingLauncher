using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HoldfastSharedMethods;
using UnityEngine;
using UnityEngine.UI;

namespace CustomCrosshairs
{
    [BepInPlugin("com.xarkanoth.customcrosshairs", "Custom Crosshairs", "1.0.26")]
    [BepInDependency("com.xarkanoth.launchercoremod", BepInDependency.DependencyFlags.HardDependency)]
    public class CustomCrosshairsMod : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        
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
        
        // State tracking
        private bool _isInGame = false;
        private bool _hasSpawned = false;
        private bool _crosshairsReplaced = false;
        private bool _rangefinderCreated = false;
        private float _setupDelayTimer = 0f;
        private const float SETUP_DELAY_AFTER_SPAWN = 0.5f; // Wait 0.5s after spawn to find UI
        
        void Awake()
        {
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
            Log.LogInfo($"[CustomCrosshairs] Connected to server (Steam: {steamId})");
            _isInGame = true;
            _hasSpawned = false;
            ResetState();
        }
        
        private void HandleDisconnectedFromServer()
        {
            Log.LogInfo("[CustomCrosshairs] Disconnected from server");
            _isInGame = false;
            _hasSpawned = false;
            ResetState();
        }
        
        private void HandleLocalPlayerSpawned(int playerId, FactionCountry faction, PlayerClass playerClass)
        {
            Log.LogInfo($"[CustomCrosshairs] ★ Local player spawned! Id={playerId}, Class={playerClass}");
            _hasSpawned = true;
            _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
            
            // Reset crosshair/rangefinder state so they get recreated
            _crosshairsReplaced = false;
            _rangefinderCreated = false;
        }
        
        private void ResetState()
        {
            _crosshairsReplaced = false;
            _rangefinderCreated = false;
            _crosshairImages.Clear();
            _rangefinderTexts.Clear();
            _mainCamera = null;
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
        
        void Update()
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
                    if (_rangefinderTexts.ContainsKey(crosshairName))
                        continue;
                    
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
                    Log.LogInfo($"[CustomCrosshairs] ✓ Rangefinder active with {_rangefinderTexts.Count} text elements");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[CustomCrosshairs] Error creating rangefinder texts: {ex.Message}");
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
                
                // Update all rangefinder texts
                string distanceText = distance > 0f ? $"{distance:F0}m" : "---";
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
            ClearSpriteCache();
        }
    }
    
    public class CrosshairConfig
    {
        public string SelectedCrosshairId { get; set; } = "default";
        public bool Enabled { get; set; } = true;
        public bool RangefinderEnabled { get; set; } = true;
    }
}
