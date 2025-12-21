using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using AdvancedAdminUI.Settings;
using AdvancedAdminUI.Utils;
using HoldfastSharedMethods;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Detects and visualizes AFK players who haven't moved in a set time period
    /// </summary>
    public class AFKIndicatorFeature : IAdminFeature
    {
        public string FeatureName => "AFK Indicator";
        
        private bool _isEnabled = false;
        // Settings are now loaded from ClassSettings.cs - keeping these for backward compatibility
        private const float AFK_TIME_THRESHOLD = 10.0f; // Default 10 seconds without movement
        private const float MOVEMENT_THRESHOLD = 0.1f; // Default 0.1 meters movement threshold
        private const float UPDATE_INTERVAL = 0.5f; // Update every 0.5 seconds
        private const float CIRCLE_RADIUS = 2.0f; // Increased from 1.0f for better visibility
        private const float LINE_WIDTH = 0.3f; // Increased from 0.1f for thicker, more visible lines
        private const int CIRCLE_SEGMENTS = 64; // Increased for smoother circle
        
        private float _lastUpdateTime = 0f;
        
        // Track player data: InstanceID -> AFKData
        private class AFKData
        {
            public Transform Transform;
            public Vector3 LastPosition;
            public float TimeStationary = 0f;
            public GameObject CircleObject = null;
            public LineRenderer CircleRenderer = null;
            public bool IsAFK = false;
            public string ClassName = ""; // Store player class for threshold lookup
        }
        
        private Dictionary<int, AFKData> _playerData = new Dictionary<int, AFKData>(); // Key: instanceId
        private Dictionary<int, int> _playerIdToInstanceId = new Dictionary<int, int>(); // Key: holdfastPlayerId, Value: instanceId
        private Material _greyMaterial; // Grey circle for AFK players
        
        public bool IsEnabled => _isEnabled;

        public void Enable()
        {
            _isEnabled = true;
            CreateMaterial();
            
            // Subscribe to OnPlayerSpawned to start tracking players when they spawn
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerSpawnedCallbacks.Add(OnPlayerSpawned);
            
            // Subscribe to OnRoundDetails to reset on new round (keep player IDs)
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundDetailsCallbacks.Add(OnRoundDetails);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndFactionWinnerCallbacks.Add(OnRoundEndFactionWinner); // Wrapper that calls OnRoundDetails
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndPlayerWinnerCallbacks.Add(OnRoundEndPlayerWinner); // Wrapper that calls OnRoundDetails
            
            // Subscribe to OnPlayerKilledPlayer to remove dead players
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerKilledPlayerCallbacks.Add(OnPlayerKilledPlayer);
            
            // Subscribe to OnPlayerLeft to remove players who left
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerLeftCallbacks.Add(OnPlayerLeft);
            
            // Subscribe to client connection changes to cleanup on disconnect
            AdvancedAdminUI.Utils.PlayerEventManager._onClientConnectionChangedCallbacks.Add(OnClientConnectionChanged);
            
            // Use PlayerTracker to get all existing players
            var allPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetAllPlayers();
            int addedCount = 0;
            
            foreach (var kvp in allPlayers)
            {
                var playerData = kvp.Value;
                if (playerData.PlayerObject == null || playerData.PlayerTransform == null)
                    continue;
                
                int instanceId = playerData.PlayerObject.GetInstanceID();
                
                // Skip Dragoon and Hussar - they are excluded from AFK detection
                string className = playerData.ClassName ?? "";
                if (className.IndexOf("dragoon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("hussar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                
                // Skip local player
                if (Camera.main != null)
                {
                    float distToCamera = Vector3.Distance(playerData.PlayerTransform.position, Camera.main.transform.position);
                    if (distToCamera < 3f)
                        continue;
                }
                
                if (!_playerData.ContainsKey(instanceId))
                {
                    _playerData[instanceId] = new AFKData
                    {
                        Transform = playerData.PlayerTransform,
                        LastPosition = playerData.PlayerTransform.position,
                        TimeStationary = 0f,
                        IsAFK = false
                    };
                    _playerIdToInstanceId[playerData.PlayerId] = instanceId;
                    addedCount++;
                }
            }
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Enabled - Found {addedCount} existing player(s)");
        }

        public void Disable()
        {
            _isEnabled = false;
            
            // Unsubscribe from all events
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerSpawnedCallbacks.Remove(OnPlayerSpawned);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundDetailsCallbacks.Remove(OnRoundDetails);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndFactionWinnerCallbacks.Remove(OnRoundEndFactionWinner);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndPlayerWinnerCallbacks.Remove(OnRoundEndPlayerWinner);
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerKilledPlayerCallbacks.Remove(OnPlayerKilledPlayer);
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerLeftCallbacks.Remove(OnPlayerLeft);
            AdvancedAdminUI.Utils.PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            CleanupAllIndicators();
            DestroyMaterial();
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Disabled");
        }
        
        /// <summary>
        /// Called when client connects to or disconnects from a server
        /// </summary>
        private void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Cleanup all tracking data on disconnect
                CleanupAllIndicators();
            }
        }

        private float _lastScanTime = 0f;
        private const float SCAN_INTERVAL = 1.0f; // Check for new players every 1 second
        
        public void OnUpdate()
        {
            if (!_isEnabled)
                return;

            // PERFORMANCE: Only check for new players periodically, not every frame
            // With 300 players, iterating every frame causes massive lag
            if (Time.time - _lastScanTime >= SCAN_INTERVAL)
            {
                _lastScanTime = Time.time;
                
                // Cache Camera.main to avoid repeated lookups
                Camera mainCamera = Camera.main;
                Vector3 cameraPos = mainCamera != null ? mainCamera.transform.position : Vector3.zero;
                float localPlayerSqrDist = 3f * 3f; // 9 sqr meters
                
                // Check for new players from PlayerTracker
                var allPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetAllPlayers();
                foreach (var kvp in allPlayers)
                {
                    var playerData = kvp.Value;
                    if (playerData.PlayerObject == null || playerData.PlayerTransform == null)
                        continue;

                    int instanceId = playerData.PlayerObject.GetInstanceID();
                    
                    // Skip Dragoon and Hussar - they are excluded from AFK detection
                    string className = playerData.ClassName ?? "";
                    if (className.IndexOf("dragoon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        className.IndexOf("hussar", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }
                    
                    // Skip local player using sqrMagnitude (faster than Distance)
                    if (mainCamera != null)
                    {
                        float sqrDistToCamera = (playerData.PlayerTransform.position - cameraPos).sqrMagnitude;
                        if (sqrDistToCamera < localPlayerSqrDist)
                            continue;
                    }
                    
                    // Add if not already tracked
                    if (!_playerData.ContainsKey(instanceId))
                    {
                        _playerData[instanceId] = new AFKData
                        {
                            Transform = playerData.PlayerTransform,
                            LastPosition = playerData.PlayerTransform.position,
                            TimeStationary = 0f,
                            IsAFK = false,
                            ClassName = playerData.ClassName ?? ""
                        };
                        _playerIdToInstanceId[playerData.PlayerId] = instanceId;
                    }
                    else
                    {
                        // Update class name if it changed
                        _playerData[instanceId].ClassName = playerData.ClassName ?? "";
                        // Update mapping in case playerId changed (shouldn't happen, but be safe)
                        _playerIdToInstanceId[playerData.PlayerId] = instanceId;
                    }
                }
            }

            // Update existing players periodically
            if (Time.time - _lastUpdateTime >= UPDATE_INTERVAL)
            {
                _lastUpdateTime = Time.time;
                UpdateExistingPlayers();
            }
        }

        public void OnGUI()
        {
            if (!_isEnabled || Camera.main == null)
                return;

            // Draw AFK text above players
            foreach (var kvp in _playerData)
            {
                AFKData data = kvp.Value;
                if (!data.IsAFK || data.Transform == null || data.CircleObject == null)
                    continue;

                Vector3 worldPos = data.Transform.position + Vector3.up * 2.5f; // 2.5m above player
                Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
                
                // Only draw if on screen
                if (screenPos.z > 0 && screenPos.x > 0 && screenPos.x < Screen.width && screenPos.y > 0 && screenPos.y < Screen.height)
                {
                    // Draw background box for better visibility
                    GUI.color = new Color(0f, 0f, 0f, 0.7f); // Semi-transparent black background
                    GUI.Box(new Rect(screenPos.x - 60, Screen.height - screenPos.y - 30, 120, 40), "");
                    
                    // Draw grey text
                    GUI.color = new Color(0.7f, 0.7f, 0.7f, 1f); // Grey
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontSize = 24; // Increased from 16 for better visibility
                    style.fontStyle = FontStyle.Bold;
                    style.alignment = TextAnchor.MiddleCenter;
                    style.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f); // Grey
                    
                    GUI.Label(new Rect(screenPos.x - 60, Screen.height - screenPos.y - 30, 120, 40), "AFK", style);
                    GUI.color = Color.white;
                }
            }
        }

        public void OnApplicationQuit()
        {
            CleanupAllIndicators();
            DestroyMaterial();
        }
        
        private void DestroyMaterial()
        {
            if (_greyMaterial != null)
            {
                UnityEngine.Object.Destroy(_greyMaterial);
                _greyMaterial = null;
            }
        }
        
        /// <summary>
        /// Called when round ends with faction winner - DON'T cleanup here to avoid lag spike
        /// Cleanup will happen on the actual OnRoundDetails when the new round starts
        /// </summary>
        private void OnRoundEndFactionWinner(HoldfastSharedMethods.FactionCountry factionCountry, HoldfastSharedMethods.FactionRoundWinnerReason reason)
        {
            // Do nothing - cleanup will happen when OnRoundDetails is called for the NEW round
        }
        
        /// <summary>
        /// Called when round ends with player winner - DON'T cleanup here to avoid lag spike
        /// Cleanup will happen on the actual OnRoundDetails when the new round starts
        /// </summary>
        private void OnRoundEndPlayerWinner(int playerId)
        {
            // Do nothing - cleanup will happen when OnRoundDetails is called for the NEW round
        }
        
        /// <summary>
        /// Called when a new round starts - cleanup everything except tracked player IDs
        /// </summary>
        private void OnRoundDetails(int roundId, string serverName, string mapName, HoldfastSharedMethods.FactionCountry attackingFaction, HoldfastSharedMethods.FactionCountry defendingFaction, HoldfastSharedMethods.GameplayMode gameplayMode, HoldfastSharedMethods.GameType gameType)
        {
            // Skip dummy round details (from old round end code)
            if (roundId < 0 || string.IsNullOrEmpty(mapName))
                return;
            
            // Destroy visual objects - use Destroy (not DestroyImmediate) for better performance
            foreach (var kvp in _playerData)
            {
                try
                {
                    if (kvp.Value?.CircleObject != null)
                    {
                        UnityEngine.Object.Destroy(kvp.Value.CircleObject);
                        kvp.Value.CircleObject = null;
                        kvp.Value.CircleRenderer = null;
                    }
                }
                catch { }
            }
            
            // Clean up any orphaned GameObjects
            CleanupOrphanedGameObjects();
            
            // Clear tracking data
            _playerData.Clear();
            _playerIdToInstanceId.Clear();
        }
        
        /// <summary>
        /// Re-scan PlayerTracker for existing players and start tracking them
        /// Called after round cleanup to immediately start tracking for the new round
        /// </summary>
        private void RescanForExistingPlayers()
        {
            var allPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetAllPlayers();
            int addedCount = 0;
            
            foreach (var kvp in allPlayers)
            {
                var playerData = kvp.Value;
                if (playerData.PlayerObject == null || playerData.PlayerTransform == null)
                    continue;
                
                int instanceId = playerData.PlayerObject.GetInstanceID();
                
                // Skip Dragoon and Hussar - they are excluded from AFK detection
                string className = playerData.ClassName ?? "";
                if (className.IndexOf("dragoon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("hussar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                
                // Skip local player
                if (Camera.main != null)
                {
                    float distToCamera = Vector3.Distance(playerData.PlayerTransform.position, Camera.main.transform.position);
                    if (distToCamera < 3f)
                        continue;
                }
                
                // Add if not already tracked (shouldn't be, since we just cleared everything)
                if (!_playerData.ContainsKey(instanceId))
                {
                    _playerData[instanceId] = new AFKData
                    {
                        Transform = playerData.PlayerTransform,
                        LastPosition = playerData.PlayerTransform.position,
                        TimeStationary = 0f,
                        IsAFK = false,
                        ClassName = playerData.ClassName ?? ""
                    };
                    _playerIdToInstanceId[playerData.PlayerId] = instanceId;
                    addedCount++;
                }
            }
            
            if (HoldfastScriptMod.IsRCLoggedIn())
                AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] ✓ Re-scanned and started tracking {addedCount} existing player(s) for new round");
        }
        
        /// <summary>
        /// Called when a player spawns - start tracking them for AFK detection
        /// </summary>
        private void OnPlayerSpawned(int playerId, GameObject playerObject)
        {
            if (playerObject == null)
                return;
            
            int instanceId = playerObject.GetInstanceID();
            
            // Skip local player
            if (Camera.main != null)
            {
                float distToCamera = Vector3.Distance(playerObject.transform.position, Camera.main.transform.position);
                if (distToCamera < 3f)
                    return;
            }
            
            // Get player data from PlayerTracker
            var playerData = AdvancedAdminUI.Utils.PlayerTracker.GetPlayer(playerId);
            if (playerData == null)
                return;
            
            // Skip Dragoon and Hussar - they are excluded from AFK detection
            string className = playerData.ClassName ?? "";
            if (className.IndexOf("dragoon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("hussar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }
            
            // Add if not already tracked
            if (!_playerData.ContainsKey(instanceId))
            {
                _playerData[instanceId] = new AFKData
                {
                    Transform = playerObject.transform,
                    LastPosition = playerObject.transform.position,
                    TimeStationary = 0f,
                    IsAFK = false,
                    ClassName = playerData.ClassName ?? ""
                };
                _playerIdToInstanceId[playerId] = instanceId;
                // Silent log - only log if RC logged in to reduce spam
                // if (AdvancedAdminUI.Utils.HoldfastScriptMod.IsRCLoggedIn())
                //     AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Tracking player: Id={playerId}");
            }
            else
            {
                // Update existing entry with latest data
                var data = _playerData[instanceId];
                data.Transform = playerObject.transform;
                data.LastPosition = playerObject.transform.position;
                data.ClassName = playerData.ClassName ?? "";
                data.TimeStationary = 0f; // Reset AFK timer on respawn
                if (data.IsAFK)
                {
                    data.IsAFK = false;
                    RemoveAFKIndicator(instanceId, data);
                }
                _playerIdToInstanceId[playerId] = instanceId;
            }
        }
        
        /// <summary>
        /// Called when a player leaves - remove their tracking and objects
        /// </summary>
        private void OnPlayerLeft(int playerId)
        {
            if (HoldfastScriptMod.IsRCLoggedIn())
                AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Player left: {playerId} - removing tracking and objects");
            RemovePlayerByPlayerId(playerId);
        }
        
        /// <summary>
        /// Called when a player kills another player - remove victim's tracking and objects
        /// </summary>
        private void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, HoldfastSharedMethods.EntityHealthChangedReason reason, string details)
        {
            RemovePlayerByPlayerId(victimPlayerId);
        }
        
        /// <summary>
        /// Remove a player's AFK indicator by Holdfast playerId
        /// Handles both alive and dead players (dead players have null PlayerObject)
        /// </summary>
        private void RemovePlayerByPlayerId(int holdfastPlayerId)
        {
            // First try to get instanceId from our mapping (works even if player is dead)
            if (_playerIdToInstanceId.TryGetValue(holdfastPlayerId, out int instanceId))
            {
                if (_playerData.TryGetValue(instanceId, out AFKData data))
                {
                    RemoveAFKIndicator(instanceId, data);
                    _playerData.Remove(instanceId);
                    _playerIdToInstanceId.Remove(holdfastPlayerId);
                    if (HoldfastScriptMod.IsRCLoggedIn())
                        AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Removed AFK tracking for player {holdfastPlayerId} (instanceId: {instanceId})");
                    return;
                }
            }
            
            // Fallback: try to get from PlayerTracker if player is still alive
            var playerData = AdvancedAdminUI.Utils.PlayerTracker.GetPlayer(holdfastPlayerId);
            if (playerData != null && playerData.PlayerObject != null)
            {
                instanceId = playerData.PlayerObject.GetInstanceID();
                if (_playerData.TryGetValue(instanceId, out AFKData data))
                {
                    RemoveAFKIndicator(instanceId, data);
                    _playerData.Remove(instanceId);
                    if (_playerIdToInstanceId.ContainsKey(holdfastPlayerId))
                        _playerIdToInstanceId.Remove(holdfastPlayerId);
                }
            }
        }
        
        /// <summary>
        /// Called when time remaining updates - when it hits 0, clean up all visual objects but keep tracking
        /// </summary>
        private void OnUpdateTimeRemaining(float timeRemaining)
        {
            if (timeRemaining <= 0f)
            {
                if (HoldfastScriptMod.IsRCLoggedIn())
                    AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Time remaining hit 0 - cleaning up all visual objects (keeping tracking)");
                CleanupAllVisualObjects();
            }
        }
        
        /// <summary>
        /// Clean up all visual objects (circles) but keep tracking data
        /// Public so it can be called from global cleanup
        /// </summary>
        public void CleanupAllVisualObjects()
        {
            // Remove all AFK indicators but keep tracking data
            foreach (var kvp in _playerData)
            {
                try
                {
                    if (kvp.Value?.CircleObject != null)
                    {
                        UnityEngine.Object.Destroy(kvp.Value.CircleObject);
                        kvp.Value.CircleObject = null;
                        kvp.Value.CircleRenderer = null;
                    }
                    if (kvp.Value != null)
                    {
                        kvp.Value.IsAFK = false;
                        kvp.Value.TimeStationary = 0f;
                    }
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error cleaning up visual object for player {kvp.Key}: {ex.Message}");
                }
            }
        }

        private void UpdateExistingPlayers()
        {
            if (_playerData.Count == 0)
                return;

            float deltaTime = Time.time - (_lastUpdateTime - UPDATE_INTERVAL);
            if (deltaTime <= 0)
                deltaTime = UPDATE_INTERVAL;

            List<int> toRemove = new List<int>();
            
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                AFKData data = kvp.Value;

                if (data.Transform == null || data.Transform.gameObject == null)
                {
                    toRemove.Add(playerId);
                    continue;
                }

                Vector3 currentPos = data.Transform.position;
                float distanceMoved = Vector3.Distance(currentPos, data.LastPosition);
                
                // Get class-specific thresholds
                float movementThreshold = ClassSettings.GetMovementThreshold(data.ClassName);
                float afkTimeThreshold = ClassSettings.GetAfkTimeThreshold(data.ClassName);
                
                // Check if player has moved significantly
                if (distanceMoved > movementThreshold)
                {
                    // Player moved - reset AFK timer
                    data.LastPosition = currentPos;
                    data.TimeStationary = 0f;
                    
                    if (data.IsAFK)
                    {
                        data.IsAFK = false;
                        RemoveAFKIndicator(playerId, data);
                    }
                }
                else
                {
                    // Player hasn't moved - increment AFK timer
                    data.TimeStationary += deltaTime;
                    
                    if (data.TimeStationary >= afkTimeThreshold && !data.IsAFK)
                    {
                        data.IsAFK = true;
                        CreateAFKIndicator(data.Transform, playerId, data);
                    }
                }
            }

            foreach (int id in toRemove)
            {
                if (_playerData.TryGetValue(id, out AFKData data))
                {
                    RemoveAFKIndicator(id, data);
                    _playerData.Remove(id);
                }
            }
        }

        private void CreateMaterial()
        {
            if (_greyMaterial != null)
                return;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Diffuse");
            
            if (shader != null)
            {
                _greyMaterial = new Material(shader);
                _greyMaterial.color = new Color(0.5f, 0.5f, 0.5f, 1f); // Grey color for AFK
                _greyMaterial.SetFloat("_Mode", 0);
            }
        }

        private void CreateAFKIndicator(Transform playerTransform, int playerId, AFKData data)
        {
            if (data.CircleObject != null || _greyMaterial == null)
                return;

            Vector3 playerPos = playerTransform.position;
            
            GameObject circleObj = new GameObject($"AFKIndicator_{playerId}");
            circleObj.layer = 5; // UI layer - excluded from minimap
            circleObj.transform.position = new Vector3(playerPos.x, playerPos.y + 0.2f, playerPos.z);
            circleObj.transform.rotation = Quaternion.identity;

            LineRenderer lr = circleObj.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.material = _greyMaterial;
            lr.startWidth = LINE_WIDTH;
            lr.endWidth = LINE_WIDTH;
            lr.positionCount = CIRCLE_SEGMENTS + 1;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = true;

            // Create circle points
            for (int i = 0; i <= CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)i / CIRCLE_SEGMENTS * 2f * Mathf.PI;
                float x = Mathf.Cos(angle) * CIRCLE_RADIUS;
                float z = Mathf.Sin(angle) * CIRCLE_RADIUS;
                lr.SetPosition(i, new Vector3(x, 0, z));
            }

            data.CircleObject = circleObj;
            data.CircleRenderer = lr;
        }

        private void RemoveAFKIndicator(int playerId, AFKData data)
        {
            if (data?.CircleObject != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(data.CircleObject);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error destroying circle object: {ex.Message}");
                }
                data.CircleObject = null;
                data.CircleRenderer = null;
            }
            if (data != null)
            {
                data.IsAFK = false;
                data.TimeStationary = 0f;
            }
        }

        private void CleanupAllIndicators()
        {
            foreach (var kvp in _playerData)
            {
                try
                {
                    RemoveAFKIndicator(kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error cleaning up indicator for player {kvp.Key}: {ex.Message}");
                }
            }
            _playerData.Clear();
        }
        
        /// <summary>
        /// Clean up any orphaned GameObjects that might exist from players who left
        /// This is a safety net to catch GameObjects that weren't properly cleaned up in OnPlayerLeft
        /// </summary>
        private void CleanupOrphanedGameObjects()
        {
            try
            {
                // Find all GameObjects with names matching our patterns
                GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                int cleanedCount = 0;
                
                foreach (GameObject obj in allObjects)
                {
                    if (obj == null)
                        continue;
                    
                    string objName = obj.name;
                    
                    // Clean up orphaned AFK indicators
                    if (objName.StartsWith("AFKIndicator_"))
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                        cleanedCount++;
                    }
                }
                
                if (cleanedCount > 0 && HoldfastScriptMod.IsRCLoggedIn())
                {
                    AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Cleaned up {cleanedCount} orphaned GameObject(s) from previous round");
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error cleaning up orphaned GameObjects: {ex.Message}");
            }
        }
    }
}

