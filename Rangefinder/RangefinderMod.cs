using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HoldfastSharedMethods;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Rangefinder
{
    public class RangefinderRunner : MonoBehaviour
    {
        void Update()
        {
            RangefinderMod.DoUpdate();
        }
    }

    public class TrajectoryRenderer : MonoBehaviour
    {
        void OnPostRender()
        {
            RangefinderMod.DoRenderTrajectory(GetComponent<Camera>());
        }
    }

    [BepInPlugin("com.xarkanoth.rangefinder", "Rangefinder", "1.0.0")]
    [BepInDependency("com.xarkanoth.launchercoremod", BepInDependency.DependencyFlags.HardDependency)]
    public class RangefinderMod : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        private static RangefinderMod _instance;

        private const string CROSSHAIR_PANEL_PATH = "Main Canvas/Game Elements Panel/Crosshair Panel";
        private const string MUSKET_CROSSHAIR_NAME = "Musket Crosshair";
        private const string BLUNDERBUSS_CROSSHAIR_NAME = "Blunderbuss Crosshair";
        private const string PISTOL_CROSSHAIR_NAME = "Pistol Crosshair";
        private const string RIFLE_CROSSHAIR_NAME = "Rifle Crosshair";
        private const string CUSTOM_CROSSHAIR_NAME = "Custom Crosshair";
        private const string CROSSHAIR_IMAGE_NAME = "Crosshair Image";

        private bool _isMasterLoggedIn = false;

        // Rangefinder
        private Camera _mainCamera;
        private float _lastRaycastTime = 0f;
        private const float RAYCAST_INTERVAL = 0.1f;
        private float _currentDistance = 0f;
        private int _raycastLayerMask = -1;
        private Dictionary<string, Text> _rangefinderTexts = new Dictionary<string, Text>();

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
        private const float AIM_CONE_COS = 0.9848f;
        private const float MIN_DROP_DISPLAY = 0.3f;

        // State tracking
        private bool _hasSpawned = false;
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
            Log.LogInfo("Rangefinder mod loaded!");

            SubscribeToGameEvents();
            SceneManager.sceneLoaded += OnSceneLoadedCreateRunner;
        }

        private void OnSceneLoadedCreateRunner(Scene scene, LoadSceneMode mode)
        {
            if (_runnerCreated) return;
            _runnerCreated = true;

            var go = new GameObject("RangefinderRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<RangefinderRunner>();
            Log.LogInfo($"[Rangefinder] Runner created on scene '{scene.name}'");
        }

        private void SubscribeToGameEvents()
        {
            try
            {
                var coreModAssembly = System.Reflection.Assembly.Load("LauncherCoreMod");
                if (coreModAssembly == null)
                {
                    Log.LogWarning("[Rangefinder] LauncherCoreMod assembly not found");
                    return;
                }

                var gameEventsType = coreModAssembly.GetType("LauncherCoreMod.GameEvents");
                if (gameEventsType == null)
                {
                    Log.LogWarning("[Rangefinder] GameEvents type not found");
                    return;
                }

                var connectedEvent = gameEventsType.GetEvent("OnConnectedToServer");
                if (connectedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        connectedEvent.EventHandlerType,
                        this,
                        typeof(RangefinderMod).GetMethod(nameof(HandleConnectedToServer),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    connectedEvent.AddEventHandler(null, handler);
                }

                var disconnectedEvent = gameEventsType.GetEvent("OnDisconnectedFromServer");
                if (disconnectedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        disconnectedEvent.EventHandlerType,
                        this,
                        typeof(RangefinderMod).GetMethod(nameof(HandleDisconnectedFromServer),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    disconnectedEvent.AddEventHandler(null, handler);
                }

                var spawnedEvent = gameEventsType.GetEvent("OnLocalPlayerSpawned");
                if (spawnedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        spawnedEvent.EventHandlerType,
                        this,
                        typeof(RangefinderMod).GetMethod(nameof(HandleLocalPlayerSpawned),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    spawnedEvent.AddEventHandler(null, handler);
                }

                var generalSpawnedEvent = gameEventsType.GetEvent("OnPlayerSpawned");
                if (generalSpawnedEvent != null)
                {
                    var handler = Delegate.CreateDelegate(
                        generalSpawnedEvent.EventHandlerType,
                        this,
                        typeof(RangefinderMod).GetMethod(nameof(HandlePlayerSpawned),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    generalSpawnedEvent.AddEventHandler(null, handler);
                }

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

                Log.LogInfo($"[Rangefinder] Subscribed to game events. Master login: {_isMasterLoggedIn}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[Rangefinder] Error subscribing to events: {ex.Message}");
            }
        }

        private void HandleConnectedToServer(ulong steamId)
        {
            Log.LogInfo($"[Rangefinder] Connected to server (Steam ID: {steamId})");
            _hasSpawned = false;
            ResetState();
        }

        private void HandleDisconnectedFromServer()
        {
            Log.LogInfo("[Rangefinder] Disconnected from server");
            _hasSpawned = false;
            ResetState();
        }

        private void HandleLocalPlayerSpawned(int playerId, FactionCountry faction, PlayerClass playerClass)
        {
            Log.LogInfo($"[Rangefinder] Local player spawned (ID: {playerId}, Class: {playerClass}, Faction: {faction})");
            _localPlayerId = playerId;
            _localFaction = faction;
            _hasSpawned = true;
            _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
            _rangefinderCreated = false;
        }

        private void HandlePlayerSpawned(int playerId, int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            if (playerObject != null && _isMasterLoggedIn)
            {
                _trackedPlayers[playerId] = new TrackedPlayer
                {
                    PlayerId = playerId,
                    Faction = faction,
                    PlayerObject = playerObject
                };
            }

            if (!_hasSpawned && playerObject != null)
            {
                Log.LogInfo($"[Rangefinder] Player spawned (fallback, ID: {playerId}, Class: {playerClass})");
                _localFaction = faction;
                _localPlayerId = playerId;
                _hasSpawned = true;
                _setupDelayTimer = SETUP_DELAY_AFTER_SPAWN;
                _rangefinderCreated = false;
            }
        }

        private void ResetState()
        {
            _rangefinderCreated = false;

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

        public static void DoUpdate()
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance.DoUpdateInternal();
        }

        private void DoUpdateInternal()
        {
            try
            {
                if (!_isMasterLoggedIn) return;

                if (Input.GetKeyDown(KeyCode.F10))
                {
                    Log.LogInfo("[Rangefinder] F10 pressed - Reloading...");
                    ResetState();
                    CheckMasterLoginStatus();
                    _hasSpawned = true;
                    _setupDelayTimer = 0.1f;
                }

                if (_hasSpawned && _setupDelayTimer > 0)
                {
                    _setupDelayTimer -= Time.deltaTime;
                    if (_setupDelayTimer <= 0)
                    {
                        SetupRangefinder();
                    }
                }

                if (_rangefinderCreated)
                {
                    UpdateRangefinder();
                }
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 1)
                {
                    Log.LogError($"[Rangefinder] Update() exception: {ex.Message}");
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

        private void SetupRangefinder()
        {
            Log.LogInfo("[Rangefinder] Setting up rangefinder...");

            GameObject crosshairPanel = GameObject.Find(CROSSHAIR_PANEL_PATH);
            if (crosshairPanel == null)
            {
                crosshairPanel = GameObject.Find("Crosshair Panel");
            }

            if (crosshairPanel == null)
            {
                Log.LogWarning("[Rangefinder] Crosshair panel not found - will retry on next spawn");
                return;
            }

            CreateRangefinderTexts(crosshairPanel);
        }

        private void CreateRangefinderTexts(GameObject crosshairPanel)
        {
            Log.LogInfo("[Rangefinder] Creating rangefinder text elements...");

            try
            {
                DestroyExistingRangefinderTexts(crosshairPanel);
                _rangefinderTexts.Clear();

                string[] crosshairNames = {
                    MUSKET_CROSSHAIR_NAME,
                    BLUNDERBUSS_CROSSHAIR_NAME,
                    PISTOL_CROSSHAIR_NAME,
                    RIFLE_CROSSHAIR_NAME,
                    CUSTOM_CROSSHAIR_NAME
                };

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

                    Transform parentTransform = FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME);
                    if (parentTransform == null)
                    {
                        parentTransform = crosshairTransform;
                    }

                    GameObject textObj = new GameObject("RangefinderText");
                    textObj.transform.SetParent(parentTransform, false);

                    RectTransform rectTransform = textObj.AddComponent<RectTransform>();
                    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    rectTransform.pivot = new Vector2(0f, 0.5f);
                    rectTransform.anchoredPosition = new Vector2(50f, 0f);
                    rectTransform.sizeDelta = new Vector2(150f, 40f);

                    Text textComponent = textObj.AddComponent<Text>();
                    textComponent.text = "---";
                    textComponent.font = font;
                    textComponent.fontSize = 18;
                    textComponent.color = new Color(1f, 1f, 0f, 1f);
                    textComponent.alignment = TextAnchor.MiddleLeft;
                    textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                    textComponent.verticalOverflow = VerticalWrapMode.Overflow;
                    textComponent.raycastTarget = false;

                    Outline outline = textObj.AddComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(1f, -1f);

                    _rangefinderTexts[crosshairName] = textComponent;
                    Log.LogInfo($"[Rangefinder] Created rangefinder for {crosshairName}");
                }

                if (_rangefinderTexts.Count > 0)
                {
                    _rangefinderCreated = true;
                    HookTrajectoryRenderer();
                    Log.LogInfo($"[Rangefinder] Active with {_rangefinderTexts.Count} text elements");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[Rangefinder] Error creating rangefinder texts: {ex.Message}");
            }
        }

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

                    Transform[] parents = new Transform[] {
                        FindChildByName(crosshairTransform, CROSSHAIR_IMAGE_NAME),
                        crosshairTransform
                    };

                    foreach (Transform parent in parents)
                    {
                        if (parent == null) continue;

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
                    Log.LogInfo($"[Rangefinder] Cleaned up {destroyed} old rangefinder text(s)");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[Rangefinder] Error cleaning up old rangefinder texts: {ex.Message}");
            }
        }

        private void HookTrajectoryRenderer()
        {
            _trajectoryHooked = false;
            Log.LogInfo("[Rangefinder] Trajectory renderer will attach to main camera when found");
        }

        private void AttachTrajectoryToCamera()
        {
            if (_trajectoryHooked || _mainCamera == null) return;

            var existing = _mainCamera.GetComponent<TrajectoryRenderer>();
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing);
            }

            _mainCamera.gameObject.AddComponent<TrajectoryRenderer>();
            _trajectoryHooked = true;
            Log.LogInfo($"[Rangefinder] TrajectoryRenderer attached to camera '{_mainCamera.name}'");
        }

        private void UnhookTrajectoryRenderer()
        {
            _trajectoryHooked = false;
        }

        public static void DoRenderTrajectory(Camera cam)
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance.RenderTrajectoryInternal(cam);
        }

        private void RenderTrajectoryInternal(Camera cam)
        {
            try
            {
                if (!_isMasterLoggedIn) return;
                if (!_hasSpawned || !_rangefinderCreated) return;
                if (cam == null) return;
                if (_mainCamera != null && cam != _mainCamera) return;

                if (!_trajectoryLoggedOnce)
                {
                    _trajectoryLoggedOnce = true;
                    Log.LogInfo($"[Rangefinder] Trajectory renderer active (muzzleV={_muzzleVelocity}, gravity={_bulletGravity})");
                }

                if (_trajectoryMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null)
                    {
                        Log.LogWarning("[Rangefinder] Hidden/Internal-Colored shader not found");
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

                float maxRange = (_currentDistance > 10f) ? _currentDistance : TRAJECTORY_MAX_RANGE;
                float maxTime = maxRange / _muzzleVelocity;
                float startTime = TRAJECTORY_START_OFFSET / _muzzleVelocity;

                _trajectoryMaterial.SetPass(0);
                GL.PushMatrix();

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

                    float alpha = Mathf.Lerp(0.9f, 0.15f, fraction);
                    GL.Color(new Color(0.2f, 1f, 0.4f, alpha));
                    GL.Vertex(prevPoint);
                    GL.Vertex(point);

                    prevPoint = point;
                }

                GL.End();

                Vector3 impactPoint = camPos
                    + forward * (_muzzleVelocity * maxTime)
                    + Vector3.down * (0.5f * _bulletGravity * maxTime * maxTime);

                Vector3 camRight = cam.transform.right;
                Vector3 camUp = cam.transform.up;
                float crossSize = Mathf.Clamp(maxRange * 0.003f, 0.15f, 1.5f);

                GL.Begin(GL.LINES);
                GL.Color(new Color(1f, 0.3f, 0.3f, 0.9f));

                GL.Vertex(impactPoint - camRight * crossSize);
                GL.Vertex(impactPoint + camRight * crossSize);
                GL.Vertex(impactPoint - camUp * crossSize);
                GL.Vertex(impactPoint + camUp * crossSize);

                GL.End();
                GL.PopMatrix();
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 1)
                {
                    Log.LogError($"[Rangefinder] Trajectory render error: {ex.Message}");
                }
            }
        }

        private void UpdateRangefinder()
        {
            if (Time.time - _lastRaycastTime < RAYCAST_INTERVAL)
                return;

            _lastRaycastTime = Time.time;

            try
            {
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

                if (!_trajectoryHooked && _isMasterLoggedIn)
                {
                    AttachTrajectoryToCamera();
                }

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

                Ray ray = _mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
                RaycastHit hit;

                float distance = 0f;
                if (Physics.Raycast(ray, out hit, 2000f, _raycastLayerMask, QueryTriggerInteraction.Ignore))
                {
                    distance = hit.distance;
                }

                _currentDistance = distance;

                string distanceText = distance > 0f ? $"{distance:F0}m" : "---";

                string aimGuidance = FindEnemyAimGuidance(ray.direction);
                if (!string.IsNullOrEmpty(aimGuidance))
                {
                    distanceText += "\n" + aimGuidance;
                }

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
                Log.LogError($"[Rangefinder] Error updating rangefinder: {ex.Message}");
            }
        }

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

            float timeToTarget = nearestDist / _muzzleVelocity;
            Vector3 bulletAtTarget = camPos
                + aimDir * nearestDist
                + Vector3.down * (0.5f * _bulletGravity * timeToTarget * timeToTarget);

            float verticalMiss = bulletAtTarget.y - nearestEnemyCenter.y;

            float heightDiff = nearestEnemyCenter.y - camPos.y;
            string elevText = "";
            if (Mathf.Abs(heightDiff) > 2f)
            {
                elevText = heightDiff > 0f ? "\u25B2" : "\u25BC";
            }

            float absMiss = Mathf.Abs(verticalMiss);

            if (absMiss < MIN_DROP_DISPLAY)
            {
                return $"<color=#44FF44>E:{nearestDist:F0}m{elevText} \u25CF</color>";
            }
            else if (verticalMiss < 0f)
            {
                return $"<color=#FF6666>E:{nearestDist:F0}m{elevText} \u2191{absMiss:F1}m</color>";
            }
            else
            {
                return $"<color=#FFAA44>E:{nearestDist:F0}m{elevText} \u2193{absMiss:F1}m</color>";
            }
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

        void OnDestroy()
        {
            UnhookTrajectoryRenderer();
            if (_trajectoryMaterial != null)
            {
                UnityEngine.Object.Destroy(_trajectoryMaterial);
                _trajectoryMaterial = null;
            }
        }
    }
}
