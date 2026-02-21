using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HoldfastSharedMethods;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CustomCrosshairs
{
    public class CustomCrosshairsRunner : MonoBehaviour
    {
        void Update()
        {
            CustomCrosshairsMod.DoUpdate();
        }
    }

    [BepInPlugin("com.xarkanoth.customcrosshairs", "Custom Crosshairs", "1.0.41")]
    [BepInDependency("com.xarkanoth.launchercoremod", BepInDependency.DependencyFlags.HardDependency)]
    public class CustomCrosshairsMod : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        private static CustomCrosshairsMod _instance;

        private const string CROSSHAIR_PANEL_PATH = "Main Canvas/Game Elements Panel/Crosshair Panel";
        private const string MUSKET_CROSSHAIR_NAME = "Musket Crosshair";
        private const string BLUNDERBUSS_CROSSHAIR_NAME = "Blunderbuss Crosshair";
        private const string PISTOL_CROSSHAIR_NAME = "Pistol Crosshair";
        private const string RIFLE_CROSSHAIR_NAME = "Rifle Crosshair";
        private const string CUSTOM_CROSSHAIR_NAME = "Custom Crosshair";
        private const string CROSSHAIR_IMAGE_NAME = "Crosshair Image";

        private CrosshairConfig _config;
        private string _configPath;
        private string _crosshairsFolder;
        private Dictionary<string, Sprite> _loadedSprites = new Dictionary<string, Sprite>();
        private Dictionary<string, Image> _crosshairImages = new Dictionary<string, Image>();

        // State tracking
        private bool _hasSpawned = false;
        private bool _crosshairsReplaced = false;
        private float _setupDelayTimer = 0f;
        private const float SETUP_DELAY_AFTER_SPAWN = 0.5f;
        private static bool _runnerCreated = false;

        void Awake()
        {
            _instance = this;
            Log = Logger;
            Log.LogInfo("Custom Crosshairs mod loaded!");

            string pluginFolder = Path.GetDirectoryName(Info.Location);
            string bepInExFolder = Path.GetDirectoryName(pluginFolder);

            _configPath = Path.Combine(bepInExFolder, "config", "com.xarkanoth.customcrosshairs.json");
            _crosshairsFolder = Path.Combine(bepInExFolder, "CustomCrosshairs");

            if (!Directory.Exists(_crosshairsFolder))
            {
                Directory.CreateDirectory(_crosshairsFolder);
            }

            LoadConfig();
            SubscribeToGameEvents();
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

                Log.LogInfo("[CustomCrosshairs] Subscribed to game events");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[CustomCrosshairs] Error subscribing to events: {ex.Message} - will use fallback polling");
            }
        }

        private void HandleConnectedToServer(ulong steamId)
        {
            Log.LogInfo($"[CustomCrosshairs] Connected to server (Steam ID: {steamId})");
            _hasSpawned = false;
            ResetState();
        }

        private void HandleDisconnectedFromServer()
        {
            Log.LogInfo("[CustomCrosshairs] Disconnected from server - resetting state");
            _hasSpawned = false;
            ResetState();
        }

        private void HandleLocalPlayerSpawned(int playerId, FactionCountry faction, PlayerClass playerClass)
        {
            Log.LogInfo($"[CustomCrosshairs] Local player spawned (ID: {playerId}, Class: {playerClass})");
            _hasSpawned = true;
            _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
            _crosshairsReplaced = false;
        }

        private void HandlePlayerSpawned(int playerId, int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            if (!_hasSpawned && playerObject != null)
            {
                Log.LogInfo($"[CustomCrosshairs] Player spawned (fallback, ID: {playerId}, Class: {playerClass})");
                _hasSpawned = true;
                _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
                _crosshairsReplaced = false;
            }
        }

        private void ResetState()
        {
            _crosshairsReplaced = false;
            _crosshairImages.Clear();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = ParseConfig(json);
                    Log.LogInfo($"Loaded config: CrosshairId={_config?.SelectedCrosshairId}, Enabled={_config?.Enabled}");
                }
                else
                {
                    _config = new CrosshairConfig
                    {
                        SelectedCrosshairId = "default",
                        Enabled = true
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
                    Enabled = true
                };
            }
        }

        private CrosshairConfig ParseConfig(string json)
        {
            var config = new CrosshairConfig();

            try
            {
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
            }
            catch
            {
                config.SelectedCrosshairId = "default";
                config.Enabled = true;
            }

            return config;
        }

        public static void DoUpdate()
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance.DoUpdateInternal();
        }

        private void DoUpdateInternal()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    Log.LogInfo("F9 pressed - Reloading crosshairs...");
                    ResetState();
                    ClearSpriteCache();
                    LoadConfig();
                    _hasSpawned = true;
                    _setupDelayTimer = 0.1f;
                }

                if (_hasSpawned && _setupDelayTimer > 0)
                {
                    _setupDelayTimer -= Time.deltaTime;
                    if (_setupDelayTimer <= 0)
                    {
                        SetupCrosshairs();
                    }
                }
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 1)
                {
                    Log.LogError($"[CustomCrosshairs] Update() exception: {ex.Message}");
                }
            }
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

        private void SetupCrosshairs()
        {
            Log.LogInfo("[CustomCrosshairs] Setting up crosshairs...");

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

            if (!_crosshairsReplaced && _config != null && _config.Enabled)
            {
                TryReplaceCrosshairs(crosshairPanel);
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

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

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
    }
}
