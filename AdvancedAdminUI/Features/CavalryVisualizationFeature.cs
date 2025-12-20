using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Rendering;
using HoldfastSharedMethods;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Visualizes cavalry (horses) with 3D cylinders
    /// </summary>
    public class CavalryVisualizationFeature : IAdminFeature
    {
        public string FeatureName => "Cavalry Visualization";
        
        private bool _isEnabled = false;
        private float _lastScanTime = 0f;
        private const float CLEANUP_INTERVAL = 5.0f; // Cleanup invalid cylinders every 5 seconds
        private const float UPDATE_INTERVAL = 0.2f; // Update cylinder positions every 0.2s
        private float _lastUpdateTime = 0f;
        private const int CIRCLE_SEGMENTS = 64;
        private const float CYLINDER_RADIUS = 12.5f; // 25 meters diameter / 2
        private const float LINE_WIDTH = 0.15f;
        private const float MIN_HEIGHT_OFFSET = 0.5f;
        // No limit on tracked cavalry players - can track unlimited Hussars and Dragoons
        
        private Dictionary<int, KeyValuePair<Transform, GameObject>> _rings = new Dictionary<int, KeyValuePair<Transform, GameObject>>();
        
        private int _localPlayerHorseId = -1;
        private float _lastLocalPlayerCheck = 0f;
        private const float LOCAL_PLAYER_CHECK_INTERVAL = 2.0f;
        
        private readonly string[] _targetPatterns = new string[]
        {
            "Horse_Holdfast"
        };

        public bool IsEnabled => _isEnabled;

        public void Enable()
        {
            _isEnabled = true;
            
            // Subscribe to OnPlayerSpawned to start tracking cavalry players when they spawn
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
            
            // Use PlayerTracker to get all existing cavalry players
            var allPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetAllPlayers();
            int addedCount = 0;
            
            foreach (var kvp in allPlayers)
            {
                var playerData = kvp.Value;
                string className = playerData.ClassName ?? "";
                
                // Check if player is a cavalry class
                if (className.IndexOf("Hussar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("Dragoon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (playerData.PlayerObject != null && playerData.PlayerTransform != null)
                    {
                        AddCavalryPlayer(playerData.PlayerObject);
                        addedCount++;
                    }
                }
            }
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Enabled - Found {addedCount} existing cavalry player(s)");
        }

        public void Disable()
        {
            _isEnabled = false;
            
            // Unsubscribe from all events to prevent new cylinders from being created
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerSpawnedCallbacks.Remove(OnPlayerSpawned);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundDetailsCallbacks.Remove(OnRoundDetails);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndFactionWinnerCallbacks.Remove(OnRoundEndFactionWinner);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndPlayerWinnerCallbacks.Remove(OnRoundEndPlayerWinner);
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerKilledPlayerCallbacks.Remove(OnPlayerKilledPlayer);
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerLeftCallbacks.Remove(OnPlayerLeft);
            AdvancedAdminUI.Utils.PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            // Clean up all rings AND orphaned GameObjects
            CleanupAllRings();
            CleanupOrphanedGameObjects();
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Disabled - unsubscribed from events and cleaned up all cylinders");
        }
        
        /// <summary>
        /// Called when client connects to or disconnects from a server
        /// </summary>
        private void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Cleanup all rings on disconnect
                CleanupAllRings();
                CleanupOrphanedGameObjects();
                _localPlayerHorseId = -1;
            }
        }
        
        private const float SCAN_INTERVAL = 1.0f; // Check for new cavalry players every 1 second
        
        public void OnUpdate()
        {
            if (!_isEnabled)
                return;

            // PERFORMANCE: Only check for new players periodically, not every frame
            // With 300 players, iterating every frame causes massive lag
            if (Time.time - _lastScanTime >= SCAN_INTERVAL)
            {
                _lastScanTime = Time.time;
                
                // Check for new cavalry players from PlayerTracker
                var allPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetAllPlayers();
                foreach (var kvp in allPlayers)
                {
                    var playerData = kvp.Value;
                    if (playerData.PlayerObject == null || playerData.PlayerTransform == null)
                        continue;
                        
                    string className = playerData.ClassName ?? "";
                    
                    // Check if player is a cavalry class (optimized string check)
                    if (className.IndexOf("Hussar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        className.IndexOf("Dragoon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int instanceId = playerData.PlayerObject.GetInstanceID();
                        
                        // Add if not already tracked
                        if (!_rings.ContainsKey(instanceId))
                        {
                            AddCavalryPlayer(playerData.PlayerObject);
                        }
                    }
                }
            }

            // Only update cylinder positions periodically, not every frame
            if (Time.time - _lastUpdateTime >= UPDATE_INTERVAL)
            {
                _lastUpdateTime = Time.time;
                UpdateExistingCylinders();
            }

            // No scanning needed - all tracking is event-driven via PlayerTracker
            // Just do periodic cleanup of invalid cylinders
            if (Time.time - _lastScanTime >= CLEANUP_INTERVAL)
            {
                _lastScanTime = Time.time;
                CleanupInvalidCylinders();
            }
        }
        
        private void AddCavalryPlayer(GameObject playerObject)
        {
            // Don't create cylinders if feature is disabled
            if (!_isEnabled)
                return;
                
            if (playerObject == null || playerObject.transform == null)
                return;

            // Extra validation - check if the GameObject is still valid
            try
            {
                // This will throw if object is destroyed
                var _ = playerObject.activeInHierarchy;
            }
            catch
            {
                return;
            }

            int instanceId = playerObject.GetInstanceID();
            
            // Skip if already tracked
            if (_rings.ContainsKey(instanceId))
                return;

            // Skip local player
            if (Camera.main != null)
            {
                float distToCamera = Vector3.Distance(playerObject.transform.position, Camera.main.transform.position);
                if (distToCamera < 5f)
                {
                    AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Skipping local player's cavalry");
                    return;
                }
            }

            // Track the player GameObject directly (they're always on a horse)
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Tracking cavalry player: {playerObject.name} (ID: {instanceId})");
            CreateCylinder(playerObject.transform, instanceId);
        }
        
        // Removed OnPlayerDespawned - PlayerTracker handles cleanup
        // Removed CheckForHorse, CheckChildrenRecursive, and AddHorse methods
        // Cavalry classes always have horses, so we track the player GameObject directly

        public void OnGUI()
        {
            // No GUI needed for cavalry visualization
        }

        public void OnApplicationQuit()
        {
            CleanupAllRings();
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
            foreach (var kvp in _rings.Values)
            {
                try
                {
                    if (kvp.Value != null)
                    {
                        UnityEngine.Object.Destroy(kvp.Value);
                    }
                }
                catch { }
            }
            
            // Clean up any orphaned GameObjects
            CleanupOrphanedGameObjects();
            
            // Clear tracking data
            _rings.Clear();
        }
        
        /// <summary>
        /// Re-scan PlayerTracker for existing cavalry players and start tracking them
        /// Called after round cleanup to immediately start tracking for the new round
        /// </summary>
        private void RescanForExistingPlayers()
        {
            var allPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetAllPlayers();
            int addedCount = 0;
            
            foreach (var kvp in allPlayers)
            {
                var playerData = kvp.Value;
                string className = playerData.ClassName ?? "";
                
                // Check if player is a cavalry class
                if (className.IndexOf("Hussar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("Dragoon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (playerData.PlayerObject != null && playerData.PlayerTransform != null)
                    {
                        int instanceId = playerData.PlayerObject.GetInstanceID();
                        
                        // Skip if already tracked (shouldn't be, since we just cleared everything)
                        if (!_rings.ContainsKey(instanceId))
                        {
                            AddCavalryPlayer(playerData.PlayerObject);
                            addedCount++;
                        }
                    }
                }
            }
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] ✓ Re-scanned and started tracking {addedCount} existing cavalry player(s) for new round");
        }
        
        /// <summary>
        /// Called when a player spawns - check if they're cavalry and start tracking
        /// </summary>
        private void OnPlayerSpawned(int playerId, GameObject playerObject)
        {
            // Don't process if feature is disabled
            if (!_isEnabled)
                return;
                
            if (playerObject == null)
                return;
            
            // Get player data from PlayerTracker
            var playerData = AdvancedAdminUI.Utils.PlayerTracker.GetPlayer(playerId);
            if (playerData == null)
                return;
            
            string className = playerData.ClassName ?? "";
            
            // Check if player is a cavalry class
            if (className.IndexOf("Hussar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("Dragoon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int instanceId = playerObject.GetInstanceID();
                
                // Add if not already tracked
                if (!_rings.ContainsKey(instanceId))
                {
                    AddCavalryPlayer(playerObject);
                    AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Started tracking spawned cavalry player: Id={playerId}, Class={className}");
                }
            }
        }
        
        /// <summary>
        /// Called when a player leaves - remove their tracking and objects
        /// </summary>
        private void OnPlayerLeft(int playerId)
        {
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
        /// Remove a player's cavalry visualization by Holdfast playerId
        /// </summary>
        private void RemovePlayerByPlayerId(int holdfastPlayerId)
        {
            var playerData = AdvancedAdminUI.Utils.PlayerTracker.GetPlayer(holdfastPlayerId);
            if (playerData == null || playerData.PlayerObject == null)
                return;
            
            int instanceId = playerData.PlayerObject.GetInstanceID();
            if (_rings.ContainsKey(instanceId))
            {
                RemoveRing(instanceId);
            }
        }
        
        /// <summary>
        /// Called when time remaining updates - when it hits 0, clean up all visual objects but keep tracking
        /// </summary>
        private void OnUpdateTimeRemaining(float timeRemaining)
        {
            if (timeRemaining <= 0f)
            {
                AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Time remaining hit 0 - cleaning up all visual objects (keeping tracking)");
                CleanupAllVisualObjects();
            }
        }
        
        /// <summary>
        /// Clean up all visual objects (rings) but keep tracking data
        /// </summary>
        private void CleanupAllVisualObjects()
        {
            // Remove all rings but keep tracking data
            foreach (var kvp in _rings.Values)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            _rings.Clear();
        }

        private void CleanupInvalidCylinders()
        {
            // Just clean up invalid cylinders, no scanning
            List<int> toRemove = new List<int>();
            
            foreach (var kvp in _rings)
            {
                int targetId = kvp.Key;
                Transform target = kvp.Value.Key;
                
                if (target == null || target.gameObject == null || target.gameObject.GetInstanceID() != targetId)
                {
                    toRemove.Add(targetId);
                }
            }

            foreach (int targetId in toRemove)
            {
                RemoveRing(targetId);
            }
        }

        private bool IsLocalPlayerHorse(GameObject horseObj, int instanceId)
        {
            if (instanceId == _localPlayerHorseId && Time.time - _lastLocalPlayerCheck < LOCAL_PLAYER_CHECK_INTERVAL)
                return true;
            
            if (Camera.main != null)
            {
                float distanceToCamera = Vector3.Distance(horseObj.transform.position, Camera.main.transform.position);
                if (distanceToCamera < 5f)
                {
                    _localPlayerHorseId = instanceId;
                    _lastLocalPlayerCheck = Time.time;
                    return true;
                }
            }
            
            CharacterController charController = horseObj.GetComponent<CharacterController>();
            if (charController != null && Camera.main != null)
            {
                float distance = Vector3.Distance(horseObj.transform.position, Camera.main.transform.position);
                if (distance < 10f)
                {
                    _localPlayerHorseId = instanceId;
                    _lastLocalPlayerCheck = Time.time;
                    return true;
                }
            }
            
            Transform parent = horseObj.transform.parent;
            if (parent != null)
            {
                string parentName = parent.gameObject.name.ToLower();
                if (parentName.Contains("player") || parentName.Contains("local") || parentName.Contains("mine"))
                {
                    _localPlayerHorseId = instanceId;
                    _lastLocalPlayerCheck = Time.time;
                    return true;
                }
            }
            
            Rigidbody rb = horseObj.GetComponent<Rigidbody>();
            if (rb != null && Camera.main != null)
            {
                float distance = Vector3.Distance(horseObj.transform.position, Camera.main.transform.position);
                if (distance < 8f && !rb.isKinematic)
                {
                    _localPlayerHorseId = instanceId;
                    _lastLocalPlayerCheck = Time.time;
                    return true;
                }
            }
            
            return false;
        }

        // Removed FindTransformByInstanceId - no longer needed since we track via events

        private void CreateCylinder(Transform target, int instanceId)
        {
            // Don't create if disabled
            if (!_isEnabled)
                return;
                
            if (target == null || target.gameObject == null)
                return;

            if (_rings.ContainsKey(instanceId))
            {
                AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Cylinder already exists for InstanceID {instanceId}, skipping duplicate creation");
                return;
            }

            Bounds bounds = GetObjectBounds(target.gameObject);
            float topY = bounds.max.y;
            float bottomY = Mathf.Max(bounds.min.y, target.position.y + MIN_HEIGHT_OFFSET);

            GameObject cylinderObject = new GameObject($"AdminCylinder_{target.gameObject.name}_{target.GetInstanceID()}");
            cylinderObject.transform.position = target.position;
            cylinderObject.transform.rotation = Quaternion.identity;
            
            // Put on layer 5 (UI) so it can be excluded from minimap camera
            cylinderObject.layer = 5;

            Material mat = null;
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Diffuse");
            
            if (shader != null)
            {
                mat = new Material(shader);
                mat.color = Color.cyan;
                mat.SetFloat("_Mode", 0);
            }
            else
            {
                AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Could not find suitable shader for LineRenderer!");
                return;
            }

            CreateCircle(cylinderObject, "BottomCircle", bottomY, mat);
            CreateCircle(cylinderObject, "TopCircle", topY, mat);
            CreateVerticalLines(cylinderObject, bottomY, topY, mat);

            _rings[instanceId] = new KeyValuePair<Transform, GameObject>(target, cylinderObject);
        }

        private Bounds GetObjectBounds(GameObject obj)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                return renderer.bounds;

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
                return collider.bounds;

            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combinedBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combinedBounds.Encapsulate(renderers[i].bounds);
                return combinedBounds;
            }

            return new Bounds(obj.transform.position, new Vector3(2f, 2f, 2f));
        }

        private void CreateCircle(GameObject parent, string name, float yPosition, Material mat)
        {
            GameObject circleObj = new GameObject(name);
            circleObj.transform.SetParent(parent.transform);
            circleObj.transform.localPosition = Vector3.zero;
            circleObj.layer = 5; // UI layer - excluded from minimap

            LineRenderer lr = circleObj.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.positionCount = CIRCLE_SEGMENTS;
            lr.useWorldSpace = true;
            lr.material = mat;
            lr.startWidth = LINE_WIDTH;
            lr.endWidth = LINE_WIDTH;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = true;

            Vector3[] points = new Vector3[CIRCLE_SEGMENTS];
            Vector3 center = parent.transform.position;
            center.y = yPosition;

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)i / CIRCLE_SEGMENTS * 2f * Mathf.PI;
                points[i] = center + new Vector3(
                    Mathf.Cos(angle) * CYLINDER_RADIUS,
                    0f,
                    Mathf.Sin(angle) * CYLINDER_RADIUS
                );
            }

            lr.SetPositions(points);
        }

        private void CreateVerticalLines(GameObject parent, float bottomY, float topY, Material mat)
        {
            int verticalLineCount = 8;
            for (int i = 0; i < verticalLineCount; i++)
            {
                float angle = (float)i / verticalLineCount * 2f * Mathf.PI;
                float x = Mathf.Cos(angle) * CYLINDER_RADIUS;
                float z = Mathf.Sin(angle) * CYLINDER_RADIUS;

                GameObject lineObj = new GameObject($"VerticalLine_{i}");
                lineObj.transform.SetParent(parent.transform);
                lineObj.transform.localPosition = Vector3.zero;
                lineObj.layer = 5; // UI layer - excluded from minimap

                LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                lr.loop = false;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.material = mat;
                lr.startWidth = LINE_WIDTH;
                lr.endWidth = LINE_WIDTH;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = true;

                Vector3 center = parent.transform.position;
                Vector3[] points = new Vector3[2]
                {
                    center + new Vector3(x, bottomY - parent.transform.position.y, z),
                    center + new Vector3(x, topY - parent.transform.position.y, z)
                };

                lr.SetPositions(points);
            }
        }

        private void UpdateExistingCylinders()
        {
            List<int> toRemove = new List<int>();
            
            // Process cylinders in batches to avoid frame spikes
            int processed = 0;
            const int MAX_PER_UPDATE = 5; // Process max 5 cylinders per update cycle
            
            foreach (var kvp in _rings)
            {
                if (processed >= MAX_PER_UPDATE)
                    break; // Continue next update cycle
                    
                int instanceId = kvp.Key;
                Transform target = kvp.Value.Key;
                GameObject cylinder = kvp.Value.Value;
                
                if (target == null || target.gameObject == null || cylinder == null)
                {
                    toRemove.Add(instanceId);
                    processed++;
                    continue;
                }
                
                if (target.gameObject.GetInstanceID() != instanceId)
                {
                    toRemove.Add(instanceId);
                    processed++;
                    continue;
                }
                
                UpdateCylinder(cylinder, target);
                processed++;
            }
            
            // Remove invalid cylinders
            foreach (int instanceId in toRemove)
            {
                RemoveRing(instanceId);
            }
        }

        private void UpdateCylinder(GameObject cylinder, Transform target)
        {
            if (cylinder == null || target == null)
                return;

            cylinder.transform.position = target.position;

            Bounds bounds = GetObjectBounds(target.gameObject);
            float topY = bounds.max.y;
            float bottomY = Mathf.Max(bounds.min.y, target.position.y + MIN_HEIGHT_OFFSET);

            LineRenderer[] renderers = cylinder.GetComponentsInChildren<LineRenderer>();
            Vector3 center = target.position;

            foreach (LineRenderer lr in renderers)
            {
                if (lr.name.Contains("BottomCircle"))
                    UpdateCircle(lr, center, bottomY);
                else if (lr.name.Contains("TopCircle"))
                    UpdateCircle(lr, center, topY);
                else if (lr.name.Contains("VerticalLine"))
                    UpdateVerticalLine(lr, center, bottomY, topY);
            }
        }

        private void UpdateCircle(LineRenderer lr, Vector3 center, float yPosition)
        {
            center.y = yPosition;
            Vector3[] points = new Vector3[CIRCLE_SEGMENTS];

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)i / CIRCLE_SEGMENTS * 2f * Mathf.PI;
                points[i] = center + new Vector3(
                    Mathf.Cos(angle) * CYLINDER_RADIUS,
                    0f,
                    Mathf.Sin(angle) * CYLINDER_RADIUS
                );
            }

            lr.SetPositions(points);
        }

        private void UpdateVerticalLine(LineRenderer lr, Vector3 center, float bottomY, float topY)
        {
            string name = lr.gameObject.name;
            int lineIndex = 0;
            
            if (name.Contains("VerticalLine_"))
            {
                string indexStr = name.Substring(name.IndexOf("VerticalLine_") + "VerticalLine_".Length);
                if (int.TryParse(indexStr, out int parsedIndex))
                    lineIndex = parsedIndex;
            }

            int verticalLineCount = 8;
            float angle = (float)lineIndex / verticalLineCount * 2f * Mathf.PI;
            float x = Mathf.Cos(angle) * CYLINDER_RADIUS;
            float z = Mathf.Sin(angle) * CYLINDER_RADIUS;

            Vector3[] points = new Vector3[2]
            {
                center + new Vector3(x, bottomY - center.y, z),
                center + new Vector3(x, topY - center.y, z)
            };

            lr.SetPositions(points);
        }

        private void RemoveRing(int instanceId)
        {
            if (_rings.TryGetValue(instanceId, out var kvp))
            {
                GameObject ring = kvp.Value;
                if (ring != null)
                    UnityEngine.Object.Destroy(ring);
                _rings.Remove(instanceId);
            }
        }

        public void CleanupAllRings()
        {
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Cleaning up all ring GameObjects...");
            foreach (var kvp in _rings.Values)
            {
                try
                {
                    GameObject ring = kvp.Value;
                    if (ring != null)
                        UnityEngine.Object.Destroy(ring);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error destroying ring: {ex.Message}");
                }
            }
            _rings.Clear();
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] ✓ All ring GameObjects cleaned up");
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
                    
                    // Clean up orphaned cavalry rings (AdminCylinder_*)
                    if (objName.StartsWith("AdminCylinder_"))
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                        cleanedCount++;
                    }
                }
                
                if (cleanedCount > 0)
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


