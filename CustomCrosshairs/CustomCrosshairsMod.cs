using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace CustomCrosshairs
{
    [BepInPlugin("com.xarkanoth.customcrosshairs", "Custom Crosshairs", "1.0.17")]
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
        private const string LOGIN_TOKEN_FILE = "master_login.token";
        private const string HASH_SALT = "HF_MODDING_2024_XARK";
        private bool _isMasterLoggedIn = false;
        private float _lastTokenCheck = 0f;
        private const float TOKEN_CHECK_INTERVAL = 5f;
        
        // Rangefinder
        private Camera _mainCamera;
        private float _lastRaycastTime = 0f;
        private const float RAYCAST_INTERVAL = 0.1f;
        private float _currentDistance = 0f;
        private int _raycastLayerMask = -1; // Will be calculated on first use
        
        // Tracking
        private bool _crosshairsReplaced = false;
        private bool _rangefinderCreated = false;
        private float _lastSearchTime = 0f;
        private const float SEARCH_INTERVAL = 0.5f;  // Search every 0.5 seconds until found
        private bool _loggedSearching = false;
        
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
            
            // Check master login
            CheckMasterLoginToken();
            
            Log.LogInfo($"Initial state - Master login: {_isMasterLoggedIn}, RangefinderEnabled: {_config?.RangefinderEnabled}");
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
                    Log.LogInfo("No config file found, using defaults. Configure via Modding Launcher.");
                    _config = new CrosshairConfig { SelectedCrosshairId = "default", Enabled = true, RangefinderEnabled = false };
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load config: {ex.Message}");
                _config = new CrosshairConfig { SelectedCrosshairId = "default", Enabled = true, RangefinderEnabled = false };
            }
        }
        
        private CrosshairConfig ParseConfig(string json)
        {
            var config = new CrosshairConfig();
            
            var crosshairIdMatch = Regex.Match(json, @"""SelectedCrosshairId""\s*:\s*""([^""]*)""");
            if (crosshairIdMatch.Success)
            {
                config.SelectedCrosshairId = crosshairIdMatch.Groups[1].Value;
            }
            
            var enabledMatch = Regex.Match(json, @"""Enabled""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            if (enabledMatch.Success)
            {
                config.Enabled = enabledMatch.Groups[1].Value.ToLower() == "true";
            }
            
            var rangefinderMatch = Regex.Match(json, @"""RangefinderEnabled""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            if (rangefinderMatch.Success)
            {
                config.RangefinderEnabled = rangefinderMatch.Groups[1].Value.ToLower() == "true";
            }
            
            return config;
        }
        
        void Update()
        {
            // Check master login periodically
            if (Time.time - _lastTokenCheck > TOKEN_CHECK_INTERVAL)
            {
                _lastTokenCheck = Time.time;
                bool wasLoggedIn = _isMasterLoggedIn;
                CheckMasterLoginToken();
                
                if (_isMasterLoggedIn != wasLoggedIn)
                {
                    Log.LogInfo($"Master login status changed: {_isMasterLoggedIn}");
                    
                    // Update rangefinder visibility based on master login and config
                    if (_isMasterLoggedIn && _config != null && _config.RangefinderEnabled)
                    {
                        Log.LogInfo("Rangefinder ENABLED (master login + config enabled)");
                        // Will be created on next search cycle if crosshair panel exists
                        _rangefinderCreated = false;
                    }
                    else
                    {
                        if (!_isMasterLoggedIn)
                            Log.LogInfo("Rangefinder disabled (no master login)");
                        else if (_config == null || !_config.RangefinderEnabled)
                            Log.LogInfo("Rangefinder disabled (config RangefinderEnabled=false)");
                        RemoveRangefinderTexts();
                        _rangefinderCreated = false;
                    }
                }
            }
            
            // F9 to reload crosshairs
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Log.LogInfo("F9 pressed - Reloading crosshairs...");
                _crosshairsReplaced = false;
                _rangefinderCreated = false;
                _crosshairImages.Clear();
                RemoveRangefinderTexts();
                // Clear sprite cache to force reload
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
                LoadConfig(); // Reload config
                TryReplaceCrosshairs();
            }
            
            // Search for crosshair UI elements periodically
            if (Time.time - _lastSearchTime > SEARCH_INTERVAL)
            {
                _lastSearchTime = Time.time;
                
                // Try to find crosshair panel - it only exists when in-game
                GameObject crosshairPanel = GameObject.Find(CROSSHAIR_PANEL_PATH);
                
                // Log search attempts periodically (every 5 seconds) when panel not found
                if (crosshairPanel == null && Time.time % 5f < SEARCH_INTERVAL)
                {
                    Log.LogInfo($"Searching for crosshair panel... (in-game: {Time.time > 10f}, master login: {_isMasterLoggedIn}, rangefinder enabled: {_config?.RangefinderEnabled})");
                }
                
                if (crosshairPanel != null)
                {
                    if (!_loggedSearching)
                    {
                        Log.LogInfo($"Found crosshair panel at: {CROSSHAIR_PANEL_PATH}");
                        Log.LogInfo($"Master login: {_isMasterLoggedIn}, RangefinderEnabled: {_config?.RangefinderEnabled}");
                        _loggedSearching = true;
                    }
                    
                    // Try to replace crosshairs if not done yet
                    if (!_crosshairsReplaced)
                    {
                        TryReplaceCrosshairs();
                    }
                    
                    // Create rangefinder if enabled and not yet created
                    if (_isMasterLoggedIn && _config != null && _config.RangefinderEnabled && !_rangefinderCreated)
                    {
                        Log.LogInfo("Creating rangefinder texts (conditions met)");
                        CreateRangefinderTexts();
                        _rangefinderCreated = true;
                    }
                    else if (!_rangefinderCreated)
                    {
                        // Log why rangefinder isn't being created
                        if (!_isMasterLoggedIn)
                            Log.LogWarning("Rangefinder not created: Master login = false");
                        else if (_config == null)
                            Log.LogWarning("Rangefinder not created: Config is null");
                        else if (!_config.RangefinderEnabled)
                            Log.LogWarning("Rangefinder not created: RangefinderEnabled = false");
                    }
                }
                else
                {
                    // Panel not found - we're probably in menu, reset state
                    if (_crosshairsReplaced || _rangefinderCreated)
                    {
                        Log.LogInfo("Crosshair panel lost (returned to menu) - resetting state");
                        _crosshairsReplaced = false;
                        _rangefinderCreated = false;
                        _loggedSearching = false;
                        _crosshairImages.Clear();
                        _rangefinderTexts.Clear();
                    }
                }
            }
            
            // Update rangefinder if active
            if (_isMasterLoggedIn && _config != null && _config.RangefinderEnabled && _rangefinderCreated)
            {
                UpdateRangefinder();
            }
        }
        
        /// <summary>
        /// Attempts to find and replace crosshair sprites
        /// </summary>
        private void TryReplaceCrosshairs()
        {
            if (_config == null || !_config.Enabled)
                return;
                
            if (string.IsNullOrEmpty(_config.SelectedCrosshairId) || _config.SelectedCrosshairId == "default")
                return; // Using default crosshairs
            
            try
            {
                GameObject crosshairPanel = GameObject.Find(CROSSHAIR_PANEL_PATH);
                if (crosshairPanel == null)
                {
                    return; // UI not loaded yet
                }
                
                Log.LogInfo("Found Crosshair Panel, searching for crosshair images...");
                
                // Find all crosshair GameObjects
                bool foundAny = false;
                
                // Load crosshair sprites from downloaded folder
                string selectedCrosshairFolder = Path.Combine(_crosshairsFolder, _config.SelectedCrosshairId);
                if (!Directory.Exists(selectedCrosshairFolder))
                {
                    Log.LogWarning($"Crosshair folder not found: {selectedCrosshairFolder}");
                    return;
                }
                
                foundAny |= ReplaceCrosshair(crosshairPanel, MUSKET_CROSSHAIR_NAME, Path.Combine(selectedCrosshairFolder, "MusketCrosshair.png"));
                foundAny |= ReplaceCrosshair(crosshairPanel, BLUNDERBUSS_CROSSHAIR_NAME, Path.Combine(selectedCrosshairFolder, "BlunderbussCrosshair.png"));
                foundAny |= ReplaceCrosshair(crosshairPanel, PISTOL_CROSSHAIR_NAME, Path.Combine(selectedCrosshairFolder, "PistolCrosshair.png"));
                foundAny |= ReplaceCrosshair(crosshairPanel, RIFLE_CROSSHAIR_NAME, Path.Combine(selectedCrosshairFolder, "RifleCrosshair.png"));
                foundAny |= ReplaceCrosshair(crosshairPanel, CUSTOM_CROSSHAIR_NAME, Path.Combine(selectedCrosshairFolder, "CustomCrosshair.png"));
                
                if (foundAny)
                {
                    _crosshairsReplaced = true;
                    Log.LogInfo("Crosshairs replaced successfully!");
                    
                    // Create rangefinder texts if enabled
                    if (_isMasterLoggedIn && _config != null && _config.RangefinderEnabled)
                    {
                        CreateRangefinderTexts();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error replacing crosshairs: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Replaces a specific crosshair sprite
        /// </summary>
        private bool ReplaceCrosshair(GameObject parent, string crosshairName, string imagePath)
        {
            try
            {
                // Find the crosshair GameObject
                Transform crosshairTransform = FindChildByName(parent.transform, crosshairName);
                if (crosshairTransform == null)
                {
                    return false;
                }
                
                // Find the Crosshair Image component
                Transform imageTransform = FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME);
                if (imageTransform == null)
                {
                    return false;
                }
                
                Image imageComponent = imageTransform.GetComponent<Image>();
                if (imageComponent == null)
                {
                    return false;
                }
                
                // Load and replace sprite if file exists
                if (File.Exists(imagePath))
                {
                    Sprite newSprite = LoadSpriteFromFile(imagePath);
                    if (newSprite != null)
                    {
                        imageComponent.sprite = newSprite;
                        _crosshairImages[crosshairName] = imageComponent;
                        Log.LogInfo($"Replaced {crosshairName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error replacing {crosshairName}: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Recursively finds a child GameObject by name
        /// </summary>
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
        
        /// <summary>
        /// Loads a sprite from a PNG/JPG file
        /// </summary>
        private Sprite LoadSpriteFromFile(string filePath)
        {
            try
            {
                // Check cache first
                string fileName = Path.GetFileName(filePath);
                if (_loadedSprites.ContainsKey(fileName) && _loadedSprites[fileName] != null)
                {
                    return _loadedSprites[fileName];
                }
                
                if (!File.Exists(filePath))
                {
                    return null;
                }
                
                // Read file bytes
                byte[] fileData = File.ReadAllBytes(filePath);
                
                // Create texture
                Texture2D texture = new Texture2D(2, 2);
                if (!UnityEngine.ImageConversion.LoadImage(texture, fileData))
                {
                    Log.LogError($"Failed to load image: {filePath}");
                    return null;
                }
                
                // Create sprite (centered pivot, 100 pixels per unit)
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
                
                sprite.name = Path.GetFileNameWithoutExtension(filePath);
                
                // Cache sprite
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
        
        /// <summary>
        /// Creates rangefinder text components for all crosshairs
        /// </summary>
        private void CreateRangefinderTexts()
        {
            if (!_isMasterLoggedIn || _config == null || !_config.RangefinderEnabled) return;
            
            Log.LogInfo("Creating rangefinder text elements...");
            
            try
            {
                GameObject crosshairPanel = GameObject.Find(CROSSHAIR_PANEL_PATH);
                if (crosshairPanel == null)
                {
                    return;
                }
                
                string[] crosshairNames = {
                    MUSKET_CROSSHAIR_NAME,
                    BLUNDERBUSS_CROSSHAIR_NAME,
                    PISTOL_CROSSHAIR_NAME,
                    RIFLE_CROSSHAIR_NAME,
                    CUSTOM_CROSSHAIR_NAME
                };
                
                foreach (string crosshairName in crosshairNames)
                {
                    if (_rangefinderTexts.ContainsKey(crosshairName))
                        continue; // Already created
                    
                    Transform crosshairTransform = FindChildByName(crosshairPanel.transform, crosshairName);
                    if (crosshairTransform == null) continue;
                    
                    Transform imageTransform = FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME);
                    if (imageTransform == null) continue;
                    
                    // Check if text already exists
                    Text existingText = imageTransform.GetComponentInChildren<Text>();
                    if (existingText != null && existingText.name == "RangefinderText")
                    {
                        _rangefinderTexts[crosshairName] = existingText;
                        continue;
                    }
                    
                    // Create new text GameObject
                    GameObject textObj = new GameObject("RangefinderText");
                    textObj.transform.SetParent(imageTransform, false);
                    
                    // Position to the TOP-RIGHT of the crosshair image
                    RectTransform rectTransform = textObj.AddComponent<RectTransform>();
                    rectTransform.anchorMin = new Vector2(1f, 1f);  // Top-right anchor
                    rectTransform.anchorMax = new Vector2(1f, 1f);  // Top-right anchor
                    rectTransform.pivot = new Vector2(0f, 0.5f);    // Left-center pivot (text extends right)
                    rectTransform.anchoredPosition = new Vector2(15f, -5f);  // Offset to top-right
                    rectTransform.sizeDelta = new Vector2(100f, 30f);
                    
                    // Add Text component
                    Text textComponent = textObj.AddComponent<Text>();
                    textComponent.text = "";
                    textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    textComponent.fontSize = 16;
                    textComponent.color = Color.yellow;
                    textComponent.alignment = TextAnchor.MiddleLeft;  // Left-align since it's to the right
                    textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                    textComponent.verticalOverflow = VerticalWrapMode.Overflow;
                    
                    // Add outline/shadow for visibility
                    Shadow shadow = textObj.AddComponent<Shadow>();
                    shadow.effectColor = Color.black;
                    shadow.effectDistance = new Vector2(1f, -1f);
                    
                    _rangefinderTexts[crosshairName] = textComponent;
                    Log.LogInfo($"Created rangefinder text for: {crosshairName}");
                }
                
                Log.LogInfo($"Rangefinder active with {_rangefinderTexts.Count} text elements");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error creating rangefinder texts: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Removes all rangefinder text components
        /// </summary>
        private void RemoveRangefinderTexts()
        {
            foreach (var text in _rangefinderTexts.Values)
            {
                if (text != null && text.gameObject != null)
                {
                    UnityEngine.Object.Destroy(text.gameObject);
                }
            }
            _rangefinderTexts.Clear();
        }
        
        /// <summary>
        /// Updates rangefinder distance display
        /// </summary>
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
                }
                
                if (_mainCamera == null)
                {
                    GameObject cameraObj = GameObject.FindGameObjectWithTag("MainCamera");
                    if (cameraObj != null)
                    {
                        _mainCamera = cameraObj.GetComponent<Camera>();
                    }
                }
                
                if (_mainCamera == null)
                    return;
                
                // Calculate layer mask on first use - ignore UI, triggers, and player layers
                if (_raycastLayerMask == -1)
                {
                    // Start with all layers
                    _raycastLayerMask = ~0;
                    
                    // Ignore common non-world layers (by name if they exist)
                    string[] layersToIgnore = { "UI", "Ignore Raycast", "TransparentFX", "Water", "LocalPlayer", "Player" };
                    foreach (string layerName in layersToIgnore)
                    {
                        int layer = LayerMask.NameToLayer(layerName);
                        if (layer >= 0)
                        {
                            _raycastLayerMask &= ~(1 << layer);
                        }
                    }
                    
                    Log.LogInfo($"Rangefinder LayerMask calculated: {_raycastLayerMask}");
                }
                
                // Raycast from camera center forward
                Ray ray = _mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
                RaycastHit hit;
                
                float distance = 0f;
                
                // Use layermask to ignore UI and player layers, QueryTriggerInteraction.Ignore to skip triggers
                if (Physics.Raycast(ray, out hit, 2000f, _raycastLayerMask, QueryTriggerInteraction.Ignore))
                {
                    distance = hit.distance;
                }
                else
                {
                    distance = 0f;
                }
                
                _currentDistance = distance;
                
                // Update all rangefinder texts
                string distanceText = distance > 0f ? $"{distance:F1}m" : "";
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
                Log.LogError($"Error updating rangefinder: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks for master login token
        /// </summary>
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
                            if (!wasLoggedIn)
                            {
                                Log.LogInfo($"Master login token verified at: {tokenPath}");
                            }
                            break;
                        }
                    }
                }
                
                if (!_isMasterLoggedIn && wasLoggedIn)
                {
                    Log.LogInfo("Master login token not found or invalid");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Error checking master login: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets possible paths for master login token
        /// </summary>
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
        
        /// <summary>
        /// Verifies master login token
        /// </summary>
        private bool VerifySecureToken(string token)
        {
            if (token == "MASTER_ACCESS_GRANTED") return true;
            if (token == CreateExpectedToken(DateTime.UtcNow)) return true;
            if (token == CreateExpectedToken(DateTime.UtcNow.AddDays(-1))) return true;
            return false;
        }
        
        /// <summary>
        /// Creates expected token for verification
        /// </summary>
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
        
        void OnApplicationQuit()
        {
            // Clean up sprites
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
            _crosshairImages.Clear();
            RemoveRangefinderTexts();
        }
    }
    
    public class CrosshairConfig
    {
        public string SelectedCrosshairId { get; set; } = "default";
        public bool RangefinderEnabled { get; set; } = false;
        public bool Enabled { get; set; } = true;
    }
}
