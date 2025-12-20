using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using AdvancedAdminUI.Settings;
using HoldfastSharedMethods;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Identifies and visualizes "Rambo" players (lone wolves) who are separated from their team
    /// Optimized for 300+ players using spatial partitioning and event-based tracking
    /// </summary>
    public class RamboIndicatorFeature : IAdminFeature
    {
        public string FeatureName => "Rambo Indicator";
        
        private bool _isEnabled = false;
        private bool _showConnectionLines = true; // Separate toggle for connection lines
        // Group display is now handled by AdminWindow - no longer needed here
        
        // Settings are now loaded from ClassSettings.cs - keeping these for backward compatibility during migration
        // TODO: Remove these constants once all code is migrated to use ClassSettings
        private const float PROXIMITY_THRESHOLD_DEFAULT = 1.0f; // Default 1 meter spacing requirement
        private const float PROXIMITY_THRESHOLD_CAVALRY = 25.0f; // 25 meters for Hussar/Dragoon
        private const float PROXIMITY_THRESHOLD_SPECIAL = 5.0f; // 5 meters for LightInfantry, Rifleman, Officer, Sergeant, Surgeon
        private const float RAMBO_TIME_THRESHOLD = 1.0f; // 1 second before marking as rambo (reduced from 2.5s)
        private const int MIN_LINE_SIZE_DEFAULT = 3; // Default: need 3+ players in chain
        private const int MIN_LINE_SIZE_CAVALRY = 3; // Cavalry: need 2+ players in chain
        private const int MIN_LINE_SIZE_SPECIAL = 3; // Special classes: need 3+ players in chain
        private const float SCAN_INTERVAL = 1.0f; // Scan for NEW players every 1 second
        private const float UPDATE_INTERVAL = 0.1f; // Update existing players every 0.1 seconds (10 times per second)
        private const float COMPONENT_UPDATE_INTERVAL = 1.0f; // Update connected components every 1 second (reduced from 2s for faster updates)
        private const float VALIDATION_UPDATE_INTERVAL = 0.1f; // Update validation status every 0.1 seconds (very fast response to group changes)
        private float _lastComponentUpdateTime = 0f;
        private float _lastValidationUpdateTime = 0f;
        private float _lastConnectionUpdateTime = 0f;
        private float _lastCircleUpdateTime = 0f;
        private Dictionary<int, int> _cachedComponentSizes = new Dictionary<int, int>(); // Cache component sizes
        private Dictionary<int, ComponentComposition> _cachedComponentCompositions = new Dictionary<int, ComponentComposition>(); // Cache component class compositions
        private Dictionary<int, int> _cachedComponentIds = new Dictionary<int, int>(); // Cache component IDs (playerId -> componentId)
        private const float CIRCLE_RADIUS = 1.0f;
        private const float LINE_WIDTH = 0.1f;
        private const int CIRCLE_SEGMENTS = 32;
        
        private float _lastScanTime = 0f;
        private float _lastUpdateTime = 0f;
        
        // Track player data: InstanceID -> RamboData
        private class RamboData
        {
            public Transform Transform;
            public Vector3 LastPosition;
            public float TimeAlone = 0f;
            public GameObject CircleObject = null;
            public LineRenderer CircleRenderer = null;
            public bool IsRambo = false;
            public float LastProximityCheck = 0f;
            public string ClassName = ""; // Store player class for threshold lookup
            public string Faction = ""; // Store player faction - rules only apply to same faction
        }
        
        private Dictionary<int, RamboData> _playerData = new Dictionary<int, RamboData>();
        private Dictionary<int, GameObject> _playerIdToGameObject = new Dictionary<int, GameObject>(); // Track by Holdfast playerId
        private Material _redMaterial;
        private Material _greenMaterial; // Material for legal line ovals
        private Material _connectionMaterial; // Material for connection lines
        
        // Track line groups: componentId -> LineGroupData
        private class LineGroupData
        {
            public HashSet<int> PlayerIds = new HashSet<int>(); // All player IDs in this group
            public bool IsValid = true;
            public string ValidationReason = ""; // Why it's invalid (if invalid)
            public GameObject OvalObject = null;
            public LineRenderer OvalRenderer = null;
            public Vector3 CenterPosition = Vector3.zero;
            public Vector3 BoundsSize = Vector3.zero;
            public float ValidationStatusChangeTime = 0f; // Time when current validation status was set
        }
        
        // Public class for exposing group data to UI
        public class GroupDisplayData
        {
            public int ComponentId { get; set; }
            public HashSet<int> PlayerIds { get; set; }
            public bool IsValid { get; set; }
            public string ValidationReason { get; set; }
            public string Faction { get; set; }
            public ComponentComposition Composition { get; set; }
            public int ComponentSize { get; set; }
            public float Duration { get; set; }
        }
        
        private Dictionary<int, LineGroupData> _lineGroups = new Dictionary<int, LineGroupData>(); // componentId -> LineGroupData
        private Dictionary<int, int> _playerToComponentId = new Dictionary<int, int>(); // playerId -> componentId
        
        // Track connections between players: (playerId1, playerId2) -> LineRenderer GameObject
        // Use ordered pair (smaller ID first) to avoid duplicates
        private Dictionary<string, GameObject> _connections = new Dictionary<string, GameObject>();
        
        // Track alive cavalry per faction: faction -> (hussarCount, dragoonCount)
        private Dictionary<string, (int hussarCount, int dragoonCount)> _cavalryCountsByFaction = new Dictionary<string, (int, int)>();
        // Track cavalry players: faction -> List<playerId>
        private Dictionary<string, List<int>> _hussarPlayersByFaction = new Dictionary<string, List<int>>();
        private Dictionary<string, List<int>> _dragoonPlayersByFaction = new Dictionary<string, List<int>>();
        private float _lastCavalryCountUpdateTime = 0f;
        private const float CAVALRY_COUNT_UPDATE_INTERVAL = 1.0f; // Update cavalry counts every second
        
        // Track alive skirmishers per faction: faction -> (riflemanCount, lightInfantryCount)
        private Dictionary<string, (int riflemanCount, int lightInfantryCount)> _skirmisherCountsByFaction = new Dictionary<string, (int, int)>();
        // Track skirmisher players: faction -> List<playerId>
        private Dictionary<string, List<int>> _riflemanPlayersByFaction = new Dictionary<string, List<int>>();
        private Dictionary<string, List<int>> _lightInfantryPlayersByFaction = new Dictionary<string, List<int>>();
        private float _lastSkirmisherCountUpdateTime = 0f;
        private const float SKIRMISHER_COUNT_UPDATE_INTERVAL = 1.0f; // Update skirmisher counts every second
        
        // Surgeon proximity detection: playerId -> (isNearLine, isNearSkirmishers, nearestGroupType)
        private Dictionary<int, (bool isNearLine, bool isNearSkirmishers, string nearestGroupType)> _surgeonProximity = new Dictionary<int, (bool, bool, string)>();
        private const float SURGEON_PROXIMITY_THRESHOLD = 5.0f; // 5 meters
        
        // Spatial grid for efficient proximity checks (reduces O(n²) to ~O(n))
        private Dictionary<int, List<int>> _spatialGrid = new Dictionary<int, List<int>>();
        private const float GRID_CELL_SIZE = 5.0f; // 5m grid cells
        
        public bool IsEnabled => _isEnabled;
        public bool ShowConnectionLines => _showConnectionLines;
        
        public void SetShowConnectionLines(bool show)
        {
            _showConnectionLines = show;
            if (!show)
            {
                // Clean up all connection lines when disabled
                CleanupAllConnections();
            }
        }
        
        // Group display is now handled by AdminWindow - ShowGroupDisplay removed
        
        /// <summary>
        /// Gets all group data for display in external UI
        /// </summary>
        public List<GroupDisplayData> GetGroupDisplayData()
        {
            List<GroupDisplayData> result = new List<GroupDisplayData>();
            
            foreach (var kvp in _lineGroups)
            {
                int componentId = kvp.Key;
                LineGroupData groupData = kvp.Value;
                
                if (groupData.PlayerIds.Count < 2)
                    continue;
                
                // Get faction, composition, and size from first player
                string faction = "Unknown";
                ComponentComposition composition = null;
                int componentSize = 0;
                
                foreach (int playerId in groupData.PlayerIds)
                {
                    // Get faction from PlayerTracker
                    var playerData = AdvancedAdminUI.Utils.PlayerTracker.GetPlayer(playerId);
                    if (playerData != null && !string.IsNullOrEmpty(playerData.FactionName))
                    {
                        faction = playerData.FactionName;
                    }
                    
                    if (_cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp))
                        composition = comp;
                    if (_cachedComponentSizes.TryGetValue(playerId, out int size))
                        componentSize = size;
                    
                    // We got what we need from first player
                    if (!string.IsNullOrEmpty(faction) && composition != null && componentSize > 0)
                        break;
                }
                
                float duration = Time.time - groupData.ValidationStatusChangeTime;
                
                result.Add(new GroupDisplayData
                {
                    ComponentId = componentId,
                    PlayerIds = new HashSet<int>(groupData.PlayerIds),
                    IsValid = groupData.IsValid,
                    ValidationReason = groupData.ValidationReason ?? "",
                    Faction = faction,
                    Composition = composition,
                    ComponentSize = componentSize,
                    Duration = duration
                });
            }
            
            return result;
        }

        public void Enable()
        {
            _isEnabled = true;
            CreateMaterial();
            
            // Subscribe to OnPlayerSpawned via PlayerEventManager (receives events from IHoldfastSharedMethods)
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerSpawnedCallbacks.Add(OnPlayerSpawnedSimple);
            
            // Subscribe to OnPlayerKilledPlayer to remove dead players
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerKilledPlayerCallbacks.Add(OnPlayerKilledPlayer);
            
            // Subscribe to OnPlayerLeft to remove players who left
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerLeftCallbacks.Add(OnPlayerLeft);
            
            // Subscribe to OnRoundDetails to reset on new round (keep player IDs)
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundDetailsCallbacks.Add(OnRoundDetails);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndFactionWinnerCallbacks.Add(OnRoundEndFactionWinner); // Wrapper that calls OnRoundDetails
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndPlayerWinnerCallbacks.Add(OnRoundEndPlayerWinner); // Wrapper that calls OnRoundDetails
            
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
                
                // Try to get CharacterController, but don't require it immediately
                // It might be added later, so we'll check again in OnUpdate
                CharacterController cc = playerData.PlayerObject.GetComponent<CharacterController>();
                
                // Skip if no CharacterController and GameObject name doesn't suggest it's a player
                if (cc == null)
                {
                    string objName = playerData.PlayerObject.name.ToLower();
                    if (!objName.Contains("player") && !objName.Contains("proxy"))
                        continue; // Probably not a player GameObject
                }
                
                int instanceId = playerData.PlayerObject.GetInstanceID();
                
                // Skip Dragoon and Hussar - they are excluded from rambo detection
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
                    _playerData[instanceId] = new RamboData
                    {
                        Transform = playerData.PlayerTransform,
                        LastPosition = playerData.PlayerTransform.position,
                        TimeAlone = 0f,
                        IsRambo = false,
                        ClassName = playerData.ClassName ?? "",
                        Faction = playerData.FactionName ?? ""
                    };
                    
                    _playerIdToGameObject[playerData.PlayerId] = playerData.PlayerObject;
                    addedCount++;
                }
                else
                {
                    // Update class name and faction if they changed
                    _playerData[instanceId].ClassName = playerData.ClassName ?? "";
                    _playerData[instanceId].Faction = playerData.FactionName ?? "";
                }
            }
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Enabled - Found {addedCount} existing player(s)");
        }

        public void Disable()
        {
            _isEnabled = false;
            
            // Unsubscribe from all events
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerSpawnedCallbacks.Remove(OnPlayerSpawnedSimple);
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerKilledPlayerCallbacks.Remove(OnPlayerKilledPlayer);
            AdvancedAdminUI.Utils.PlayerEventManager._onPlayerLeftCallbacks.Remove(OnPlayerLeft);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundDetailsCallbacks.Remove(OnRoundDetails);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndFactionWinnerCallbacks.Remove(OnRoundEndFactionWinner);
            AdvancedAdminUI.Utils.PlayerEventManager._onRoundEndPlayerWinnerCallbacks.Remove(OnRoundEndPlayerWinner);
            AdvancedAdminUI.Utils.PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            CleanupAllIndicators();
            DestroyMaterials();
            
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
                    
                    // Skip Dragoon and Hussar - they are excluded from rambo detection
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
                        // Cache CharacterController check result
                        CharacterController cc = playerData.PlayerObject.GetComponent<CharacterController>();
                        if (cc == null)
                        {
                            // Only check name if no CharacterController
                            string objName = playerData.PlayerObject.name;
                            if (objName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) < 0 && 
                                objName.IndexOf("Proxy", StringComparison.OrdinalIgnoreCase) < 0)
                                continue; // Probably not a player GameObject
                        }
                        
                        _playerData[instanceId] = new RamboData
                        {
                            Transform = playerData.PlayerTransform,
                            LastPosition = playerData.PlayerTransform.position,
                            TimeAlone = 0f,
                            IsRambo = false,
                            ClassName = playerData.ClassName ?? "",
                            Faction = playerData.FactionName ?? ""
                        };
                        
                        _playerIdToGameObject[playerData.PlayerId] = playerData.PlayerObject;
                    }
                    else
                    {
                        // Update class name and faction if they changed
                        _playerData[instanceId].ClassName = playerData.ClassName ?? "";
                        _playerData[instanceId].Faction = playerData.FactionName ?? "";
                    }
                }
            }

            // Update circle positions less frequently for performance (every 0.1 seconds instead of every frame)
            if (Time.time - _lastCircleUpdateTime >= 0.1f)
            {
                _lastCircleUpdateTime = Time.time;
                foreach (var kvp in _playerData)
                {
                    RamboData data = kvp.Value;
                    if (data.IsRambo && data.CircleObject != null && data.CircleRenderer != null && data.Transform != null)
                    {
                        Vector3 playerPos = data.Transform.position;
                        Vector3 circlePos = new Vector3(playerPos.x, playerPos.y + 0.2f, playerPos.z);
                        data.CircleObject.transform.position = circlePos;
                        
                        // Update LineRenderer positions (they're in world space, so we need to update them)
                        Vector3[] points = new Vector3[CIRCLE_SEGMENTS];
                        for (int i = 0; i < CIRCLE_SEGMENTS; i++)
                        {
                            float angle = (float)i / CIRCLE_SEGMENTS * 2f * Mathf.PI;
                            points[i] = circlePos + new Vector3(
                                Mathf.Cos(angle) * CIRCLE_RADIUS,
                                0f,
                                Mathf.Sin(angle) * CIRCLE_RADIUS
                            );
                        }
                        data.CircleRenderer.SetPositions(points);
                    }
                }
            }
            
            // Update connection line positions less frequently (every 0.3 seconds) - only if enabled
            if (_showConnectionLines && Time.time - _lastConnectionUpdateTime >= 0.3f)
            {
                UpdateConnectionLines();
            }

            // Update existing players frequently (but efficiently)
            if (Time.time - _lastUpdateTime >= UPDATE_INTERVAL)
            {
                _lastUpdateTime = Time.time;
                UpdateExistingPlayers();
            }

            // Cleanup invalid players periodically
            if (Time.time - _lastScanTime >= SCAN_INTERVAL)
            {
                _lastScanTime = Time.time;
                VerifyAndCleanupPlayers();
            }
        }

        public void OnGUI()
        {
            // Group display is now handled by AdminWindow - no longer drawn here
            
            if (!_isEnabled || Camera.main == null)
                return;

            // Only draw timers for rambos on screen (limit GUI calls)
            int drawnCount = 0;
            const int MAX_GUI_DRAWS = 50; // Increased limit for line groups
            
            // Draw line group validation text (only for dragoon/hussar groups)
            foreach (var kvp in _lineGroups)
            {
                if (drawnCount >= MAX_GUI_DRAWS)
                    break;
                
                LineGroupData groupData = kvp.Value;
                
                if (groupData.PlayerIds.Count < 2)
                    continue;
                
                // Only show validation text for dragoon/hussar groups
                ComponentComposition composition = null;
                foreach (int playerId in groupData.PlayerIds)
                {
                    if (_cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp))
                    {
                        composition = comp;
                        break;
                    }
                }
                
                if (!GroupContainsCavalry(groupData.PlayerIds, composition))
                    continue; // Skip non-cavalry groups
                
                Vector3 worldPos = groupData.CenterPosition + Vector3.up * 3f; // Above the group
                Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0 && screenPos.x > 0 && screenPos.x < Screen.width && screenPos.y > 0 && screenPos.y < Screen.height)
                {
                    // Calculate duration since validation status changed
                    float duration = Time.time - groupData.ValidationStatusChangeTime;
                    string durationText = duration >= 0.1f ? $" - {duration:F1}s" : "";
                    
                    string displayText = groupData.IsValid 
                        ? $"VALID{durationText}" 
                        : $"INVALID: {groupData.ValidationReason}{durationText}";
                    Color textColor = groupData.IsValid ? Color.green : Color.red;

                    GUI.color = textColor;
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontSize = 16;
                    style.fontStyle = FontStyle.Bold;
                    style.normal.textColor = textColor;
                    style.alignment = TextAnchor.MiddleCenter;
                    style.wordWrap = true;

                    // Calculate text size
                    GUIContent content = new GUIContent(displayText);
                    Vector2 textSize = style.CalcSize(content);
                    float width = Mathf.Max(200f, textSize.x + 20f);
                    float height = textSize.y + 10f;

                    Rect labelRect = new Rect(screenPos.x - width / 2f, Screen.height - screenPos.y - height, width, height);
                    
                    // Draw background with proper color based on validity (helps prevent overlap)
                    Color bgColor = groupData.IsValid ? new Color(0f, 0.3f, 0f, 0.9f) : new Color(0.3f, 0f, 0f, 0.9f);
                    GUI.color = bgColor;
                    GUI.Box(labelRect, "");
                    
                    // Draw text immediately (OnGUI runs every frame, so this updates instantly)
                    GUI.color = textColor;
                    GUI.Label(labelRect, displayText, style);
                    GUI.color = Color.white;
                    
                    drawnCount++;
                }
            }
            
            // Draw individual rambo timers
            foreach (var kvp in _playerData)
            {
                if (drawnCount >= MAX_GUI_DRAWS)
                    break;
                    
                RamboData data = kvp.Value;
                
                float ramboThreshold = ClassSettings.GetRamboTimeThreshold(data.ClassName);
                if (!data.IsRambo || data.TimeAlone < ramboThreshold || data.Transform == null)
                    continue;

                Vector3 worldPos = data.Transform.position + Vector3.up * 2f;
                Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0 && screenPos.x > 0 && screenPos.x < Screen.width && screenPos.y > 0 && screenPos.y < Screen.height)
                {
                    float ramboTime = data.TimeAlone - ramboThreshold;
                    int seconds = Mathf.FloorToInt(ramboTime);
                    string timeText = $"RAMBO: {seconds}s";

                    GUI.color = Color.red;
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontSize = 14; // Smaller font for performance
                    style.fontStyle = FontStyle.Bold;
                    style.normal.textColor = Color.red;
                    style.alignment = TextAnchor.MiddleCenter;

                    Rect labelRect = new Rect(screenPos.x - 60, Screen.height - screenPos.y - 25, 120, 20);
                    GUI.Box(labelRect, "");
                    GUI.Label(labelRect, timeText, style);
                    GUI.color = Color.white;
                    
                    drawnCount++;
                }
            }
        }

        public void OnApplicationQuit()
        {
            CleanupAllIndicators();
            DestroyMaterials();
        }
        
        private void DestroyMaterials()
        {
            if (_redMaterial != null)
            {
                UnityEngine.Object.Destroy(_redMaterial);
                _redMaterial = null;
            }
            if (_greenMaterial != null)
            {
                UnityEngine.Object.Destroy(_greenMaterial);
                _greenMaterial = null;
            }
            if (_connectionMaterial != null)
            {
                UnityEngine.Object.Destroy(_connectionMaterial);
                _connectionMaterial = null;
            }
        }
        
        /// <summary>
        /// Called when round ends with faction winner - DON'T cleanup here to avoid lag spike
        /// Cleanup will happen on the actual OnRoundDetails when the new round starts
        /// </summary>
        private void OnRoundEndFactionWinner(HoldfastSharedMethods.FactionCountry factionCountry, HoldfastSharedMethods.FactionRoundWinnerReason reason)
        {
            // Do nothing - cleanup will happen when OnRoundDetails is called for the NEW round
            // This prevents massive lag from destroying 200+ GameObjects at once
        }
        
        /// <summary>
        /// Called when round ends with player winner - DON'T cleanup here to avoid lag spike
        /// Cleanup will happen on the actual OnRoundDetails when the new round starts
        /// </summary>
        private void OnRoundEndPlayerWinner(int playerId)
        {
            // Do nothing - cleanup will happen when OnRoundDetails is called for the NEW round
            // This prevents massive lag from destroying 200+ GameObjects at once
        }
        
        /// <summary>
        /// Called when a new round starts - cleanup everything except tracked player IDs
        /// Player IDs remain in PlayerTracker, but all feature-specific tracking is cleared
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
            
            // Clean up all connection lines
            CleanupAllConnections();
            
            // Clean up all line group ovals
            CleanupAllLineGroups();
            
            // Also clean up any orphaned GameObjects that might exist (from players who left)
            // This ensures we catch any GameObjects that weren't properly cleaned up in OnPlayerLeft
            CleanupOrphanedGameObjects();
            
            // NOW clear all tracking data (after destroying objects)
            _cachedComponentSizes.Clear();
            _cachedComponentCompositions.Clear();
            _cachedComponentIds.Clear();
            _playerIdToGameObject.Clear();
            _playerData.Clear();
            _spatialGrid.Clear();
            _playerToComponentId.Clear();
            
            // Clear cavalry tracking
            _cavalryCountsByFaction.Clear();
            _hussarPlayersByFaction.Clear();
            _dragoonPlayersByFaction.Clear();
            
            // Clear skirmisher tracking
            _skirmisherCountsByFaction.Clear();
            _riflemanPlayersByFaction.Clear();
            _lightInfantryPlayersByFaction.Clear();
            _surgeonProximity.Clear();
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] ✓ Round cleanup complete - all visual objects and tracking cleared (player IDs preserved in PlayerTracker)");
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Waiting for OnPlayerSpawned events to start tracking players for new round");
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
                
                // Try to get CharacterController, but don't require it immediately
                CharacterController cc = playerData.PlayerObject.GetComponent<CharacterController>();
                
                // Skip if no CharacterController and GameObject name doesn't suggest it's a player
                if (cc == null)
                {
                    string objName = playerData.PlayerObject.name.ToLower();
                    if (!objName.Contains("player") && !objName.Contains("proxy"))
                        continue; // Probably not a player GameObject
                }
                
                int instanceId = playerData.PlayerObject.GetInstanceID();
                
                // Skip Dragoon and Hussar - they are excluded from rambo detection
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
                    _playerData[instanceId] = new RamboData
                    {
                        Transform = playerData.PlayerTransform,
                        LastPosition = playerData.PlayerTransform.position,
                        TimeAlone = 0f,
                        IsRambo = false,
                        ClassName = playerData.ClassName ?? "",
                        Faction = playerData.FactionName ?? ""
                    };
                    
                    _playerIdToGameObject[playerData.PlayerId] = playerData.PlayerObject;
                    addedCount++;
                }
            }
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] ✓ Re-scanned and started tracking {addedCount} existing player(s) for new round");
        }
        
        /// <summary>
        /// Called when a player spawns - start tracking them for rambo detection, valid/invalid areas, etc.
        /// This is where we start checking for rulebreaks, valid/invalid, class, etc.
        /// </summary>
        private void OnPlayerSpawnedSimple(int playerId, GameObject playerObject)
        {
            if (playerObject == null)
            {
                return;
            }
            
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
            
            // Skip Dragoon and Hussar - they are excluded from rambo detection
            string className = playerData.ClassName ?? "";
            if (className.IndexOf("dragoon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("hussar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }
            
            // Add or update player tracking
            if (!_playerData.ContainsKey(instanceId))
            {
                // Start tracking this player - this is where rambo detection, valid/invalid checks begin
                _playerData[instanceId] = new RamboData
                {
                    Transform = playerObject.transform,
                    LastPosition = playerObject.transform.position,
                    TimeAlone = 0f,
                    IsRambo = false,
                    ClassName = playerData.ClassName ?? "",
                    Faction = playerData.FactionName ?? ""
                };
                
                _playerIdToGameObject[playerId] = playerObject;
                
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
                data.Faction = playerData.FactionName ?? "";
                data.ClassName = playerData.ClassName ?? "";
                data.TimeAlone = 0f; // Reset rambo timer on respawn
                if (data.IsRambo)
                {
                    data.IsRambo = false;
                    RemoveRamboIndicator(instanceId, data);
                }
                
                _playerIdToGameObject[playerId] = playerObject;
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
        /// Clean up all visual objects (circles, connections, line groups) but keep tracking data
        /// Public so it can be called from global cleanup
        /// </summary>
        public void CleanupAllVisualObjects()
        {
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] Cleaning up all visual GameObjects (circles, lines, ovals)...");
            
            // Remove all rambo indicator circles
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
                        kvp.Value.IsRambo = false;
                        kvp.Value.TimeAlone = 0f;
                    }
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error cleaning up indicator for player {kvp.Key}: {ex.Message}");
                }
            }
            
            // Clean up all connection lines
            CleanupAllConnections();
            
            // Clean up all line group ovals
            CleanupAllLineGroups();
            
            AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] ✓ All visual GameObjects cleaned up");
        }

        // Removed OnPlayerSpawned and OnPlayerDespawned - PlayerTracker handles all tracking

        private void VerifyAndCleanupPlayers()
        {
            // Verify existing players still exist and clean up invalid ones
            // This is a fallback/cleanup pass since we're using event-driven tracking
            List<int> toRemove = new List<int>();
            
            foreach (var kvp in _playerData)
            {
                if (kvp.Value.Transform == null || kvp.Value.Transform.gameObject == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int id in toRemove)
            {
                RemoveRamboIndicator(id, _playerData[id]);
                _playerData.Remove(id);
            }
            
            // Also check for any new players that might have been missed by events
            // (fallback for cases where Harmony patch didn't work)
            if (_playerData.Count == 0)
            {
                // If we have no players tracked, do a one-time scan
                CharacterController[] controllers = UnityEngine.Object.FindObjectsOfType<CharacterController>();
                foreach (CharacterController controller in controllers)
                {
                    if (controller == null || controller.gameObject == null)
                        continue;

                    Transform t = controller.transform;
                    if (t.parent != null && t.parent != t.root)
                        continue;

                    int instanceId = controller.gameObject.GetInstanceID();
                    if (_playerData.ContainsKey(instanceId))
                        continue;

                    if (Camera.main != null)
                    {
                        float distToCamera = Vector3.Distance(t.position, Camera.main.transform.position);
                        if (distToCamera < 3f)
                            continue;
                    }

                    _playerData[instanceId] = new RamboData
                    {
                        Transform = t,
                        LastPosition = t.position,
                        TimeAlone = 0f,
                        IsRambo = false
                    };
                }
            }
        }

        private void UpdateExistingPlayers()
        {
            if (_playerData.Count == 0)
                return;

            // Calculate actual delta time since last update (UPDATE_INTERVAL), not frame delta
            float deltaTime = UPDATE_INTERVAL;
            
            // CLEANUP: Remove players with destroyed/null transforms
            List<int> toRemove = null;
            foreach (var kvp in _playerData)
            {
                bool shouldRemove = false;
                
                if (kvp.Value.Transform == null)
                {
                    shouldRemove = true;
                }
                else
                {
                    // Check if Unity object was destroyed
                    try
                    {
                        // Accessing a property on a destroyed object throws
                        var _ = kvp.Value.Transform.position;
                    }
                    catch
                    {
                        shouldRemove = true;
                    }
                }
                
                if (shouldRemove)
                {
                    if (toRemove == null) toRemove = new List<int>();
                    toRemove.Add(kvp.Key);
                }
            }
            
            // Remove invalid entries and clean up their visual objects
            if (toRemove != null)
            {
                foreach (int instanceId in toRemove)
                {
                    if (_playerData.TryGetValue(instanceId, out RamboData data))
                    {
                        RemoveRamboIndicator(instanceId, data);
                        _playerData.Remove(instanceId);
                        _playerToComponentId.Remove(instanceId);
                        _cachedComponentSizes.Remove(instanceId);
                        _cachedComponentCompositions.Remove(instanceId);
                    }
                }
            }
            
            // Build spatial grid for efficient proximity checks
            _spatialGrid.Clear();
            foreach (var kvp in _playerData)
            {
                if (kvp.Value.Transform == null)
                    continue;

                Vector3 pos = kvp.Value.Transform.position;
                int gridKey = GetGridKey(pos);
                
                if (!_spatialGrid.ContainsKey(gridKey))
                    _spatialGrid[gridKey] = new List<int>();
                
                _spatialGrid[gridKey].Add(kvp.Key);
            }

            // Update connected components less frequently (expensive operation)
            if (Time.time - _lastComponentUpdateTime >= COMPONENT_UPDATE_INTERVAL)
            {
                _lastComponentUpdateTime = Time.time;
                _cachedComponentSizes = FindConnectedComponentsOptimized();
                _cachedComponentCompositions = FindConnectedComponentCompositions();
                UpdateLineGroups(); // Update line group visualization (creates/removes groups)
            }
            
            // Update validation status more frequently (fast operation - just re-validates existing groups)
            if (Time.time - _lastValidationUpdateTime >= VALIDATION_UPDATE_INTERVAL)
            {
                _lastValidationUpdateTime = Time.time;
                UpdateLineGroupValidation(); // Fast validation update without recalculating components
            }
            
            // Update line group oval positions
            if (Time.time - _lastCircleUpdateTime >= 0.2f)
            {
                _lastCircleUpdateTime = Time.time;
                UpdateLineGroupPositions();
            }
            
            // Update connection visualization less frequently (every 1 second) for performance - only if enabled
            if (_showConnectionLines && Time.time - _lastConnectionUpdateTime >= 1.0f)
            {
                _lastConnectionUpdateTime = Time.time;
                
                // Clean up connections for non-cavalry players first
                List<string> connectionsToRemove = new List<string>();
                foreach (var kvp in _connections)
                {
                    string connectionKey = kvp.Key;
                    string[] parts = connectionKey.Split('_');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int id1) && int.TryParse(parts[1], out int id2))
                    {
                        bool player1IsCavalry = false;
                        bool player2IsCavalry = false;
                        
                        if (_playerData.TryGetValue(id1, out RamboData data1))
                        {
                            string className1 = (data1.ClassName ?? "").ToLower();
                            player1IsCavalry = className1.Contains("dragoon") || className1.Contains("hussar");
                        }
                        
                        if (_playerData.TryGetValue(id2, out RamboData data2))
                        {
                            string className2 = (data2.ClassName ?? "").ToLower();
                            player2IsCavalry = className2.Contains("dragoon") || className2.Contains("hussar");
                        }
                        
                        // Remove connection if either player is not cavalry
                        if (!player1IsCavalry || !player2IsCavalry)
                        {
                            connectionsToRemove.Add(connectionKey);
                        }
                    }
                }
                
                foreach (string key in connectionsToRemove)
                {
                    if (_connections.TryGetValue(key, out GameObject connectionObj))
                    {
                        if (connectionObj != null)
                            UnityEngine.Object.Destroy(connectionObj);
                        _connections.Remove(key);
                    }
                }
                
                // Track nearby players for connection visualization (using spatial grid for efficiency)
                // Only process cavalry players
                Dictionary<int, HashSet<int>> nearbyPlayersMap = new Dictionary<int, HashSet<int>>();
                
                foreach (var kvp in _playerData)
                {
                    int playerId = kvp.Key;
                    RamboData data = kvp.Value;

                    if (data.Transform == null || data.Transform.gameObject == null)
                        continue;
                    
                    // Only create connection lines for dragoon/hussar players
                    string className = (data.ClassName ?? "").ToLower();
                    bool isCavalry = className.Contains("dragoon") || className.Contains("hussar");
                    if (!isCavalry)
                        continue; // Skip non-cavalry players

                    Vector3 playerPos = data.Transform.position;
                    
                    // Get proximity threshold for this player's class
                    float threshold = GetProximityThresholdForClass(data.ClassName);
                    
                    // Calculate how many grid cells to check based on threshold
                    int cellRadius = Mathf.CeilToInt(threshold / GRID_CELL_SIZE) + 1;
                    
                    // Track all nearby players for connection visualization
                    HashSet<int> nearbyPlayerIds = new HashSet<int>();
                    
                    for (int dx = -cellRadius; dx <= cellRadius; dx++)
                    {
                        for (int dz = -cellRadius; dz <= cellRadius; dz++)
                        {
                            Vector3 checkPos = playerPos + new Vector3(dx * GRID_CELL_SIZE, 0, dz * GRID_CELL_SIZE);
                            int gridKey = GetGridKey(checkPos);
                            
                            if (_spatialGrid.TryGetValue(gridKey, out List<int> nearbyPlayers))
                            {
                                foreach (int otherPlayerId in nearbyPlayers)
                                {
                                    if (otherPlayerId == playerId)
                                        continue;

                                    if (!_playerData.TryGetValue(otherPlayerId, out RamboData otherData))
                                        continue;

                                    if (otherData.Transform == null)
                                        continue;
                                    
                                    // Only connect cavalry players
                                    string otherClassName = (otherData.ClassName ?? "").ToLower();
                                    bool otherIsCavalry = otherClassName.Contains("dragoon") || otherClassName.Contains("hussar");
                                    if (!otherIsCavalry)
                                        continue;
                                    
                                    // Only connect players of the same faction
                                    if (string.IsNullOrEmpty(data.Faction) || string.IsNullOrEmpty(otherData.Faction) ||
                                        !data.Faction.Equals(otherData.Faction, StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    // Two-tier proximity check:
                                    // 1. Universal rule: Any class within 1 meter is connected
                                    // 2. Class-specific rule: If not within 1m, use class-specific thresholds
                                    //    (with special cases like Carpenter + Artillery = 10m)
                                    float classThreshold = GetEffectiveThreshold(data.ClassName, otherData.ClassName);

                                    float distance = Vector3.Distance(playerPos, otherData.Transform.position);
                                    
                                    // Connect if within 1 meter (universal) OR within class-specific threshold
                                    if (distance <= 1.0f || distance <= classThreshold)
                                    {
                                        nearbyPlayerIds.Add(otherPlayerId);
                                    }
                                }
                            }
                        }
                    }
                    
                    // Always add player to map, even if they have no nearby players
                    // This ensures all players are included in the graph for MST calculation
                    nearbyPlayersMap[playerId] = nearbyPlayerIds;
                }
                
                // Build MST (Minimum Spanning Tree) for each connected component
                // This ensures connections follow shortest paths without redundant connections
                RebuildConnectionMST(nearbyPlayersMap);
            }
            
            // Update cavalry counts periodically
            if (Time.time - _lastCavalryCountUpdateTime >= CAVALRY_COUNT_UPDATE_INTERVAL)
            {
                UpdateCavalryCounts();
                _lastCavalryCountUpdateTime = Time.time;
            }
            
            // Update skirmisher counts periodically
            if (Time.time - _lastSkirmisherCountUpdateTime >= SKIRMISHER_COUNT_UPDATE_INTERVAL)
            {
                UpdateSkirmisherCounts();
                UpdateSurgeonProximity();
                _lastSkirmisherCountUpdateTime = Time.time;
            }
            
            // Update rambo status based on in-group check and cached connected component sizes
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                RamboData data = kvp.Value;

                if (data.Transform == null || data.Transform.gameObject == null)
                    continue;
                
                // First check: Is player "in-group"? (within InGroupRadius of InGroupMinPlayers+ other players)
                // This prevents false rambo calls during movement when players are catching up
                bool isInGroup = IsInGroup(playerId, data);
                
                if (isInGroup)
                {
                    // Player is in-group - reset rambo timer
                    if (data.TimeAlone > 0)
                    {
                        data.TimeAlone = 0f;
                        if (data.IsRambo)
                        {
                            data.IsRambo = false;
                            RemoveRamboIndicator(playerId, data);
                        }
                    }
                    continue; // Skip further checks, player is fine
                }
                
                // Not in-group - check if they're in a valid line formation
                // Get the size and composition of this player's connected component from cache
                int componentSize = _cachedComponentSizes.TryGetValue(playerId, out int size) ? size : 1;
                ComponentComposition composition = _cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp) ? comp : null;
                
                // Check if this player is in a valid line based on class-specific rules
                bool isInValidLine = IsValidLineForPlayer(data.ClassName, componentSize, composition);
                
                if (!isInValidLine)
                {
                    // Not in a valid line and not in-group - increment rambo timer
                    data.TimeAlone += deltaTime;
                    
                    float ramboThreshold = ClassSettings.GetRamboTimeThreshold(data.ClassName);
                    if (data.TimeAlone >= ramboThreshold && !data.IsRambo)
                    {
                        data.IsRambo = true;
                        CreateRamboIndicator(data.Transform, playerId, data);
                    }
                }
                else
                {
                    // In a valid line - reset rambo timer
                    if (data.TimeAlone > 0)
                    {
                        data.TimeAlone = 0f;
                        if (data.IsRambo)
                        {
                            data.IsRambo = false;
                            RemoveRamboIndicator(playerId, data);
                        }
                    }
                }
            }
        }

        private float GetProximityThresholdForClass(string className)
        {
            return ClassSettings.GetProximityThreshold(className);
        }
        
        /// <summary>
        /// Get the effective proximity threshold between two classes, accounting for special cases
        /// (e.g., Carpenter with artillery classes uses 10m instead of 5m)
        /// </summary>
        private float GetEffectiveThreshold(string className1, string className2)
        {
            if (string.IsNullOrEmpty(className1) || string.IsNullOrEmpty(className2))
                return 1.0f;
            
            string class1Lower = className1.ToLower();
            string class2Lower = className2.ToLower();
            
            // Special case: Carpenter with artillery classes uses 10m
            bool isCarpenter1 = class1Lower.Contains("carpenter");
            bool isCarpenter2 = class2Lower.Contains("carpenter");
            bool isArtillery1 = class1Lower.Contains("cannoneer") || class1Lower.Contains("rocketeer") || class1Lower.Contains("sapper");
            bool isArtillery2 = class2Lower.Contains("cannoneer") || class2Lower.Contains("rocketeer") || class2Lower.Contains("sapper");
            
            if ((isCarpenter1 && isArtillery2) || (isCarpenter2 && isArtillery1))
            {
                return 10.0f; // Carpenter + Artillery = 10m
            }
            
            // Default: use the smaller threshold of the two classes
            float threshold1 = GetProximityThresholdForClass(className1);
            float threshold2 = GetProximityThresholdForClass(className2);
            return Mathf.Min(threshold1, threshold2);
        }

        private string GetConnectionKey(int playerId1, int playerId2)
        {
            // Always use smaller ID first to avoid duplicates
            if (playerId1 < playerId2)
                return $"{playerId1}_{playerId2}";
            return $"{playerId2}_{playerId1}";
        }

        /// <summary>
        /// Rebuilds connection lines using Minimum Spanning Tree (MST) algorithm
        /// This ensures connections follow shortest paths without redundant connections
        /// </summary>
        private void RebuildConnectionMST(Dictionary<int, HashSet<int>> nearbyPlayersMap)
        {
            // Build a graph of all edges (player pairs) with distances as weights
            List<Edge> allEdges = new List<Edge>();
            
            foreach (var kvp in nearbyPlayersMap)
            {
                int playerId1 = kvp.Key;
                if (!_playerData.TryGetValue(playerId1, out RamboData data1) || data1.Transform == null)
                    continue;
                
                Vector3 pos1 = data1.Transform.position;
                
                foreach (int playerId2 in kvp.Value)
                {
                    if (playerId1 >= playerId2) // Only add each edge once (smaller ID first)
                        continue;
                        
                    if (!_playerData.TryGetValue(playerId2, out RamboData data2) || data2.Transform == null)
                        continue;
                    
                    Vector3 pos2 = data2.Transform.position;
                    float distance = Vector3.Distance(pos1, pos2);
                    
                    allEdges.Add(new Edge { PlayerId1 = playerId1, PlayerId2 = playerId2, Distance = distance });
                }
            }
            
            // Sort edges by distance (shortest first) for MST
            allEdges.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            
            // Find connected components and build MST for each
            Dictionary<int, int> parent = new Dictionary<int, int>(); // For union-find
            Dictionary<int, int> rank = new Dictionary<int, int>();
            
            // Initialize union-find structure for all players that appear in edges
            // This includes both players in the map keys AND their neighbors
            HashSet<int> allPlayersInGraph = new HashSet<int>();
            foreach (var edge in allEdges)
            {
                allPlayersInGraph.Add(edge.PlayerId1);
                allPlayersInGraph.Add(edge.PlayerId2);
            }
            
            foreach (int playerId in allPlayersInGraph)
            {
                parent[playerId] = playerId;
                rank[playerId] = 0;
            }
            
            // Helper method for union-find find operation (with path compression)
            int Find(int x)
            {
                if (parent[x] != x)
                    parent[x] = Find(parent[x]); // Path compression
                return parent[x];
            }
            
            // Helper method for union-find union operation (union by rank)
            void Union(int x, int y)
            {
                int rootX = Find(x);
                int rootY = Find(y);
                
                if (rootX == rootY)
                    return;
                
                // Union by rank
                if (rank[rootX] < rank[rootY])
                    parent[rootX] = rootY;
                else if (rank[rootX] > rank[rootY])
                    parent[rootY] = rootX;
                else
                {
                    parent[rootY] = rootX;
                    rank[rootX]++;
                }
            }
            
            // Build MST using Kruskal's algorithm
            HashSet<string> mstEdges = new HashSet<string>();
            
            foreach (var edge in allEdges)
            {
                int root1 = Find(edge.PlayerId1);
                int root2 = Find(edge.PlayerId2);
                
                if (root1 != root2)
                {
                    // This edge connects two different components - add it to MST
                    Union(edge.PlayerId1, edge.PlayerId2);
                    string connectionKey = GetConnectionKey(edge.PlayerId1, edge.PlayerId2);
                    mstEdges.Add(connectionKey);
                }
            }
            
            // Remove connections that are no longer in MST
            List<string> toRemove = new List<string>();
            foreach (var kvp in _connections)
            {
                if (!mstEdges.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (string key in toRemove)
            {
                if (_connections.TryGetValue(key, out GameObject lineObj))
                {
                    UnityEngine.Object.Destroy(lineObj);
                    _connections.Remove(key);
                }
            }
            
            // Add new connections that are in MST but don't exist yet
            foreach (string connectionKey in mstEdges)
            {
                if (!_connections.ContainsKey(connectionKey))
                {
                    string[] parts = connectionKey.Split('_');
                    if (parts.Length == 2 && 
                        int.TryParse(parts[0], out int id1) && 
                        int.TryParse(parts[1], out int id2))
                    {
                        CreateConnectionLine(id1, id2);
                    }
                }
            }
        }
        
        /// <summary>
        /// Edge structure for MST algorithm
        /// </summary>
        private struct Edge
        {
            public int PlayerId1;
            public int PlayerId2;
            public float Distance;
        }

        private void CreateConnectionLine(int playerId1, int playerId2)
        {
            if (!_playerData.TryGetValue(playerId1, out RamboData data1) || 
                !_playerData.TryGetValue(playerId2, out RamboData data2))
                return;
            
            if (data1.Transform == null || data2.Transform == null || _connectionMaterial == null)
                return;
            
            string connectionKey = GetConnectionKey(playerId1, playerId2);
            
            GameObject lineObj = new GameObject($"Connection_{connectionKey}");
            lineObj.layer = 5; // UI layer - excluded from minimap
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.material = _connectionMaterial;
            lr.startWidth = LINE_WIDTH * 0.5f; // Thinner than rambo circle
            lr.endWidth = LINE_WIDTH * 0.5f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = true;
            
            _connections[connectionKey] = lineObj;
        }

        private void UpdateConnectionLines()
        {
            // Limit number of connections to update per frame for performance
            const int MAX_CONNECTIONS_PER_UPDATE = 50;
            int updated = 0;
            List<string> toRemove = new List<string>();
            
            foreach (var kvp in _connections)
            {
                if (updated >= MAX_CONNECTIONS_PER_UPDATE)
                    break;
                
                // Check if GameObject is null (destroyed)
                if (kvp.Value == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                    
                string[] parts = kvp.Key.Split('_');
                if (parts.Length != 2)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                
                if (!int.TryParse(parts[0], out int id1) || !int.TryParse(parts[1], out int id2))
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                
                if (!_playerData.TryGetValue(id1, out RamboData data1) || 
                    !_playerData.TryGetValue(id2, out RamboData data2))
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                
                if (data1.Transform == null || data2.Transform == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                
                // Remove connection if either player is not cavalry
                string className1 = (data1.ClassName ?? "").ToLower();
                string className2 = (data2.ClassName ?? "").ToLower();
                bool player1IsCavalry = className1.Contains("dragoon") || className1.Contains("hussar");
                bool player2IsCavalry = className2.Contains("dragoon") || className2.Contains("hussar");
                
                if (!player1IsCavalry || !player2IsCavalry)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                
                // Update line positions at same level as rambo circle (0.2f above player)
                Vector3 pos1 = data1.Transform.position;
                Vector3 pos2 = data2.Transform.position;
                
                Vector3 lineStart = new Vector3(pos1.x, pos1.y + 0.2f, pos1.z);
                Vector3 lineEnd = new Vector3(pos2.x, pos2.y + 0.2f, pos2.z);
                
                // Double-check GameObject is still valid before accessing component
                if (kvp.Value == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }
                
                LineRenderer lr = kvp.Value.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.SetPosition(0, lineStart);
                    lr.SetPosition(1, lineEnd);
                    updated++;
                }
                else
                {
                    // LineRenderer component missing - remove connection
                    toRemove.Add(kvp.Key);
                }
            }
            
            // Clean up invalid connections
            foreach (string key in toRemove)
            {
                if (_connections.TryGetValue(key, out GameObject lineObj))
                {
                    if (lineObj != null)
                        UnityEngine.Object.Destroy(lineObj);
                    _connections.Remove(key);
                }
            }
        }

        /// <summary>
        /// Component composition tracking for class-specific line validation
        /// </summary>
        public class ComponentComposition
        {
            public Dictionary<string, int> ClassCounts = new Dictionary<string, int>();
            public int TotalCount = 0;
        }
        
        /// <summary>
        /// Checks if a player is "in-group" (within InGroupRadius of InGroupMinPlayers total players including self)
        /// InGroupMinPlayers = 3 means: player (1) + 2 others = 3 total
        /// This prevents false rambo calls during movement when players are catching up to their group
        /// Only considers players of the same faction
        /// </summary>
        private bool IsInGroup(int playerId, RamboData data)
        {
            if (data.Transform == null || string.IsNullOrEmpty(data.ClassName) || string.IsNullOrEmpty(data.Faction))
                return false;

            float inGroupRadius = ClassSettings.GetInGroupRadius(data.ClassName);
            int minPlayers = ClassSettings.GetInGroupMinPlayers(data.ClassName);

            Vector3 playerPos = data.Transform.position;
            int nearbyCount = 1; // Start at 1 to count self

            // Check all other players to see if enough are within the in-group radius
            // Only consider players of the same faction
            foreach (var kvp in _playerData)
            {
                if (kvp.Key == playerId)
                    continue;

                RamboData otherData = kvp.Value;
                if (otherData.Transform == null)
                    continue;
                
                // Only consider same-faction players
                if (string.IsNullOrEmpty(otherData.Faction) || !otherData.Faction.Equals(data.Faction, StringComparison.OrdinalIgnoreCase))
                    continue;

                float distance = Vector3.Distance(playerPos, otherData.Transform.position);
                if (distance <= inGroupRadius)
                {
                    nearbyCount++;
                    if (nearbyCount >= minPlayers)
                        return true; // Found enough total players (self + others)
                }
            }

            return false; // Not enough total players
        }
        
        /// <summary>
        /// Updates skirmisher counts per faction (Rifleman and Light Infantry)
        /// </summary>
        private void UpdateSkirmisherCounts()
        {
            _riflemanPlayersByFaction.Clear();
            _lightInfantryPlayersByFaction.Clear();
            _skirmisherCountsByFaction.Clear();
            
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                RamboData data = kvp.Value;
                
                if (data.Transform == null || string.IsNullOrEmpty(data.Faction))
                    continue;
                
                string classLower = (data.ClassName ?? "").ToLower();
                bool isRifleman = classLower.Contains("rifleman");
                bool isLightInfantry = classLower.Contains("lightinfantry") || classLower.Contains("light infantry");
                
                if (isRifleman)
                {
                    if (!_riflemanPlayersByFaction.ContainsKey(data.Faction))
                        _riflemanPlayersByFaction[data.Faction] = new List<int>();
                    _riflemanPlayersByFaction[data.Faction].Add(playerId);
                }
                
                if (isLightInfantry)
                {
                    if (!_lightInfantryPlayersByFaction.ContainsKey(data.Faction))
                        _lightInfantryPlayersByFaction[data.Faction] = new List<int>();
                    _lightInfantryPlayersByFaction[data.Faction].Add(playerId);
                }
            }
            
            // Update counts and log warnings
            foreach (var kvp in _riflemanPlayersByFaction)
            {
                string faction = kvp.Key;
                int riflemanCount = kvp.Value.Count;
                int lightInfantryCount = _lightInfantryPlayersByFaction.TryGetValue(faction, out var li) ? li.Count : 0;
                
                _skirmisherCountsByFaction[faction] = (riflemanCount, lightInfantryCount);
                
                if (riflemanCount > 5)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[Skirmishers] ⚠️ Faction {faction} has {riflemanCount} Rifleman alive (max 5 allowed)");
                }
            }
            
            foreach (var kvp in _lightInfantryPlayersByFaction)
            {
                string faction = kvp.Key;
                int lightInfantryCount = kvp.Value.Count;
                int riflemanCount = _riflemanPlayersByFaction.TryGetValue(faction, out var rifles) ? rifles.Count : 0;
                
                if (!_skirmisherCountsByFaction.ContainsKey(faction))
                    _skirmisherCountsByFaction[faction] = (riflemanCount, lightInfantryCount);
            }
        }
        
        /// <summary>
        /// Updates surgeon proximity detection - determines if surgeons are near lines or skirmishers
        /// </summary>
        private void UpdateSurgeonProximity()
        {
            _surgeonProximity.Clear();
            
            // Find all surgeons
            List<(int playerId, RamboData data, string faction)> surgeons = new List<(int, RamboData, string)>();
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                RamboData data = kvp.Value;
                
                if (data.Transform == null || string.IsNullOrEmpty(data.Faction))
                    continue;
                
                string classLower = (data.ClassName ?? "").ToLower();
                if (classLower.Contains("surgeon"))
                {
                    surgeons.Add((playerId, data, data.Faction));
                }
            }
            
            // For each surgeon, check proximity to lines and skirmishers
            foreach (var surgeon in surgeons)
            {
                int surgeonId = surgeon.playerId;
                Vector3 surgeonPos = surgeon.data.Transform.position;
                string faction = surgeon.faction;
                
                bool isNearLine = false;
                bool isNearSkirmishers = false;
                string nearestGroupType = "none";
                float nearestLineDist = float.MaxValue;
                float nearestSkirmisherDist = float.MaxValue;
                
                // Check proximity to line infantry (within 5m = part of line)
                foreach (var kvp in _playerData)
                {
                    if (kvp.Key == surgeonId)
                        continue;
                    
                    RamboData otherData = kvp.Value;
                    if (otherData.Transform == null || string.IsNullOrEmpty(otherData.Faction))
                        continue;
                    
                    // Only check same faction
                    if (!otherData.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    string classLower = (otherData.ClassName ?? "").ToLower();
                    float distance = Vector3.Distance(surgeonPos, otherData.Transform.position);
                    
                    // Check if near line infantry (LineInfantry, Grenadier, Guard, Officer)
                    bool isLineInfantry = classLower.Contains("lineinfantry") || classLower.Contains("line infantry") ||
                                         classLower.Contains("grenadier") || classLower.Contains("guard") ||
                                         (classLower.Contains("officer") && !classLower.Contains("naval"));
                    
                    if (isLineInfantry && distance <= SURGEON_PROXIMITY_THRESHOLD)
                    {
                        if (distance < nearestLineDist)
                        {
                            nearestLineDist = distance;
                            isNearLine = true;
                            nearestGroupType = "line";
                        }
                    }
                    
                    // Check if near skirmishers (Rifleman, Light Infantry)
                    bool isSkirmisher = classLower.Contains("rifleman") || 
                                       classLower.Contains("lightinfantry") || 
                                       classLower.Contains("light infantry");
                    
                    if (isSkirmisher && distance <= SURGEON_PROXIMITY_THRESHOLD)
                    {
                        if (distance < nearestSkirmisherDist)
                        {
                            nearestSkirmisherDist = distance;
                            isNearSkirmishers = true;
                            if (nearestSkirmisherDist < nearestLineDist)
                                nearestGroupType = "skirmishers";
                        }
                    }
                }
                
                // If surgeon is near both, prioritize the closer one
                if (isNearLine && isNearSkirmishers)
                {
                    if (nearestLineDist < nearestSkirmisherDist)
                    {
                        isNearSkirmishers = false;
                        nearestGroupType = "line";
                    }
                    else
                    {
                        isNearLine = false;
                        nearestGroupType = "skirmishers";
                    }
                }
                
                _surgeonProximity[surgeonId] = (isNearLine, isNearSkirmishers, nearestGroupType);
            }
        }
        
        /// <summary>
        /// Updates cavalry counts per faction (Hussars and Dragoons)
        /// </summary>
        private void UpdateCavalryCounts()
        {
            _hussarPlayersByFaction.Clear();
            _dragoonPlayersByFaction.Clear();
            _cavalryCountsByFaction.Clear();
            
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                RamboData data = kvp.Value;
                
                if (data.Transform == null || string.IsNullOrEmpty(data.Faction))
                    continue;
                
                string classLower = (data.ClassName ?? "").ToLower();
                bool isHussar = classLower.Contains("hussar");
                bool isDragoon = classLower.Contains("dragoon");
                
                if (isHussar)
                {
                    if (!_hussarPlayersByFaction.ContainsKey(data.Faction))
                        _hussarPlayersByFaction[data.Faction] = new List<int>();
                    _hussarPlayersByFaction[data.Faction].Add(playerId);
                }
                
                if (isDragoon)
                {
                    if (!_dragoonPlayersByFaction.ContainsKey(data.Faction))
                        _dragoonPlayersByFaction[data.Faction] = new List<int>();
                    _dragoonPlayersByFaction[data.Faction].Add(playerId);
                }
            }
            
            // Update counts and log warnings
            foreach (var kvp in _hussarPlayersByFaction)
            {
                string faction = kvp.Key;
                int hussarCount = kvp.Value.Count;
                int dragoonCount = _dragoonPlayersByFaction.TryGetValue(faction, out var dragoons) ? dragoons.Count : 0;
                
                _cavalryCountsByFaction[faction] = (hussarCount, dragoonCount);
                
                if (hussarCount > 6)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[Cavalry] ⚠️ Faction {faction} has {hussarCount} Hussars alive (max 6 allowed)");
                }
            }
            
            foreach (var kvp in _dragoonPlayersByFaction)
            {
                string faction = kvp.Key;
                int dragoonCount = kvp.Value.Count;
                int hussarCount = _hussarPlayersByFaction.TryGetValue(faction, out var hussars) ? hussars.Count : 0;
                
                if (!_cavalryCountsByFaction.ContainsKey(faction))
                    _cavalryCountsByFaction[faction] = (hussarCount, dragoonCount);
                
                if (dragoonCount > 6)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[Cavalry] ⚠️ Faction {faction} has {dragoonCount} Dragoons alive (max 6 allowed)");
                }
            }
        }
        
        /// <summary>
        /// Handles rambo logic specifically for Hussars and Dragoons
        /// Rules:
        /// - If >6 alive: Log warning (already done in UpdateCavalryCounts)
        /// - If exactly 6: Check in-group radius, mark those outside as rambo
        /// - If only 2 left: Switch to Officer Class rules (handled in ValidateLineGroup)
        /// </summary>
        private void HandleCavalryRamboLogic(int playerId, RamboData data, bool isHussar, bool isDragoon)
        {
            if (string.IsNullOrEmpty(data.Faction))
                return;
            
            // Get cavalry count for this faction
            if (!_cavalryCountsByFaction.TryGetValue(data.Faction, out var counts))
                return;
            
            int cavalryCount = isHussar ? counts.hussarCount : counts.dragoonCount;
            List<int> cavalryPlayers = isHussar 
                ? (_hussarPlayersByFaction.TryGetValue(data.Faction, out var h) ? h : new List<int>())
                : (_dragoonPlayersByFaction.TryGetValue(data.Faction, out var d) ? d : new List<int>());
            
            // If only 2 left, they follow Officer Class rules (handled in ValidateLineGroup)
            // Just check normal in-group logic here
            if (cavalryCount == 2)
            {
                // Use normal rambo logic (they'll be validated as Officer Class in ValidateLineGroup)
                bool isInGroup = IsInGroup(playerId, data);
                if (!isInGroup)
                {
                    int componentSize = _cachedComponentSizes.TryGetValue(playerId, out int size) ? size : 1;
                    ComponentComposition composition = _cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp) ? comp : null;
                    bool isInValidLine = IsValidLineForPlayer(data.ClassName, componentSize, composition);
                    
                    if (!isInValidLine)
                    {
                        data.TimeAlone += Time.deltaTime;
                        float ramboThreshold = ClassSettings.GetRamboTimeThreshold(data.ClassName);
                        if (data.TimeAlone >= ramboThreshold && !data.IsRambo)
                        {
                            data.IsRambo = true;
                            CreateRamboIndicator(data.Transform, playerId, data);
                        }
                    }
                    else
                    {
                        if (data.TimeAlone > 0)
                        {
                            data.TimeAlone = 0f;
                            if (data.IsRambo)
                            {
                                data.IsRambo = false;
                                RemoveRamboIndicator(playerId, data);
                            }
                        }
                    }
                }
                else
                {
                    if (data.TimeAlone > 0)
                    {
                        data.TimeAlone = 0f;
                        if (data.IsRambo)
                        {
                            data.IsRambo = false;
                            RemoveRamboIndicator(playerId, data);
                        }
                    }
                }
                return;
            }
            
            // If exactly 6, check in-group radius
            if (cavalryCount == 6)
            {
                float inGroupRadius = ClassSettings.GetInGroupRadius(data.ClassName);
                Vector3 playerPos = data.Transform.position;
                
                // Count how many other cavalry of same type are within radius
                int nearbyCavalryCount = 1; // Start at 1 to count self
                
                foreach (int otherPlayerId in cavalryPlayers)
                {
                    if (otherPlayerId == playerId)
                        continue;
                    
                    if (!_playerData.TryGetValue(otherPlayerId, out RamboData otherData))
                        continue;
                    
                    if (otherData.Transform == null)
                        continue;
                    
                    float distance = Vector3.Distance(playerPos, otherData.Transform.position);
                    if (distance <= inGroupRadius)
                    {
                        nearbyCavalryCount++;
                    }
                }
                
                // Need at least 3 total (self + 2 others) to not be rambo
                int minPlayers = ClassSettings.GetInGroupMinPlayers(data.ClassName);
                if (nearbyCavalryCount >= minPlayers)
                {
                    // In group - not rambo
                    if (data.TimeAlone > 0)
                    {
                        data.TimeAlone = 0f;
                        if (data.IsRambo)
                        {
                            data.IsRambo = false;
                            RemoveRamboIndicator(playerId, data);
                        }
                    }
                }
                else
                {
                    // Outside group radius - mark as rambo immediately
                    if (!data.IsRambo)
                    {
                        data.IsRambo = true;
                        CreateRamboIndicator(data.Transform, playerId, data);
                    }
                }
                return;
            }
            
            // If >6, they're all invalid (already logged warning)
            // Mark as rambo
            if (cavalryCount > 6)
            {
                if (!data.IsRambo)
                {
                    data.IsRambo = true;
                    CreateRamboIndicator(data.Transform, playerId, data);
                }
                return;
            }
            
            // For other counts (3-5), use normal validation
            bool isInGroupNormal = IsInGroup(playerId, data);
            if (!isInGroupNormal)
            {
                int componentSize = _cachedComponentSizes.TryGetValue(playerId, out int size) ? size : 1;
                ComponentComposition composition = _cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp) ? comp : null;
                bool isInValidLine = IsValidLineForPlayer(data.ClassName, componentSize, composition);
                
                if (!isInValidLine)
                {
                    data.TimeAlone += Time.deltaTime;
                    float ramboThreshold = ClassSettings.GetRamboTimeThreshold(data.ClassName);
                    if (data.TimeAlone >= ramboThreshold && !data.IsRambo)
                    {
                        data.IsRambo = true;
                        CreateRamboIndicator(data.Transform, playerId, data);
                    }
                }
                else
                {
                    if (data.TimeAlone > 0)
                    {
                        data.TimeAlone = 0f;
                        if (data.IsRambo)
                        {
                            data.IsRambo = false;
                            RemoveRamboIndicator(playerId, data);
                        }
                    }
                }
            }
            else
            {
                if (data.TimeAlone > 0)
                {
                    data.TimeAlone = 0f;
                    if (data.IsRambo)
                    {
                        data.IsRambo = false;
                        RemoveRamboIndicator(playerId, data);
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks if a player is in a valid line based on class-specific rules
        /// </summary>
        private bool IsValidLineForPlayer(string className, int componentSize, ComponentComposition composition)
        {
            if (string.IsNullOrEmpty(className))
                return componentSize >= ClassSettings.GetMinLineSize(className);
            
            string classLower = className.ToLower();
            
            // Check if this class has a makeup rule (e.g., Skirmishers)
            var makeupRule = ClassSettings.GetMakeupRule(className);
            if (makeupRule != null)
            {
                if (composition == null)
                {
                    // Fallback: if no composition data, use component size check
                    return componentSize >= ClassSettings.GetMinLineSize(className);
                }
                
                // Check all possible class name variations (case-insensitive matching)
                int primaryClassCount = 0;
                int surgeonCount = 0;
                int officerCount = 0;
                int otherClassCount = 0; // Count other classes (not primary, not surgeon)
                
                foreach (var kvp in composition.ClassCounts)
                {
                    string classKey = kvp.Key.ToLower();
                    string primaryClassLower = makeupRule.PrimaryClassName.ToLower();
                    
                    if (classKey.Contains(primaryClassLower) || primaryClassLower.Contains(classKey))
                        primaryClassCount += kvp.Value;
                    else if (classKey.Contains("surgeon"))
                        surgeonCount += kvp.Value;
                    else if (classKey.Contains("officer") && !classKey.Contains("naval"))
                        officerCount += kvp.Value;
                    else
                        otherClassCount += kvp.Value; // Any other class
                }
                
                // Check if this is a Skirmisher rule (for broken skirmisher handling)
                bool isSkirmisherRule = makeupRule.RuleName.Contains("Skirmisher");
                
                // Broken Skirmishers: Less than MinFormationSize (3) members
                // Broken skirmishers may act as a line's Attached Unit, so allow them to join lines
                if (isSkirmisherRule && componentSize < makeupRule.MinFormationSize)
                {
                    // Check if they can join as attached units to a line (validate line composition)
                    // Officers can be with 1-2 Light Infantry as attached units
                    return ClassSettings.ValidateLineComposition(composition.ClassCounts);
                }
                
                // Skirmisher Groups: Cannot have Officers or other classes
                // Officers can only be with 1-2 Light Infantry (attached units), not full Skirmisher groups
                if (isSkirmisherRule)
                {
                    // Full Skirmisher group (3+ members) cannot have Officers or other classes
                    if (officerCount > 0 || otherClassCount > 0)
                    {
                        // Invalid: Officer or other class in Skirmisher group
                        return false;
                    }
                }
                
                // Cavalry: Max 6 Dragoons or Max 6 Hussars
                // Cavalry can operate solo (MinFormationSize = 1), so no broken state
                // Valid if: <= MaxPrimaryClass AND <= MaxSurgeons
                bool isValid = primaryClassCount <= makeupRule.MaxPrimaryClass && surgeonCount <= makeupRule.MaxSurgeons;
                
                // Debug logging (can be removed later)
                if (!isValid && primaryClassCount > 0)
                {
                    AdvancedAdminUIMod.Log.LogInfo($"[Rambo] {makeupRule.PrimaryClassName} validation: Count={primaryClassCount}, Surgeons={surgeonCount}, ComponentSize={componentSize}, Valid={isValid}");
                }
                
                return isValid;
            }
            
            // Surgeon: Valid if they're part of a valid skirmisher line (checked by Light Infantry/Rifleman makeup rules)
            if (classLower.Contains("surgeon"))
            {
                if (composition == null)
                    return componentSize >= ClassSettings.GetMinLineSize(className);
                
                // Check if there are Light Infantry or Rifleman in the component
                int lightInfantryCount = 0;
                int riflemanCount = 0;
                int surgeonCount = 0;
                
                foreach (var kvp in composition.ClassCounts)
                {
                    string classKey = kvp.Key.ToLower();
                    if (classKey.Contains("lightinfantry") || classKey.Contains("light infantry"))
                        lightInfantryCount += kvp.Value;
                    else if (classKey.Contains("rifleman"))
                        riflemanCount += kvp.Value;
                    else if (classKey.Contains("surgeon"))
                        surgeonCount += kvp.Value;
                }
                
                // Valid if part of valid Light Infantry line
                if (lightInfantryCount > 0)
                {
                    var liRule = ClassSettings.GetMakeupRule("LightInfantry");
                    if (liRule != null)
                        return lightInfantryCount <= liRule.MaxPrimaryClass && surgeonCount <= liRule.MaxSurgeons;
                }
                
                // Valid if part of valid Rifleman line
                if (riflemanCount > 0)
                {
                    var rifleRule = ClassSettings.GetMakeupRule("Rifleman");
                    if (rifleRule != null)
                        return riflemanCount <= rifleRule.MaxPrimaryClass && surgeonCount <= rifleRule.MaxSurgeons;
                }
                
                // If no LI or Rifleman, check line composition rules
                if (composition != null && composition.ClassCounts.Count > 0)
                {
                    return ClassSettings.ValidateLineComposition(composition.ClassCounts);
                }
                
                // Fallback to default rule
                return componentSize >= ClassSettings.GetMinLineSize(className);
            }
            
            // Check if Officer is part of a Skirmisher group (not allowed)
            // Officers can only be with 1-2 Light Infantry (attached units), not full Skirmisher groups
            if (classLower.Contains("officer") && !classLower.Contains("naval"))
            {
                if (composition != null && composition.ClassCounts.Count > 0)
                {
                    int lightInfantryCount = 0;
                    int riflemanCount = 0;
                    int surgeonCount = 0;
                    
                    foreach (var kvp in composition.ClassCounts)
                    {
                        string classKey = kvp.Key.ToLower();
                        if (classKey.Contains("lightinfantry") || classKey.Contains("light infantry"))
                            lightInfantryCount += kvp.Value;
                        else if (classKey.Contains("rifleman"))
                            riflemanCount += kvp.Value;
                        else if (classKey.Contains("surgeon"))
                            surgeonCount += kvp.Value;
                    }
                    
                    // Check if this is a full Skirmisher group (3+ Light Infantry/Rifleman + Surgeon)
                    // Officers cannot be part of full Skirmisher groups
                    if (lightInfantryCount >= 3 || riflemanCount >= 3)
                    {
                        // This is a Skirmisher group - Officer cannot be part of it
                        // Officers can only be with 1-2 Light Infantry as attached units
                        return false;
                    }
                    
                    // If 1-2 Light Infantry, check line composition (Officer can be with attached units)
                    if (lightInfantryCount > 0 || riflemanCount > 0)
                    {
                        return ClassSettings.ValidateLineComposition(composition.ClassCounts);
                    }
                }
            }
            
            // Check Line Composition Rules for standard line formations
            if (composition != null && composition.ClassCounts.Count > 0)
            {
                bool isValidComposition = ClassSettings.ValidateLineComposition(composition.ClassCounts);
                if (!isValidComposition)
                {
                    // Invalid line composition - should be highlighted red (rambo)
                    return false;
                }
            }
            
            // Default: Use minimum line size check
            int minLineSize = GetMinLineSizeForClass(className);
            return componentSize >= minLineSize;
        }
        
        private int GetMinLineSizeForClass(string className)
        {
            return ClassSettings.GetMinLineSize(className);
        }
        
        /// <summary>
        /// Checks if a group contains dragoon or hussar players
        /// </summary>
        private bool GroupContainsCavalry(HashSet<int> playerIds, ComponentComposition composition)
        {
            if (playerIds == null || playerIds.Count == 0)
                return false;
            
            // Check composition first (faster)
            if (composition != null && composition.ClassCounts != null)
            {
                foreach (var kvp in composition.ClassCounts)
                {
                    string classKey = kvp.Key.ToLower();
                    if (classKey.Contains("dragoon") || classKey.Contains("hussar"))
                        return true;
                }
            }
            
            // Fallback: check individual players
            foreach (int playerId in playerIds)
            {
                if (_playerData.TryGetValue(playerId, out RamboData data))
                {
                    string className = (data.ClassName ?? "").ToLower();
                    if (className.Contains("dragoon") || className.Contains("hussar"))
                        return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Validates a line group and returns validation status with reason
        /// Only validates dragoon and hussar groups. All other classes are automatically valid.
        /// </summary>
        private (bool isValid, string reason) ValidateLineGroup(HashSet<int> playerIds, ComponentComposition composition, int componentSize)
        {
            if (playerIds == null || playerIds.Count == 0)
                return (false, "Empty group");
            
            if (composition == null || composition.ClassCounts.Count == 0)
            {
                // Can't validate without composition - but this might indicate an issue
                // Return false if we have players but no composition data
                if (playerIds.Count > 0)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[ValidateLineGroup] No composition data for group with {playerIds.Count} players");
                    return (false, "No composition data");
                }
                return (true, ""); // Empty group is technically valid
            }
            
            // Check if this group contains dragoon or hussar
            bool hasDragoon = false;
            bool hasHussar = false;
            foreach (var kvp in composition.ClassCounts)
            {
                string classKey = kvp.Key.ToLower();
                if (classKey.Contains("dragoon"))
                {
                    hasDragoon = true;
                    break;
                }
                if (classKey.Contains("hussar"))
                {
                    hasHussar = true;
                    break;
                }
            }
            
            // For non-dragoon/hussar groups, skip validation - they're automatically valid
            // They only need to check group spacing (handled by rambo detection)
            if (!hasDragoon && !hasHussar)
            {
                return (true, "");
            }
            
            // Check each player in the group
            bool allValid = true;
            List<string> reasons = new List<string>();
            
            // Count all classes in composition
            int lightInfantryCount = 0;
            int riflemanCount = 0;
            int surgeonCount = 0;
            int officerCount = 0;
            int dragoonCount = 0;
            int hussarCount = 0;
            int lineInfantryCount = 0; // ArmyLineInfantry, Grenadier, Guard
            
            // Count all classes in composition
            int totalNonSkirmisherClasses = 0;
            foreach (var kvp in composition.ClassCounts)
            {
                string classKey = kvp.Key.ToLower();
                int count = kvp.Value;
                
                if (classKey.Contains("lightinfantry") || classKey.Contains("light infantry"))
                    lightInfantryCount = count;
                else if (classKey.Contains("rifleman"))
                    riflemanCount = count;
                else if (classKey.Contains("surgeon"))
                    surgeonCount = count;
                else if (classKey.Contains("officer") && !classKey.Contains("naval"))
                {
                    officerCount = count;
                    totalNonSkirmisherClasses++;
                }
                else if (classKey.Contains("dragoon"))
                    dragoonCount = count;
                else if (classKey.Contains("hussar"))
                    hussarCount = count;
                else if (classKey.Contains("lineinfantry") || classKey.Contains("line infantry") || 
                         classKey.Contains("grenadier") || classKey.Contains("guard"))
                {
                    lineInfantryCount += count;
                    totalNonSkirmisherClasses++;
                }
                else
                {
                    // Other classes that aren't skirmisher-related
                    totalNonSkirmisherClasses++;
                }
            }
            
            // Get faction from first player in group
            string faction = "";
            foreach (int playerId in playerIds)
            {
                if (_playerData.TryGetValue(playerId, out RamboData data) && !string.IsNullOrEmpty(data.Faction))
                {
                    faction = data.Faction;
                    break;
                }
            }
            
            // Check team-wide skirmisher limits
            if (!string.IsNullOrEmpty(faction) && _skirmisherCountsByFaction.TryGetValue(faction, out var skirmisherCounts))
            {
                // Check rifleman team limit (max 5 + 1 surgeon)
                if (riflemanCount > 0 && skirmisherCounts.riflemanCount > 5)
                {
                    reasons.Add($"Too many Rifleman on team ({skirmisherCounts.riflemanCount}/5 max)");
                    allValid = false;
                }
                
                // Check if rifleman are split (all must be together)
                if (riflemanCount > 0 && skirmisherCounts.riflemanCount > riflemanCount)
                {
                    // Some rifleman are not in this group - they're split
                    reasons.Add($"Rifleman are split ({riflemanCount}/{skirmisherCounts.riflemanCount} in this group)");
                    allValid = false;
                }
            }
            
            // Check Skirmisher rules FIRST (before line composition)
            // Valid skirmisher groups take precedence over line composition rules
            bool isSkirmisherGroup = false;
            bool isValidSkirmisher = false;
            
            // Determine surgeon proximity for this group
            int surgeonsInGroup = 0;
            int surgeonsNearLine = 0;
            int surgeonsNearSkirmishers = 0;
            foreach (int playerId in playerIds)
            {
                if (_playerData.TryGetValue(playerId, out RamboData data))
                {
                    string classLower = (data.ClassName ?? "").ToLower();
                    if (classLower.Contains("surgeon"))
                    {
                        surgeonsInGroup++;
                        if (_surgeonProximity.TryGetValue(playerId, out var proximity))
                        {
                            if (proximity.isNearLine)
                                surgeonsNearLine++;
                            if (proximity.isNearSkirmishers)
                                surgeonsNearSkirmishers++;
                        }
                    }
                }
            }
            
            if (lightInfantryCount > 0)
            {
                var liRule = ClassSettings.GetMakeupRule("LightInfantry");
                if (liRule != null)
                {
                    isSkirmisherGroup = true;
                    
                    // Check if it's a valid skirmisher group
                    // Valid skirmisher: 
                    //   - 2-7 LI + 1 Surgeon (2+1 = 3 total, valid)
                    //   - 3-7 LI + 0-1 Surgeon (standard skirmisher)
                    //   - No other classes
                    bool validCount = lightInfantryCount <= liRule.MaxPrimaryClass;
                    bool validSurgeon = surgeonCount <= liRule.MaxSurgeons;
                    // Special case: 2 LI + 1 Surgeon is valid (total 3, but 2 primary class)
                    bool isSpecialCase = lightInfantryCount == 2 && surgeonCount == 1;
                    // Standard case: 3+ LI (with or without surgeon)
                    bool isStandardCase = lightInfantryCount >= liRule.MinFormationSize;
                    // Check if only LI and Surgeon (no officers, no other classes)
                    bool onlyLiAndSurgeon = totalNonSkirmisherClasses == 0 && officerCount == 0;
                    
                    // Check surgeon rules: 2 skirms + 1 surgeon valid, 2 surgeons + 1 skirm invalid
                    if (surgeonCount == 2 && lightInfantryCount == 1)
                    {
                        reasons.Add("Invalid: 2 Surgeons + 1 Light Infantry not allowed");
                        allValid = false;
                    }
                    
                    // Check if LI < 3 - can join line as attached skirms (line may be invalid but that's okay)
                    if (lightInfantryCount > 0 && lightInfantryCount < liRule.MinFormationSize)
                    {
                        // Broken LI (< 3) - can be attached to a line as attached skirms
                        // Line may be invalid due to too many attached units, but that's acceptable
                        // Continue to line composition check below (will show invalid with duration)
                    }
                    else if (validCount && validSurgeon && onlyLiAndSurgeon && (isSpecialCase || isStandardCase))
                    {
                        // Valid skirmisher group - return valid immediately (skip line composition check)
                        isValidSkirmisher = true;
                        return (true, "");
                    }
                    else
                    {
                        // Invalid skirmisher - check why
                        if (lightInfantryCount > liRule.MaxPrimaryClass)
                        {
                            reasons.Add($"Too many Light Infantry ({lightInfantryCount}/{liRule.MaxPrimaryClass})");
                        }
                        if (surgeonCount > liRule.MaxSurgeons)
                        {
                            reasons.Add($"Too many Surgeons ({surgeonCount}/{liRule.MaxSurgeons})");
                        }
                        if (componentSize >= liRule.MinFormationSize && (officerCount > 0 || composition.ClassCounts.Count > 2))
                        {
                            reasons.Add("Officer/other classes in Skirmisher group");
                        }
                    }
                }
            }
            
            if (riflemanCount > 0)
            {
                var rifleRule = ClassSettings.GetMakeupRule("Rifleman");
                if (rifleRule != null)
                {
                    isSkirmisherGroup = true;
                    
                    // Check if it's a valid skirmisher group
                    // Valid skirmisher:
                    //   - 2-5 Rifles + 1 Surgeon (2+1 = 3 total, valid)
                    //   - 3-5 Rifles + 0-1 Surgeon (standard skirmisher)
                    //   - No other classes
                    bool validCount = riflemanCount <= rifleRule.MaxPrimaryClass;
                    bool validSurgeon = surgeonCount <= rifleRule.MaxSurgeons;
                    // Special case: 2 Rifles + 1 Surgeon is valid (total 3, but 2 primary class)
                    bool isSpecialCase = riflemanCount == 2 && surgeonCount == 1;
                    // Standard case: 3+ Rifles (with or without surgeon)
                    bool isStandardCase = riflemanCount >= rifleRule.MinFormationSize;
                    // Check if only Rifles and Surgeon (no officers, no other classes)
                    bool onlyRiflesAndSurgeon = totalNonSkirmisherClasses == 0 && officerCount == 0;
                    
                    if (validCount && validSurgeon && onlyRiflesAndSurgeon && (isSpecialCase || isStandardCase))
                    {
                        // Valid skirmisher group - return valid immediately (skip line composition check)
                        isValidSkirmisher = true;
                        return (true, "");
                    }
                    else
                    {
                        // Invalid skirmisher - check why
                        if (riflemanCount > rifleRule.MaxPrimaryClass)
                        {
                            reasons.Add($"Too many Rifles ({riflemanCount}/{rifleRule.MaxPrimaryClass})");
                        }
                        if (riflemanCount < rifleRule.MinFormationSize)
                        {
                            // Broken skirmishers (< 3) - can be attached to a line, so don't mark invalid yet
                            // Will check line composition rules below
                        }
                        if (surgeonCount > rifleRule.MaxSurgeons)
                        {
                            reasons.Add($"Too many Surgeons ({surgeonCount}/{rifleRule.MaxSurgeons})");
                        }
                        if (componentSize >= rifleRule.MinFormationSize && (officerCount > 0 || composition.ClassCounts.Count > 2))
                        {
                            reasons.Add("Officer/other classes in Skirmisher group");
                        }
                    }
                }
            }
            
            // Check Cavalry rules (specialized rules - check BEFORE line rules)
            // Special case: If only 2 cavalry left alive in faction, they follow Officer Class rules instead
            bool shouldUseOfficerRules = false;
            
            // Check if only 2 cavalry left in faction
            if (!string.IsNullOrEmpty(faction) && _cavalryCountsByFaction.TryGetValue(faction, out var factionCounts))
            {
                if (dragoonCount > 0 && factionCounts.dragoonCount == 2)
                {
                    shouldUseOfficerRules = true;
                }
                if (hussarCount > 0 && factionCounts.hussarCount == 2)
                {
                    shouldUseOfficerRules = true;
                }
            }
            
            // If only 2 cavalry left, skip cavalry rules and use Officer Class rules instead
            if (!shouldUseOfficerRules)
            {
                // Dragoons can only be with Dragoons (no Hussars, no other classes)
                if (dragoonCount > 0)
                {
                    var dragoonRule = ClassSettings.GetMakeupRule("Dragoon");
                    if (dragoonRule != null)
                    {
                        // Check max count
                        if (dragoonCount > dragoonRule.MaxPrimaryClass)
                        {
                            allValid = false;
                            reasons.Add($"Too many Dragoons ({dragoonCount}/{dragoonRule.MaxPrimaryClass})");
                        }
                        
                        // Check minimum formation size (need at least MinFormationSize Dragoons)
                        if (dragoonCount > 0 && dragoonCount < dragoonRule.MinFormationSize)
                        {
                            allValid = false;
                            reasons.Add($"Too few Dragoons ({dragoonCount}/{dragoonRule.MinFormationSize} minimum)");
                        }
                        
                        // Dragoons can ONLY be with Dragoons - check for mixed classes
                        int totalCavalry = dragoonCount + hussarCount;
                        int totalNonCavalry = componentSize - totalCavalry;
                        
                        if (hussarCount > 0)
                        {
                            allValid = false;
                            reasons.Add($"Dragoons cannot be mixed with Hussars");
                        }
                        
                        if (totalNonCavalry > 0)
                        {
                            allValid = false;
                            reasons.Add($"Dragoons cannot be mixed with other classes");
                        }
                    }
                }
                
                // Hussars can only be with Hussars (no Dragoons, no other classes)
                if (hussarCount > 0)
                {
                    var hussarRule = ClassSettings.GetMakeupRule("Hussar");
                    if (hussarRule != null)
                    {
                        // Check max count
                        if (hussarCount > hussarRule.MaxPrimaryClass)
                        {
                            allValid = false;
                            reasons.Add($"Too many Hussars ({hussarCount}/{hussarRule.MaxPrimaryClass})");
                        }
                        
                        // Check minimum formation size (need at least MinFormationSize Hussars)
                        if (hussarCount > 0 && hussarCount < hussarRule.MinFormationSize)
                        {
                            allValid = false;
                            reasons.Add($"Too few Hussars ({hussarCount}/{hussarRule.MinFormationSize} minimum)");
                        }
                        
                        // Hussars can ONLY be with Hussars - check for mixed classes
                        int totalCavalry = dragoonCount + hussarCount;
                        int totalNonCavalry = componentSize - totalCavalry;
                        
                        if (dragoonCount > 0)
                        {
                            allValid = false;
                            reasons.Add($"Hussars cannot be mixed with Dragoons");
                        }
                        
                        if (totalNonCavalry > 0)
                        {
                            allValid = false;
                            reasons.Add($"Hussars cannot be mixed with other classes");
                        }
                    }
                }
                
                // If cavalry validation failed, return invalid immediately
                if (dragoonCount > 0 || hussarCount > 0)
                {
                    if (!allValid)
                    {
                        string cavalryReason = string.Join(", ", reasons);
                        return (false, cavalryReason);
                    }
                    // Valid cavalry group - return valid immediately
                    return (true, "");
                }
            }
            // If shouldUseOfficerRules is true, fall through to line composition rules below
            
            // Check Line Specialized Rules (before general line composition)
            // Valid line: 2 Line Infantry + 1 Officer OR 3+ Line Infantry
            bool isValidLine = false;
            if (lineInfantryCount > 0)
            {
                // Special case: 2 Line Infantry + 1 Officer is valid
                bool isSpecialCase = lineInfantryCount == 2 && officerCount == 1;
                // Standard case: 3+ Line Infantry
                bool isStandardCase = lineInfantryCount >= 3;
                
                if (isSpecialCase || isStandardCase)
                {
                    isValidLine = true;
                    // Still need to check general line composition rules for other violations
                }
            }
            
            // Check General Line Composition Rules (only if not a valid skirmisher group)
            // Valid skirmisher groups are already handled above and returned early
            bool isValidComposition = true;
            
            // Skip line composition check if it's a valid skirmisher group
            if (isSkirmisherGroup && isValidSkirmisher)
            {
                // Should have returned already, but just in case, skip line composition
                isValidComposition = true;
            }
            else
            {
                // Check general line composition rules for all non-skirmisher groups
                var (compValid, compositionReason) = ClassSettings.ValidateLineCompositionWithReason(composition.ClassCounts);
                isValidComposition = compValid;
                if (!isValidComposition)
                {
                    allValid = false;
                    if (!string.IsNullOrEmpty(compositionReason))
                        reasons.Add(compositionReason);
                    else
                        reasons.Add("Invalid line composition");
                }
            }
            
            // If it's a valid line (2 LI + 1 Officer OR 3+ LI) and passes composition rules, return valid
            if (isValidLine && isValidComposition && !isSkirmisherGroup)
            {
                return (true, "");
            }
            
            // If it's a broken skirmisher group (< 3), check if it can be attached to a line
            // This check happens AFTER line specialized rules to ensure we don't assume skirmishers are attached
            // when they might be a valid skirmisher group themselves
            if (isSkirmisherGroup && !isValidSkirmisher && componentSize < 3)
            {
                // Broken skirmishers can act as attached units - check line composition
                var (canAttach, attachReason) = ClassSettings.ValidateLineCompositionWithReason(composition.ClassCounts);
                if (canAttach)
                {
                    // Can be attached to a line - valid
                    return (true, "");
                }
                else
                {
                    // Cannot be attached - invalid
                    if (!string.IsNullOrEmpty(attachReason))
                        reasons.Add(attachReason);
                    else
                        reasons.Add("Broken skirmishers cannot be attached to this line");
                }
            }
            
            // Final check: Ensure minimum size of 3 for all groups (except broken skirmishers which are handled above)
            if (componentSize < 3 && !(isSkirmisherGroup && !isValidSkirmisher))
            {
                allValid = false;
                if (!reasons.Contains($"Too few players ({componentSize}/3 minimum)"))
                {
                    reasons.Insert(0, $"Too few players ({componentSize}/3 minimum)");
                }
            }
            
            string reason = string.Join(", ", reasons);
            bool finalIsValid = allValid && isValidComposition && (componentSize >= 3 || (isSkirmisherGroup && !isValidSkirmisher && componentSize < 3));
            
            return (finalIsValid, reason);
        }
        
        /// <summary>
        /// Updates line groups - tracks groups, validates them, and creates/updates oval visualizations
        /// </summary>
        private void UpdateLineGroups()
        {
            if (_playerData.Count == 0)
            {
                CleanupAllLineGroups();
                return;
            }
            
            // Build component groups from cached component data
            Dictionary<int, HashSet<int>> componentGroups = new Dictionary<int, HashSet<int>>();
            
            // Group players by component ID (from cached component IDs)
            foreach (var kvp in _cachedComponentIds)
            {
                int playerId = kvp.Key;
                int componentId = kvp.Value;
                
                if (!componentGroups.ContainsKey(componentId))
                    componentGroups[componentId] = new HashSet<int>();
                
                componentGroups[componentId].Add(playerId);
            }
            
            // Update player to component mapping
            _playerToComponentId = new Dictionary<int, int>(_cachedComponentIds);
            
            // Remove groups that no longer exist
            HashSet<int> existingComponents = new HashSet<int>(componentGroups.Keys);
            List<int> toRemove = new List<int>();
            foreach (var kvp in _lineGroups)
            {
                if (!existingComponents.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (int componentId in toRemove)
            {
                RemoveLineGroup(componentId);
            }
            
            // Update or create groups
            foreach (var kvp in componentGroups)
            {
                int componentId = kvp.Key;
                HashSet<int> playerIds = kvp.Value;
                
                if (playerIds.Count < 2)
                    continue; // Skip solo players
                
                // Get composition for this component
                ComponentComposition composition = null;
                int componentSize = 0;
                foreach (int playerId in playerIds)
                {
                    if (_cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp))
                        composition = comp;
                    if (_cachedComponentSizes.TryGetValue(playerId, out int size))
                        componentSize = size;
                    break; // All players in component have same composition
                }
                
                // Validate group
                var (isValid, reason) = ValidateLineGroup(playerIds, composition, componentSize);
                
                
                // Calculate bounding box
                Vector3 center = Vector3.zero;
                Vector3 boundsSize = Vector3.zero;
                int validPlayerCount = 0;
                
                foreach (int playerId in playerIds)
                {
                    if (_playerData.TryGetValue(playerId, out RamboData data) && data.Transform != null)
                    {
                        center += data.Transform.position;
                        validPlayerCount++;
                    }
                }
                
                if (validPlayerCount > 0)
                {
                    center /= validPlayerCount;
                    
                    // Calculate bounds
                    float maxDist = 0f;
                    foreach (int playerId in playerIds)
                    {
                        if (_playerData.TryGetValue(playerId, out RamboData data) && data.Transform != null)
                        {
                            float dist = Vector3.Distance(center, data.Transform.position);
                            if (dist > maxDist)
                                maxDist = dist;
                        }
                    }
                    boundsSize = new Vector3(maxDist * 2f + 2f, 0.1f, maxDist * 2f + 2f); // Add padding
                }
                
                // Update or create group
                if (!_lineGroups.ContainsKey(componentId))
                {
                    _lineGroups[componentId] = new LineGroupData();
                    _lineGroups[componentId].ValidationStatusChangeTime = Time.time; // Initialize timestamp
                }
                
                LineGroupData groupData = _lineGroups[componentId];
                
                // If validation status changed, update the timestamp
                bool wasValid = groupData.IsValid;
                string oldReason = groupData.ValidationReason ?? "";
                if (wasValid != isValid || oldReason != (reason ?? ""))
                {
                    groupData.ValidationStatusChangeTime = Time.time;
                }
                
                groupData.PlayerIds = playerIds;
                groupData.IsValid = isValid;
                groupData.ValidationReason = reason;
                groupData.CenterPosition = center;
                groupData.BoundsSize = boundsSize;
                
                // Only create or update oval for dragoon/hussar groups
                if (GroupContainsCavalry(playerIds, composition))
                {
                    UpdateLineGroupOval(componentId, groupData);
                }
                else
                {
                    // Remove oval if it exists for non-cavalry groups
                    RemoveLineGroupOval(componentId);
                }
            }
        }
        
        /// <summary>
        /// Fast validation update - re-validates existing groups without recalculating components
        /// This runs more frequently than UpdateLineGroups to provide faster feedback when groups split/merge
        /// Only validates dragoon/hussar groups
        /// </summary>
        private void UpdateLineGroupValidation()
        {
            if (_lineGroups.Count == 0)
                return;
            
            // Re-validate all existing groups using cached component data
            foreach (var kvp in _lineGroups)
            {
                int componentId = kvp.Key;
                LineGroupData groupData = kvp.Value;
                
                if (groupData.PlayerIds == null || groupData.PlayerIds.Count < 2)
                    continue;
                
                // Get composition from cache
                ComponentComposition composition = null;
                int componentSize = 0;
                
                foreach (int playerId in groupData.PlayerIds)
                {
                    if (_cachedComponentCompositions.TryGetValue(playerId, out ComponentComposition comp))
                        composition = comp;
                    if (_cachedComponentSizes.TryGetValue(playerId, out int size))
                        componentSize = size;
                    break; // All players in component have same composition
                }
                
                // Skip non-cavalry groups - they don't need validation
                if (!GroupContainsCavalry(groupData.PlayerIds, composition))
                {
                    // Remove oval if it exists for non-cavalry groups
                    RemoveLineGroupOval(componentId);
                    continue;
                }
                
                // Re-validate the group
                var (isValid, reason) = ValidateLineGroup(groupData.PlayerIds, composition, componentSize);
                
                // Always update validation status (ensures immediate updates, no caching delays)
                bool wasValid = groupData.IsValid;
                string oldReason = groupData.ValidationReason ?? "";
                
                // If validation status changed, update the timestamp
                if (wasValid != isValid || oldReason != (reason ?? ""))
                {
                    groupData.ValidationStatusChangeTime = Time.time;
                }
                
                groupData.IsValid = isValid;
                groupData.ValidationReason = reason ?? "";
                
                // Update oval material immediately if validity changed
                if (groupData.OvalRenderer != null && (wasValid != isValid || oldReason != groupData.ValidationReason))
                {
                    Material material = isValid ? _greenMaterial : _redMaterial;
                    if (material != null && groupData.OvalRenderer.material != material)
                    {
                        groupData.OvalRenderer.material = material;
                    }
                }
            }
        }
        
        /// <summary>
        /// Updates line group oval positions (called periodically)
        /// </summary>
        private void UpdateLineGroupPositions()
        {
            foreach (var kvp in _lineGroups)
            {
                LineGroupData groupData = kvp.Value;
                if (groupData.PlayerIds.Count < 2 || groupData.OvalObject == null)
                    continue;
                
                // Recalculate center position
                Vector3 center = Vector3.zero;
                int validPlayerCount = 0;
                
                foreach (int playerId in groupData.PlayerIds)
                {
                    if (_playerData.TryGetValue(playerId, out RamboData data) && data.Transform != null)
                    {
                        center += data.Transform.position;
                        validPlayerCount++;
                    }
                }
                
                if (validPlayerCount > 0)
                {
                    center /= validPlayerCount;
                    groupData.CenterPosition = center;
                    
                    // Update oval position
                    groupData.OvalObject.transform.position = center;
                    
                    // Recalculate bounds and update oval shape
                    float maxDist = 0f;
                    foreach (int playerId in groupData.PlayerIds)
                    {
                        if (_playerData.TryGetValue(playerId, out RamboData data) && data.Transform != null)
                        {
                            float dist = Vector3.Distance(center, data.Transform.position);
                            if (dist > maxDist)
                                maxDist = dist;
                        }
                    }
                    groupData.BoundsSize = new Vector3(maxDist * 2f + 2f, 0.1f, maxDist * 2f + 2f);
                    
                    // Update oval shape
                    float width = groupData.BoundsSize.x / 2f;
                    float height = groupData.BoundsSize.z / 2f;
                    
                    Vector3[] points = new Vector3[64];
                    for (int i = 0; i < 64; i++)
                    {
                        float angle = (float)i / 64f * 2f * Mathf.PI;
                        points[i] = center + new Vector3(
                            Mathf.Cos(angle) * width,
                            0.1f,
                            Mathf.Sin(angle) * height
                        );
                    }
                    
                    if (groupData.OvalRenderer != null)
                        groupData.OvalRenderer.SetPositions(points);
                }
            }
        }
        
        /// <summary>
        /// Creates or updates the oval visualization for a line group
        /// </summary>
        private void UpdateLineGroupOval(int componentId, LineGroupData groupData)
        {
            if (groupData.PlayerIds.Count < 2)
            {
                RemoveLineGroupOval(componentId);
                return;
            }
            
            Material material = groupData.IsValid ? _greenMaterial : _redMaterial;
            if (material == null)
                return;
            
            if (groupData.OvalObject == null)
            {
                GameObject ovalObj = new GameObject($"LineGroupOval_{componentId}");
                ovalObj.layer = 5; // UI layer - excluded from minimap
                ovalObj.transform.position = groupData.CenterPosition;
                
                LineRenderer lr = ovalObj.AddComponent<LineRenderer>();
                lr.loop = true;
                lr.material = material;
                lr.startWidth = 0.2f;
                lr.endWidth = 0.2f;
                lr.positionCount = 64; // Smooth oval
                lr.useWorldSpace = true;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = true;
                
                groupData.OvalObject = ovalObj;
                groupData.OvalRenderer = lr;
            }
            
            // Update material if validity changed
            if (groupData.OvalRenderer.material != material)
            {
                groupData.OvalRenderer.material = material;
            }
            
            // Update oval shape (ellipse around bounding box)
            Vector3 center = groupData.CenterPosition;
            float width = groupData.BoundsSize.x / 2f;
            float height = groupData.BoundsSize.z / 2f;
            
            Vector3[] points = new Vector3[64];
            for (int i = 0; i < 64; i++)
            {
                float angle = (float)i / 64f * 2f * Mathf.PI;
                points[i] = center + new Vector3(
                    Mathf.Cos(angle) * width,
                    0.1f, // Slightly above ground
                    Mathf.Sin(angle) * height
                );
            }
            
            groupData.OvalObject.transform.position = center;
            groupData.OvalRenderer.SetPositions(points);
        }
        
        /// <summary>
        /// Removes the oval visualization for a line group
        /// </summary>
        private void RemoveLineGroupOval(int componentId)
        {
            if (_lineGroups.TryGetValue(componentId, out LineGroupData groupData))
            {
                try
                {
                    if (groupData?.OvalObject != null)
                    {
                        UnityEngine.Object.Destroy(groupData.OvalObject);
                        groupData.OvalObject = null;
                        groupData.OvalRenderer = null;
                    }
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error destroying line group oval: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Removes a line group entirely
        /// </summary>
        private void RemoveLineGroup(int componentId)
        {
            RemoveLineGroupOval(componentId);
            _lineGroups.Remove(componentId);
        }
        
        /// <summary>
        /// Cleans up all line group visualizations
        /// </summary>
        private void CleanupAllLineGroups()
        {
            foreach (var kvp in _lineGroups)
            {
                try
                {
                    RemoveLineGroupOval(kvp.Key);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error cleaning up line group {kvp.Key}: {ex.Message}");
                }
            }
            _lineGroups.Clear();
            _playerToComponentId.Clear();
        }
        
        /// <summary>
        /// Finds all connected components (chains/lines) of players within proximity thresholds
        /// Optimized version using spatial grid to reduce checks
        /// Returns a dictionary mapping playerId -> the size of their connected component
        /// </summary>
        private Dictionary<int, int> FindConnectedComponentsOptimized()
        {
            Dictionary<int, int> componentSizes = new Dictionary<int, int>();
            HashSet<int> visited = new HashSet<int>();
            
            // Build adjacency list using spatial grid for efficiency
            Dictionary<int, HashSet<int>> adjacencyList = new Dictionary<int, HashSet<int>>();
            
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                RamboData data = kvp.Value;
                
                if (data.Transform == null)
                    continue;
                
                adjacencyList[playerId] = new HashSet<int>();
                Vector3 playerPos = data.Transform.position;
                float threshold = GetProximityThresholdForClass(data.ClassName);
                int cellRadius = Mathf.CeilToInt(threshold / GRID_CELL_SIZE) + 1;
                
                // Only check nearby grid cells
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    for (int dz = -cellRadius; dz <= cellRadius; dz++)
                    {
                        Vector3 checkPos = playerPos + new Vector3(dx * GRID_CELL_SIZE, 0, dz * GRID_CELL_SIZE);
                        int gridKey = GetGridKey(checkPos);
                        
                        if (_spatialGrid.TryGetValue(gridKey, out List<int> nearbyPlayers))
                        {
                            foreach (int otherPlayerId in nearbyPlayers)
                            {
                                if (otherPlayerId == playerId)
                                    continue;
                                
                                if (!_playerData.TryGetValue(otherPlayerId, out RamboData otherData))
                                    continue;
                                
                                if (otherData.Transform == null)
                                    continue;
                                
                                // Only connect players of the same faction
                                if (string.IsNullOrEmpty(data.Faction) || string.IsNullOrEmpty(otherData.Faction) ||
                                    !data.Faction.Equals(otherData.Faction, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                
                                // Two-tier proximity check:
                                // 1. Universal rule: Any class within 1 meter is connected
                                // 2. Class-specific rule: If not within 1m, use class-specific thresholds
                                //    (with special cases like Carpenter + Artillery = 10m)
                                float classThreshold = GetEffectiveThreshold(data.ClassName, otherData.ClassName);
                                
                                float distance = Vector3.Distance(playerPos, otherData.Transform.position);
                                
                                // Connect if within 1 meter (universal) OR within class-specific threshold
                                if (distance <= 1.0f || distance <= classThreshold)
                                {
                                    adjacencyList[playerId].Add(otherPlayerId);
                                }
                            }
                        }
                    }
                }
            }
            
            // Now do BFS using the adjacency list (much faster)
            _cachedComponentIds.Clear(); // Clear old component IDs
            
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                if (visited.Contains(playerId))
                    continue;
                
                // Use the first player ID as the component ID
                int componentId = playerId;
                
                // Find all players in this connected component using BFS
                HashSet<int> component = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(playerId);
                visited.Add(playerId);
                component.Add(playerId);
                
                while (queue.Count > 0)
                {
                    int currentId = queue.Dequeue();
                    
                    // Use adjacency list instead of checking all players
                    if (adjacencyList.TryGetValue(currentId, out HashSet<int> neighbors))
                    {
                        foreach (int neighborId in neighbors)
                        {
                            if (!visited.Contains(neighborId))
                            {
                                visited.Add(neighborId);
                                component.Add(neighborId);
                                queue.Enqueue(neighborId);
                            }
                        }
                    }
                }
                
                // Store component size and component ID for all players in this component
                int componentSize = component.Count;
                foreach (int id in component)
                {
                    componentSizes[id] = componentSize;
                    _cachedComponentIds[id] = componentId;
                }
            }
            
            return componentSizes;
        }
        
        /// <summary>
        /// Finds the class composition of each connected component
        /// Returns a dictionary mapping playerId -> ComponentComposition
        /// </summary>
        private Dictionary<int, ComponentComposition> FindConnectedComponentCompositions()
        {
            Dictionary<int, ComponentComposition> compositions = new Dictionary<int, ComponentComposition>();
            HashSet<int> visited = new HashSet<int>();
            
            // Build adjacency list (reuse the same logic as FindConnectedComponentsOptimized)
            Dictionary<int, HashSet<int>> adjacencyList = new Dictionary<int, HashSet<int>>();
            
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                RamboData data = kvp.Value;
                
                if (data.Transform == null)
                    continue;
                
                adjacencyList[playerId] = new HashSet<int>();
                Vector3 playerPos = data.Transform.position;
                float threshold = GetProximityThresholdForClass(data.ClassName);
                int cellRadius = Mathf.CeilToInt(threshold / GRID_CELL_SIZE) + 1;
                
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    for (int dz = -cellRadius; dz <= cellRadius; dz++)
                    {
                        Vector3 checkPos = playerPos + new Vector3(dx * GRID_CELL_SIZE, 0, dz * GRID_CELL_SIZE);
                        int gridKey = GetGridKey(checkPos);
                        
                        if (_spatialGrid.TryGetValue(gridKey, out List<int> nearbyPlayers))
                        {
                            foreach (int otherPlayerId in nearbyPlayers)
                            {
                                if (otherPlayerId == playerId)
                                    continue;
                                
                                if (!_playerData.TryGetValue(otherPlayerId, out RamboData otherData))
                                    continue;
                                
                                if (otherData.Transform == null)
                                    continue;
                                
                                // Only connect players of the same faction
                                if (string.IsNullOrEmpty(data.Faction) || string.IsNullOrEmpty(otherData.Faction) ||
                                    !data.Faction.Equals(otherData.Faction, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                
                                // Two-tier proximity check:
                                // 1. Universal rule: Any class within 1 meter is connected
                                // 2. Class-specific rule: If not within 1m, use class-specific thresholds
                                //    (with special cases like Carpenter + Artillery = 10m)
                                float classThreshold = GetEffectiveThreshold(data.ClassName, otherData.ClassName);
                                
                                float distance = Vector3.Distance(playerPos, otherData.Transform.position);
                                
                                // Connect if within 1 meter (universal) OR within class-specific threshold
                                if (distance <= 1.0f || distance <= classThreshold)
                                {
                                    adjacencyList[playerId].Add(otherPlayerId);
                                }
                            }
                        }
                    }
                }
            }
            
            // Find all connected components and their compositions using BFS
            foreach (var kvp in _playerData)
            {
                int playerId = kvp.Key;
                if (visited.Contains(playerId))
                    continue;
                
                // Find all players in this connected component using BFS
                HashSet<int> component = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(playerId);
                visited.Add(playerId);
                component.Add(playerId);
                
                while (queue.Count > 0)
                {
                    int currentId = queue.Dequeue();
                    
                    if (adjacencyList.TryGetValue(currentId, out HashSet<int> neighbors))
                    {
                        foreach (int neighborId in neighbors)
                        {
                            if (!visited.Contains(neighborId))
                            {
                                visited.Add(neighborId);
                                component.Add(neighborId);
                                queue.Enqueue(neighborId);
                            }
                        }
                    }
                }
                
                // Build composition for this component
                ComponentComposition composition = new ComponentComposition();
                composition.TotalCount = component.Count;
                
                foreach (int id in component)
                {
                    if (_playerData.TryGetValue(id, out RamboData data) && !string.IsNullOrEmpty(data.ClassName))
                    {
                        string classKey = data.ClassName.ToLower();
                        int currentCount = composition.ClassCounts.TryGetValue(classKey, out int count) ? count : 0;
                        composition.ClassCounts[classKey] = currentCount + 1;
                    }
                }
                
                // Store composition for all players in this component
                foreach (int id in component)
                {
                    compositions[id] = composition;
                }
            }
            
            return compositions;
        }

        private int GetGridKey(Vector3 position)
        {
            // Convert world position to grid cell key
            int x = Mathf.FloorToInt(position.x / GRID_CELL_SIZE);
            int z = Mathf.FloorToInt(position.z / GRID_CELL_SIZE);
            // Use hash code for grid key
            return (x * 73856093) ^ (z * 19349663); // Prime numbers for hashing
        }

        private void CreateMaterial()
        {
            if (_redMaterial != null && _greenMaterial != null && _connectionMaterial != null)
                return;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Diffuse");
            
            if (shader != null)
            {
                if (_redMaterial == null)
                {
                    _redMaterial = new Material(shader);
                    _redMaterial.color = Color.red;
                    _redMaterial.SetFloat("_Mode", 0);
                }
                
                if (_greenMaterial == null)
                {
                    _greenMaterial = new Material(shader);
                    _greenMaterial.color = new Color(0f, 1f, 0f, 0.8f); // Green for legal lines
                    _greenMaterial.SetFloat("_Mode", 0);
                }
                
                if (_connectionMaterial == null)
                {
                    _connectionMaterial = new Material(shader);
                    _connectionMaterial.color = new Color(0f, 1f, 0f, 0.6f); // Semi-transparent green
                    _connectionMaterial.SetFloat("_Mode", 0);
                }
            }
        }

        private void CreateRamboIndicator(Transform playerTransform, int playerId, RamboData data)
        {
            if (data.CircleObject != null || _redMaterial == null)
                return;

            Vector3 playerPos = playerTransform.position;
            
            GameObject circleObj = new GameObject($"RamboIndicator_{playerId}");
            circleObj.layer = 5; // UI layer - excluded from minimap
            circleObj.transform.position = new Vector3(playerPos.x, playerPos.y + 0.2f, playerPos.z);
            circleObj.transform.rotation = Quaternion.identity;

            LineRenderer lr = circleObj.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.positionCount = CIRCLE_SEGMENTS;
            lr.useWorldSpace = true;
            lr.material = _redMaterial;
            lr.startWidth = LINE_WIDTH;
            lr.endWidth = LINE_WIDTH;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = true;

            Vector3[] points = new Vector3[CIRCLE_SEGMENTS];
            Vector3 center = circleObj.transform.position;

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)i / CIRCLE_SEGMENTS * 2f * Mathf.PI;
                points[i] = center + new Vector3(
                    Mathf.Cos(angle) * CIRCLE_RADIUS,
                    0f,
                    Mathf.Sin(angle) * CIRCLE_RADIUS
                );
            }

            lr.SetPositions(points);

            data.CircleObject = circleObj;
            data.CircleRenderer = lr;
        }

        private void RemoveRamboIndicator(int playerId, RamboData data)
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
                data.IsRambo = false;
                data.TimeAlone = 0f;
            }
        }
        
        /// <summary>
        /// Remove a player's rambo indicator by Holdfast playerId
        /// </summary>
        private void RemovePlayerByPlayerId(int holdfastPlayerId)
        {
            int instanceId = -1;
            
            // Try to get instanceId from our mapping
            if (_playerIdToGameObject.TryGetValue(holdfastPlayerId, out GameObject playerObject))
            {
                if (playerObject != null)
                {
                    try
                    {
                        instanceId = playerObject.GetInstanceID();
                    }
                    catch
                    {
                        // GameObject was destroyed
                        AdvancedAdminUIMod.Log.LogInfo($"[{FeatureName}] GameObject for player {holdfastPlayerId} was already destroyed");
                    }
                }
                _playerIdToGameObject.Remove(holdfastPlayerId);
            }
            
            // If we couldn't get instanceId from mapping, search through _playerData
            // This handles cases where the GameObject was destroyed before we could get the ID
            if (instanceId < 0)
            {
                // No valid mapping for this player - nothing to clean up
            }
            
            // If we found a valid instanceId, remove it
            RamboData data = null;
            if (instanceId >= 0)
            {
                if (_playerData.TryGetValue(instanceId, out data))
                {
                    RemoveRamboIndicator(instanceId, data);
                    _playerData.Remove(instanceId);
                }
                
                // Remove from component mapping
                _playerToComponentId.Remove(instanceId);
                
                // Remove from cached component data
                _cachedComponentSizes.Remove(instanceId);
                _cachedComponentCompositions.Remove(instanceId);
                
                // Clean up connections involving this player
                List<string> connectionsToRemove = new List<string>();
                foreach (var kvp in _connections)
                {
                    string[] parts = kvp.Key.Split('_');
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[0], out int id1) && int.TryParse(parts[1], out int id2))
                        {
                            if (id1 == instanceId || id2 == instanceId)
                            {
                                connectionsToRemove.Add(kvp.Key);
                            }
                        }
                    }
                }
                
                foreach (string key in connectionsToRemove)
                {
                    if (_connections.TryGetValue(key, out GameObject connectionObj))
                    {
                        if (connectionObj != null)
                            UnityEngine.Object.Destroy(connectionObj);
                        _connections.Remove(key);
                    }
                }
                
                // Remove from spatial grid
                Vector3 playerPos = data?.Transform?.position ?? Vector3.zero;
                if (playerPos != Vector3.zero)
                {
                    int gridKey = GetGridKey(playerPos);
                    if (_spatialGrid.TryGetValue(gridKey, out List<int> gridPlayers))
                    {
                        gridPlayers.Remove(instanceId);
                        if (gridPlayers.Count == 0)
                            _spatialGrid.Remove(gridKey);
                    }
                }
                
                // Remove player from any line groups they're in
                List<int> lineGroupsToRemove = new List<int>();
                foreach (var kvp in _lineGroups)
                {
                    if (kvp.Value.PlayerIds != null && kvp.Value.PlayerIds.Contains(instanceId))
                    {
                        kvp.Value.PlayerIds.Remove(instanceId);
                        if (kvp.Value.PlayerIds.Count < 2)
                            lineGroupsToRemove.Add(kvp.Key);
                    }
                }
                
                // Remove line groups that now have < 2 players
                foreach (int componentId in lineGroupsToRemove)
                    RemoveLineGroup(componentId);
            }
            
            // Force immediate recalculation of line groups on next update
            _lastComponentUpdateTime = 0f;
        }

        // Group display methods moved to AdminWindow - no longer needed here
        
        private void CleanupAllIndicators()
        {
            foreach (var kvp in _playerData)
            {
                try
                {
                    RemoveRamboIndicator(kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error cleaning up indicator for player {kvp.Key}: {ex.Message}");
                }
            }
            _playerData.Clear();
            _spatialGrid.Clear();
            _cavalryCountsByFaction.Clear();
            _hussarPlayersByFaction.Clear();
            _dragoonPlayersByFaction.Clear();
            _skirmisherCountsByFaction.Clear();
            _riflemanPlayersByFaction.Clear();
            _lightInfantryPlayersByFaction.Clear();
            _surgeonProximity.Clear();
            CleanupAllConnections();
            CleanupAllLineGroups();
        }
        
        private void CleanupAllConnections()
        {
            foreach (var kvp in _connections)
            {
                try
                {
                    if (kvp.Value != null)
                        UnityEngine.Object.Destroy(kvp.Value);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[{FeatureName}] Error destroying connection {kvp.Key}: {ex.Message}");
                }
            }
            _connections.Clear();
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
                    
                    // Clean up orphaned connection lines
                    if (objName.StartsWith("Connection_"))
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                        cleanedCount++;
                    }
                    // Clean up orphaned rambo indicators (circles)
                    else if (objName.StartsWith("RamboIndicator_"))
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                        cleanedCount++;
                    }
                    // Clean up orphaned line group ovals
                    else if (objName.StartsWith("LineGroupOval_"))
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
