using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

using HoldfastSharedMethods;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// Persistent player tracking system - always tracks all players regardless of feature state
    /// </summary>
    public static class PlayerTracker
    {
        public class PlayerData
        {
            public int PlayerId { get; set; }
            public string PlayerName { get; set; }
            public string RegimentTag { get; set; }
            public ulong SteamId { get; set; }
            public bool IsBot { get; set; }
            public GameObject PlayerObject { get; set; }
            public Transform PlayerTransform { get; set; }
            public string ClassName { get; set; }
            public string FactionName { get; set; }
            public int SpawnSectionId { get; set; }
            public int UniformId { get; set; }
            public Vector3 LastKnownPosition { get; set; }
            public float LastKnownYaw { get; set; }
            public float LastUpdateTime { get; set; }
            public bool IsSpawned { get; set; }
        }

        private static readonly ConcurrentDictionary<int, PlayerData> _players = new();
        private static int _initialized = 0; // Use Interlocked for thread-safe initialization
        private static UnityEngine.Coroutine _scanCoroutine = null;

        public static void Initialize()
        {
            // Thread-safe initialization check
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
                return;

            try
            {
                // Subscribe to OnPlayerJoined to start tracking player
                PlayerEventManager._onPlayerJoinedCallbacks.Add(OnPlayerJoined);
                
                // Subscribe to OnPlayerSpawned via PlayerEventManager with extended data (receives events from IHoldfastSharedMethods)
                PlayerEventManager._onPlayerSpawnedExtendedCallbacks.Add(OnPlayerSpawnedExtended);
                
                // Subscribe to OnPlayerKilledPlayer to remove dead players
                PlayerEventManager._onPlayerKilledPlayerCallbacks.Add(OnPlayerKilledPlayer);
                
                // Subscribe to OnPlayerLeft to stop tracking player
                PlayerEventManager._onPlayerLeftCallbacks.Add(OnPlayerLeft);
                
                // Subscribe to OnRoundDetails to clear player data on new round (keep IDs only)
                PlayerEventManager._onRoundDetailsCallbacks.Add(OnRoundDetails);
                PlayerEventManager._onRoundEndFactionWinnerCallbacks.Add(OnRoundEnd);
                PlayerEventManager._onRoundEndPlayerWinnerCallbacks.Add(OnRoundEndPlayer);
                
                // Subscribe to client connection changes to clear all data on disconnect
                PlayerEventManager._onClientConnectionChangedCallbacks.Add(OnClientConnectionChanged);
                
                // Scan for existing players that spawned before mod initialization
                ScanForExistingPlayers();
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[PlayerTracker] Error during initialization: {ex.Message}\n{ex.StackTrace}");
                Interlocked.Exchange(ref _initialized, 0); // Reset on failure
            }
        }
        
        /// <summary>
        /// Called when client connects to or disconnects from a server
        /// </summary>
        private static void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Disconnected - clear all player data
                int previousCount = _players.Count;
                AdvancedAdminUIMod.Log.LogInfo($"[PlayerTracker] Disconnected from server - clearing {previousCount} players");
                _players.Clear();
                
                // Trigger global cleanup
                TriggerGlobalCleanup();
            }
            // On connect, we wait for OnRoundDetails/OnPlayerJoined to repopulate
        }
        
        private static void ScanForExistingPlayers()
        {
            // Stop previous coroutine if it exists
            if (_scanCoroutine != null)
            {
                try
                {
                    var runner = AdvancedAdminUIMod.Runner;
                    runner?.StopCoroutine(_scanCoroutine);
                }
                catch { }
                _scanCoroutine = null;
            }
            
            // Wait a bit for the game to fully load, then scan
            // Use Runner instead of Instance - the plugin component may be disabled by BepInEx
            var runnerObj = AdvancedAdminUIMod.Runner;
            if (runnerObj != null)
                _scanCoroutine = runnerObj.StartCoroutine(ScanForExistingPlayersDelayed());
            else
                AdvancedAdminUIMod.Log?.LogWarning("[PlayerTracker] Cannot scan - Runner is null");
        }
        
        /// <summary>
        /// Public method to trigger a rescan for existing players
        /// Call this when joining a server late or after hot reload
        /// </summary>
        public static void RescanForExistingPlayers()
        {
            if (HoldfastScriptMod.IsRCLoggedIn())
                AdvancedAdminUIMod.Log.LogInfo("[PlayerTracker] Triggering rescan for existing players...");
            ScanForExistingPlayers();
        }
        
        private static System.Collections.IEnumerator ScanForExistingPlayersDelayed()
        {
            // Wait for game to fully initialize
            yield return new WaitForSeconds(2.0f);
            
            if (HoldfastScriptMod.IsRCLoggedIn())
                AdvancedAdminUIMod.Log.LogInfo("[PlayerTracker] Scanning for existing players...");
            
            // Find all CharacterController objects (players)
            CharacterController[] controllers = UnityEngine.Object.FindObjectsOfType<CharacterController>();
            int foundCount = 0;
            
            foreach (CharacterController controller in controllers)
            {
                if (controller == null || controller.gameObject == null)
                    continue;
                
                GameObject playerObj = controller.gameObject;
                Transform playerTransform = playerObj.transform;
                
                // Skip if not a root-level object (likely a child/component)
                if (playerTransform.parent != null && playerTransform.parent != playerTransform.root)
                    continue;
                
                // Check if this looks like a player GameObject
                string objName = playerObj.name.ToLower();
                if (!objName.Contains("player") && !objName.Contains("proxy"))
                    continue;
                
                // Try to extract player ID from GameObject name (e.g., "Player - Proxy (#123)" -> 123)
                int playerId = ExtractPlayerIdFromName(playerObj.name);
                
                // If we couldn't extract ID, use instance ID as fallback
                if (playerId <= 0)
                {
                    playerId = playerObj.GetInstanceID();
                }
                
                // Skip if already tracked
                if (_players.ContainsKey(playerId))
                    continue;
                
                // Try to get class/faction from GameObject or components
                TryExtractPlayerInfo(playerObj, out string className, out string factionName);
                
                _players.TryAdd(playerId, new PlayerData
                {
                    PlayerId = playerId,
                    PlayerObject = playerObj,
                    PlayerTransform = playerTransform,
                    ClassName = className,
                    FactionName = factionName,
                    SpawnSectionId = 0,
                    UniformId = 0,
                    LastKnownPosition = playerTransform.position,
                    LastUpdateTime = Time.time,
                    IsSpawned = true
                });
                
                foundCount++;
            }
            
            if (HoldfastScriptMod.IsRCLoggedIn())
                AdvancedAdminUIMod.Log.LogInfo($"[PlayerTracker] Found {foundCount} existing player(s) in scene");
        }
        
        private static int ExtractPlayerIdFromName(string objName)
        {
            // Try to extract ID from name like "Player - Proxy (#123)"
            int hashIndex = objName.IndexOf("(#");
            if (hashIndex >= 0)
            {
                int endIndex = objName.IndexOf(")", hashIndex);
                if (endIndex > hashIndex)
                {
                    string idStr = objName.Substring(hashIndex + 2, endIndex - hashIndex - 2);
                    if (int.TryParse(idStr, out int parsedId))
                    {
                        return parsedId;
                    }
                }
            }
            return -1;
        }
        
        private static void TryExtractPlayerInfo(GameObject playerObj, out string className, out string factionName)
        {
            className = "Unknown";
            factionName = "Unknown";
            
            // Try to get info from components using reflection
            try
            {
                var components = playerObj.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                        continue;
                    
                    Type compType = component.GetType();
                    string typeName = compType.Name;
                    
                    // Look for class-related components
                    if (typeName.Contains("Player") || typeName.Contains("Actor") || typeName.Contains("Character"))
                    {
                        // Try to get class/faction from properties
                        var props = compType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        foreach (var prop in props)
                        {
                            if (prop.Name.Contains("Class") || prop.Name.Contains("PlayerClass"))
                            {
                                try
                                {
                                    object value = prop.GetValue(component);
                                    if (value != null)
                                        className = value.ToString();
                                }
                                catch (Exception ex)
                                {
                                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error reading class property: {ex.Message}");
                                }
                            }
                            else if (prop.Name.Contains("Faction") || prop.Name.Contains("Country"))
                            {
                                try
                                {
                                    object value = prop.GetValue(component);
                                    if (value != null)
                                        factionName = value.ToString();
                                }
                                catch (Exception ex)
                                {
                                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error reading faction property: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error extracting player info: {ex.Message}");
            }
        }

        /// <summary>
        /// Extended callback with full spawn data from IHoldfastSharedMethods
        /// </summary>
        private static void OnPlayerSpawnedExtended(int playerId, int spawnSectionId, object playerFaction, object playerClass, int uniformId, GameObject playerObject)
        {
            try
            {
                string className = ExtractEnumName(playerClass);
                string factionName = ExtractEnumName(playerFaction);
                if (!_players.TryGetValue(playerId, out PlayerData data))
                {
                    data = new PlayerData { PlayerId = playerId };
                    _players[playerId] = data;
                }

                data.ClassName = className;
                data.FactionName = factionName;
                data.SpawnSectionId = spawnSectionId;
                data.UniformId = uniformId;
                data.LastUpdateTime = Time.time;

                bool hasBody = playerObject != null;
                data.IsSpawned = true;
                data.PlayerObject = hasBody ? playerObject : null;
                data.PlayerTransform = hasBody ? playerObject.transform : null;
                data.LastKnownPosition = hasBody ? playerObject.transform.position : Vector3.zero;
                if (!hasBody)
                    data.LastKnownYaw = 0f;
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error tracking player {playerId}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a player joins - start tracking them with name/regiment (GameObject will be added on spawn)
        /// </summary>
        private static void OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            // Create or update entry with player identity info
            // GameObject and other data will be added when OnPlayerSpawned is called
            if (_players.TryGetValue(playerId, out PlayerData existingData))
            {
                // Update existing entry with identity info (in case spawn happened before join event)
                existingData.PlayerName = name;
                existingData.RegimentTag = regimentTag;
                existingData.SteamId = steamId;
                existingData.IsBot = isBot;
            }
            else
            {
                _players[playerId] = new PlayerData
                {
                    PlayerId = playerId,
                    PlayerName = name,
                    RegimentTag = regimentTag,
                    SteamId = steamId,
                    IsBot = isBot,
                    PlayerObject = null,
                    PlayerTransform = null,
                    ClassName = null,
                    FactionName = null,
                    SpawnSectionId = 0,
                    UniformId = 0,
                    LastKnownPosition = Vector3.zero,
                    LastUpdateTime = Time.time
                };
            }
            
            // Don't log - too spammy with 200+ players
        }
        
        /// <summary>
        /// Called when a player is killed - mark them as dead but keep tracking (don't remove from PlayerTracker)
        /// Features will handle removing them from their tracking, but we keep the player ID
        /// </summary>
        private static void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, HoldfastSharedMethods.EntityHealthChangedReason reason, string details)
        {
            // Don't remove from PlayerTracker - keep the player ID tracked
            // Features will handle cleanup of their own tracking
            if (_players.TryGetValue(victimPlayerId, out PlayerData playerData))
            {
                ClearSpawn(playerData);
            }
        }
        
        /// <summary>
        /// Called when a player leaves - stop tracking them
        /// </summary>
        private static void OnPlayerLeft(int playerId)
        {
            if (_players.TryRemove(playerId, out _))
            {
                if (HoldfastScriptMod.IsRCLoggedIn())
                    AdvancedAdminUIMod.Log.LogInfo($"[PlayerTracker] Player left: Id={playerId}");
            }
        }
        
        /// <summary>
        /// Called when a new round starts - clear ALL player data including IDs
        /// Players may have left during round rotation without OnPlayerLeft being called
        /// OnPlayerJoined and OnPlayerSpawned will rebuild the player list from scratch
        /// </summary>
        static void OnRoundEnd(FactionCountry faction, FactionRoundWinnerReason reason)
        {
            EndRoundSpawns();
        }

        static void OnRoundEndPlayer(int playerId)
        {
            EndRoundSpawns();
        }

        static void EndRoundSpawns()
        {
            foreach (PlayerData data in _players.Values)
                ClearSpawn(data);
            _pendingGameObjectSearches.Clear();
        }

        static void ClearSpawn(PlayerData data)
        {
            if (data == null)
                return;
            data.IsSpawned = false;
            data.PlayerObject = null;
            data.PlayerTransform = null;
            data.LastKnownPosition = Vector3.zero;
            data.LastKnownYaw = 0f;
            data.FactionName = null;
            data.ClassName = null;
        }

        private static void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            if (roundId < 0 || string.IsNullOrEmpty(mapName))
                return;

            int previousCount = _players.Count;
            if (HoldfastScriptMod.IsRCLoggedIn())
                AdvancedAdminUIMod.Log.LogInfo($"[PlayerTracker] New round detected. Clearing spawns for {previousCount} players");

            EndRoundSpawns();
            
            // Also trigger global cleanup to remove any stale visual GameObjects
            TriggerGlobalCleanup();
            
            // After a delay, scan for existing players (in case we joined late and events already fired)
            // Use Runner instead of Instance - the plugin component may be disabled by BepInEx
            var runner = AdvancedAdminUIMod.Runner;
            if (runner != null)
                runner.StartCoroutine(ScanAfterRoundStart());
            else
                AdvancedAdminUIMod.Log.LogWarning("[PlayerTracker] Cannot start ScanAfterRoundStart - Runner is null");
        }
        
        private static System.Collections.IEnumerator ScanAfterRoundStart()
        {
            // Wait for players to spawn and events to fire first
            yield return new WaitForSeconds(5.0f);
            
            // Only scan if we have just ourselves tracked (might have joined late)
            int currentCount = _players.Count;
            if (currentCount <= 1)
            {
                if (HoldfastScriptMod.IsRCLoggedIn())
                    AdvancedAdminUIMod.Log.LogInfo($"[PlayerTracker] Only {currentCount} player(s) tracked - scanning for existing players...");
                ScanForExistingPlayers();
            }
        }
        
        /// <summary>
        /// Extract enum name from object (handles FactionCountry, PlayerClass, etc.)
        /// </summary>
        private static string ExtractEnumName(object enumValue)
        {
            if (enumValue == null)
                return "Unknown";
            
            // If it's already a string, return it
            if (enumValue is string str)
                return str;
            
            // Try to get the enum value name
            try
            {
                // Use reflection to get the enum value name
                Type enumType = enumValue.GetType();
                if (enumType.IsEnum)
                {
                    return Enum.GetName(enumType, enumValue) ?? enumValue.ToString();
                }
                
                // Try ToString() as fallback
                return enumValue.ToString();
            }
            catch
            {
                return enumValue.ToString();
            }
        }


        private static readonly List<int> _removeIds = new(8);
        private static int _lastPlayerCount = 0;
        private static float _lastCleanupCheck = 0f;
        private const float CLEANUP_CHECK_INTERVAL = 1.0f; // Check every second
        
        private static float _lastPositionUpdateTime = 0f;
        private const float POSITION_UPDATE_INTERVAL = 0.1f; // Update positions every 0.1 seconds (10 times per second)
        
        public static void Update()
        {
            // The local player is sampled every frame. Everyone else stays on the slow interval.
            UpdateLocalPose();

            // PERFORMANCE: Only update positions periodically, not every frame
            // With 300 players, updating every frame causes lag
            if (Time.time - _lastPositionUpdateTime < POSITION_UPDATE_INTERVAL)
                return;
            
            _lastPositionUpdateTime = Time.time;
            
            // Update positions of tracked players
            _removeIds.Clear();
            
            foreach (var kvp in _players)
            {
                try
                {
                    PlayerData data = kvp.Value;
                    // Only update position if player has a GameObject (is alive and spawned)
                    // Dead players (null GameObject) are kept tracked until OnPlayerLeft
                    if (data == null || !data.IsSpawned || data.PlayerObject == null || data.PlayerTransform == null)
                    {
                        // Dead or between rounds. The next spawn fills the body again.
                        continue;
                    }

                    data.LastKnownPosition = data.PlayerTransform.position;
                    data.LastUpdateTime = Time.time;
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error updating player {kvp.Key}: {ex.Message}");
                    // Only remove on error if GameObject is also null (truly invalid entry)
                    if (kvp.Value?.PlayerObject == null)
                    {
                        _removeIds.Add(kvp.Key);
                    }
                }
            }

            // Remove invalid entries
            foreach (int id in _removeIds)
            {
                _players.TryRemove(id, out _);
            }
            
            // PERFORMANCE: Process pending GameObject searches (batched, throttled)
            ProcessPendingGameObjectSearches();
            
            // Check if we suddenly lost all players (map change/rotation)
            if (Time.time - _lastCleanupCheck >= CLEANUP_CHECK_INTERVAL)
            {
                _lastCleanupCheck = Time.time;
                int currentPlayerCount = _players.Count;
                
                // If we had players before and now we have none, trigger cleanup
                if (_lastPlayerCount > 0 && currentPlayerCount == 0)
                {
                    if (HoldfastScriptMod.IsRCLoggedIn())
                        AdvancedAdminUIMod.Log.LogInfo($"[PlayerTracker] All players lost - triggering cleanup");
                    TriggerGlobalCleanup();
                }
                
                _lastPlayerCount = currentPlayerCount;
            }
        }

        static void UpdateLocalPose()
        {
            int localId = GameEventBridge.GetLocalPlayerId();
            if (localId < 0)
                return;
            if (!_players.TryGetValue(localId, out PlayerData data))
                return;
            if (data == null || !data.IsSpawned || data.PlayerTransform == null)
                return;

            try
            {
                data.LastKnownPosition = data.PlayerTransform.position;
                data.LastUpdateTime = Time.time;
                float yaw = data.PlayerTransform.eulerAngles.y;
                Camera view = Camera.main;
                if (view != null)
                    yaw = view.transform.eulerAngles.y;
                data.LastKnownYaw = yaw;
            }
            catch
            {
                // The player object was destroyed. The next spawn fills the transform again.
            }
        }
        
        /// <summary>
        /// Trigger cleanup of all visual GameObjects across all features
        /// Called when players are lost (map change, server disconnect, etc.)
        /// </summary>
        public static void TriggerGlobalCleanup()
        {
            // Notify the main mod class to clean up all features
            AdvancedAdminUI.AdvancedAdminUIMod.RequestGlobalCleanup();
        }

        public static Dictionary<int, PlayerData> GetAllPlayers()
        {
            // PERFORMANCE: Return a snapshot copy only when needed
            // For read-only iteration, use GetPlayersReadOnly() instead
            return new Dictionary<int, PlayerData>(_players);
        }
        
        /// <summary>
        /// Get read-only access to players without creating a copy - MUCH faster for iteration
        /// WARNING: Do not modify the returned collection!
        /// </summary>
        public static IReadOnlyDictionary<int, PlayerData> GetPlayersReadOnly()
        {
            return _players;
        }

        public static PlayerData GetPlayer(int playerId)
        {
            _players.TryGetValue(playerId, out PlayerData data);
            return data;
        }

        public static List<PlayerData> GetPlayersByClass(string className)
        {
            List<PlayerData> result = [];
            foreach (var kvp in _players)
            {
                try
                {
                    if (kvp.Value?.ClassName != null && 
                        kvp.Value.ClassName.IndexOf(className, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(kvp.Value);
                    }
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error filtering player {kvp.Key}: {ex.Message}");
                }
            }
            return result;
        }

        public static int GetPlayerCount()
        {
            return _players.Count;
        }
        
        // PERFORMANCE: Throttle expensive GameObject searches
        private static float _lastGameObjectSearchTime = 0f;
        private static readonly float _gameObjectSearchCooldown = 2.0f; // Only search every 2 seconds max
        private static readonly HashSet<int> _pendingGameObjectSearches = [];
        
        /// <summary>
        /// Update player position from OnPlayerPacket (IHoldfastSharedMethods3)
        /// This is the most reliable way to track player positions, especially for late joiners
        /// If player doesn't exist in our tracker, we auto-create an entry
        /// </summary>
        public static void UpdatePlayerPosition(int playerId, Vector3 position, Vector3 rotation)
        {
            if (_players.TryGetValue(playerId, out PlayerData existingData))
            {
                if (!existingData.IsSpawned)
                    return;

                existingData.LastKnownPosition = position;
                existingData.LastKnownYaw = rotation.y;
                existingData.LastUpdateTime = Time.time;
                
                // Queue for GameObject search if we don't have it (don't search immediately)
                if (existingData.PlayerObject == null)
                    _pendingGameObjectSearches.Add(playerId);
            }
            else
            {
                // NEW PLAYER - auto-create entry from packet data
                var newData = new PlayerData
                {
                    PlayerId = playerId,
                    PlayerName = null,
                    RegimentTag = null,
                    SteamId = 0,
                    IsBot = false,
                    PlayerObject = null,
                    PlayerTransform = null,
                    ClassName = null,
                    FactionName = null,
                    SpawnSectionId = 0,
                    UniformId = 0,
                    LastKnownPosition = Vector3.zero,
                    LastKnownYaw = 0f,
                    LastUpdateTime = Time.time,
                    IsSpawned = false
                };
                
                _players.TryAdd(playerId, newData);
            }
        }
        
        /// <summary>
        /// Process pending GameObject searches (called from Update, throttled)
        /// PERFORMANCE: Only do expensive FindObjectsOfType once per cooldown period
        /// </summary>
        private static void ProcessPendingGameObjectSearches()
        {
            if (_pendingGameObjectSearches.Count == 0)
                return;
            
            if (Time.time - _lastGameObjectSearchTime < _gameObjectSearchCooldown)
                return;
            
            _lastGameObjectSearchTime = Time.time;
            
            // Only search if we have pending searches
            if (_pendingGameObjectSearches.Count == 0)
                return;
            
            try
            {
                // Character controllers are the player bodies. Scanning every GameObject hitches the frame.
                CharacterController[] bodies = UnityEngine.Object.FindObjectsOfType<CharacterController>();
                
                // Build a quick lookup of player IDs we're searching for
                var searchingFor = new HashSet<int>(_pendingGameObjectSearches);
                _pendingGameObjectSearches.Clear();
                
                foreach (var body in bodies)
                {
                    if (body == null)
                        continue;
                    GameObject obj = body.gameObject;
                    if (obj == null || obj.name == null)
                        continue;
                    
                    // Check if name contains player pattern
                    if (!obj.name.Contains("(#"))
                        continue;
                    
                    // Extract player ID from name
                    int hashIndex = obj.name.IndexOf("(#");
                    int endIndex = obj.name.IndexOf(")", hashIndex);
                    if (hashIndex < 0 || endIndex < 0)
                        continue;
                    
                    string idStr = obj.name.Substring(hashIndex + 2, endIndex - hashIndex - 2);
                    if (!int.TryParse(idStr, out int playerId))
                        continue;
                    
                    // Check if this is a player we're looking for
                    if (searchingFor.Contains(playerId) && _players.TryGetValue(playerId, out PlayerData data))
                    {
                        if (data.IsSpawned && data.PlayerObject == null)
                        {
                            data.PlayerObject = obj;
                            data.PlayerTransform = obj.transform;
                        }
                        searchingFor.Remove(playerId);
                    }
                    
                    // Stop if we found all pending players
                    if (searchingFor.Count == 0)
                        break;
                }
                
                if (searchingFor.Count > 0)
                {
                    // Re-queue only players that still exist in _players (prevents stale IDs from accumulating forever)
                    foreach (int id in searchingFor)
                    {
                        if (_players.ContainsKey(id))
                            _pendingGameObjectSearches.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[PlayerTracker] Error in batch GameObject search: {ex.Message}");
            }
        }
    }
}

