using System;
using System.Linq;
using System.Reflection;

using UnityEngine;
using HoldfastSharedMethods;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// IHoldfastSharedMethods3 interface - defined here since it may not be in our referenced DLLs
    /// The game calls these methods via reflection
    /// </summary>
    public interface IHoldfastSharedMethods3
    {
        void OnPlayerPacket(int playerId, Vector3 position, Vector3 rotation);
        void OnStartSpectate(int playerId, int spectatedPlayerId);
        void OnStopSpectate(int playerId, int spectatedPlayerId);
        void OnStartFreeflight(int playerId);
        void OnStopFreeflight(int playerId);
        void OnMeleeArenaRoundEndFactionWinner(int roundId, bool attackers);
        void OnPlayerConnected(int playerId, bool isAutoAdmin, string backendId);
        void OnPlayerDisconnected(int playerId);
    }
    
    /// <summary>
    /// Implements IHoldfastSharedMethods and IHoldfastSharedMethods3 interfaces to receive events directly from Holdfast
    /// This is the standard way Holdfast mods receive events - no patching needed!
    /// IHoldfastSharedMethods3 gives us OnPlayerPacket for real-time player position updates
    /// </summary>
    public class HoldfastScriptMod : IHoldfastSharedMethods, IHoldfastSharedMethods3
    {
        private static HoldfastScriptMod _instance;
        private static bool _isClientConnected = false; // Track if client is connected to server
        private static object _lastKnownInstancesList = null; // Track the instances list
        
        // RC Login tracking - only allow mod features for logged-in admins
        private static bool _isRCLoggedIn = false;
        private static ulong _localSteamId = 0;
        private static int _localPlayerId = -1;
        
        // Master login bypass (set via launcher)
        private static bool _masterLoginChecked = false;
        private static bool _hasMasterLogin = false;
        private const string MASTER_TOKEN_FILE = "master_login.token";
        
        /// <summary>
        /// Force re-registration (call after map change/new round)
        /// Removes old instance from list first to prevent duplicates!
        /// </summary>
        public static void ForceReRegister()
        {
            AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] Forcing re-registration...");
            
            // CRITICAL: Remove old instance from the game's list first to prevent duplicates
            if (_instance != null && _lastKnownInstancesList != null)
            {
                try
                {
                    Type listType = _lastKnownInstancesList.GetType();
                    MethodInfo removeMethod = listType.GetMethod("Remove");
                    if (removeMethod != null)
                    {
                        bool removed = (bool)removeMethod.Invoke(_lastKnownInstancesList, new object[] { _instance });
                        AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Removed old instance from list: {removed}");
                    }
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[HoldfastScriptMod] Error removing old instance: {ex.Message}");
                }
            }
            
            _instance = null;
            _lastKnownInstancesList = null;
        }
        
        /// <summary>
        /// Check if we're still registered in the instances list
        /// </summary>
        public static bool IsStillRegistered()
        {
            if (_instance == null || _lastKnownInstancesList == null)
                return false;
            
            try
            {
                // Check if our instance is still in the list
                Type listType = _lastKnownInstancesList.GetType();
                MethodInfo containsMethod = listType.GetMethod("Contains");
                if (containsMethod != null)
                {
                    bool contains = (bool)containsMethod.Invoke(_lastKnownInstancesList, new object[] { _instance });
                    return contains;
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[HoldfastScriptMod] Error checking registration: {ex.Message}");
            }
            return false;
        }

        public static bool Register()
        {
            // If we're already successfully registered, just verify and return
            if (_instance != null && IsStillRegistered())
            {
                return true;
            }
            
            // Reset state for fresh registration attempt
            if (_instance != null)
            {
                AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] Instance exists but not in list, will re-register...");
            }
            _instance = null;
            _lastKnownInstancesList = null;

            try
            {
                // Use the simple approach as suggested by eLF:
                // 1. Find ServerModLoaderManager using FindObjectOfType
                // 2. Get holdfastSharedMethodInstances (field or property)
                // 3. Call Add on it with our instance
                
                Assembly assemblyCSharp = Assembly.Load("Assembly-CSharp");
                
                // Try different possible type names (both server and client versions)
                // Try full namespace first since we saw them in the debug output
                Type modLoaderManagerType = assemblyCSharp.GetType("HoldfastGame.ClientModLoaderManager");
                if (modLoaderManagerType == null)
                {
                    modLoaderManagerType = assemblyCSharp.GetType("HoldfastGame.ServerModLoaderManager");
                }
                if (modLoaderManagerType == null)
                {
                    modLoaderManagerType = assemblyCSharp.GetType("ClientModLoaderManager");
                }
                if (modLoaderManagerType == null)
                {
                    modLoaderManagerType = assemblyCSharp.GetType("ServerModLoaderManager");
                }
                
                if (modLoaderManagerType == null)
                {
                    AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] ModLoaderManager type not found - will retry later");
                    return false;
                }
                
                // Find the ModLoaderManager instance in the scene
                UnityEngine.Object loader = UnityEngine.Object.FindObjectOfType(modLoaderManagerType);
                
                if (loader == null)
                {
                    // Only log this occasionally to reduce spam
                    return false;
                }
                
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Found {modLoaderManagerType.Name}, attempting registration...");
                
                // Get the holdfastSharedMethodInstances field or property
                FieldInfo instancesField = modLoaderManagerType.GetField("holdfastSharedMethodInstances", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                object instances = null;
                
                if (instancesField != null)
                {
                    instances = instancesField.GetValue(loader);
                    AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Found holdfastSharedMethodInstances field, value: {(instances != null ? instances.GetType().Name : "null")}");
                }
                else
                {
                    // Try property if field doesn't exist
                    PropertyInfo instancesProp = modLoaderManagerType.GetProperty("holdfastSharedMethodInstances",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (instancesProp != null)
                    {
                        instances = instancesProp.GetValue(loader);
                        AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Found holdfastSharedMethodInstances property, value: {(instances != null ? instances.GetType().Name : "null")}");
                    }
                    else
                    {
                        AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] holdfastSharedMethodInstances field/property not found!");
                        
                        // List all fields for debugging
                        var fields = modLoaderManagerType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var f in fields)
                        {
                            AdvancedAdminUIMod.Log.LogInfo($"  Field: {f.Name} ({f.FieldType.Name})");
                        }
                    }
                }
                
                if (instances == null)
                {
                    AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] holdfastSharedMethodInstances is null - will retry later");
                    return false;
                }
                
                // Call Add on the collection
                Type instancesType = instances.GetType();
                MethodInfo addMethod = instancesType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                if (addMethod == null)
                {
                    AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Could not find Add method on {instancesType.Name}");
                    
                    // List all methods for debugging
                    var methods = instancesType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var m in methods)
                    {
                        AdvancedAdminUIMod.Log.LogInfo($"  Method: {m.Name}");
                    }
                    return false;
                }
                
                // Create our instance and add it
                _instance = new HoldfastScriptMod();
                addMethod.Invoke(instances, new object[] { _instance });
                
                // Store reference to instances list for later verification
                _lastKnownInstancesList = instances;
                
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] ✓ Successfully registered with {modLoaderManagerType.Name}!");
                return true;
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[HoldfastScriptMod] Error during registration: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        // Implement IHoldfastSharedMethods interface methods
        // Methods that forward to PlayerEventManager are at the top
        // Empty stub methods are moved to the bottom per user preference

        // Forward OnPlayerSpawned from IHoldfastSharedMethods to our PlayerEventManager
        public void OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry playerFaction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            // Only log when RC logged in to reduce console spam
            if (_isRCLoggedIn)
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnPlayerSpawned: PlayerId={playerId}, SpawnSectionId={spawnSectionId}, Faction={playerFaction}, Class={playerClass}, UniformId={uniformId}, GameObject={(playerObject != null ? playerObject.name : "null")}");
            PlayerEventManager.OnPlayerSpawned(playerId, spawnSectionId, playerFaction, playerClass, uniformId, playerObject);
        }

        // Empty interface stubs (moved to bottom per user preference)
        public void OnUpdateTimeRemaining(float time) { }

        public void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            if (_isRCLoggedIn)
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnRoundDetails: RoundId={roundId}, Server={serverName}, Map={mapName}");
            PlayerEventManager.OnRoundDetails(roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType);
        }

        public void OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            // Track our local player ID by matching Steam ID
            if (steamId == _localSteamId && !isBot)
            {
                _localPlayerId = playerId;
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Identified local player: Id={playerId}, Name={name}");
                
                // Check if we were pending as an auto-admin
                if (_pendingAutoAdminPlayerId == playerId && !_isRCLoggedIn)
                {
                    _isRCLoggedIn = true;
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "  ✓ AUTO-ADMIN DETECTED");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "    Advanced Admin UI ENABLED");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "    Press F3 to open admin panel");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
                }
            }
            
            // Only log when RC logged in to reduce console spam
            if (_isRCLoggedIn)
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnPlayerJoined: PlayerId={playerId}, Name={name}");
            
            PlayerEventManager.OnPlayerJoined(playerId, steamId, name, regimentTag, isBot);
        }

        public void OnPlayerLeft(int playerId)
        {
            PlayerEventManager.OnPlayerLeft(playerId);
        }

        public void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details)
        {
            PlayerEventManager.OnPlayerKilledPlayer(killerPlayerId, victimPlayerId, reason, details);
        }

        public void OnSyncValueState(int value) { }

        public void OnUpdateSyncedTime(double time) { }

        public void OnUpdateElapsedTime(float time) { }

        public void OnIsServer(bool server) { }

        public void OnIsClient(bool client, ulong steamId) 
        {
            _isClientConnected = client;
            if (client)
            {
                _localSteamId = steamId;
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Connected to server (SteamId: {steamId})");
            }
            else
            {
                // Reset RC login state when disconnecting
                bool wasLoggedIn = _isRCLoggedIn;
                _isRCLoggedIn = false;
                _localPlayerId = -1;
                _localSteamId = 0;
                _pendingAutoAdminPlayerId = -1;
                
                if (wasLoggedIn)
                {
                    ColoredLogger.Log(ColoredLogger.Yellow, "═══════════════════════════════════════════");
                    ColoredLogger.Log(ColoredLogger.Yellow, "  Disconnected from server");
                    ColoredLogger.Log(ColoredLogger.Yellow, "  RC login reset - re-login required");
                    ColoredLogger.Log(ColoredLogger.Yellow, "═══════════════════════════════════════════");
                }
                
                // Reset the login prompt flag so it can show again on next server
                AdvancedAdminUIMod.Instance?.ResetRCLoginPromptFlag();
            }
            
            // Notify all features about the connection state change
            PlayerEventManager.OnClientConnectionChanged(client);
        }
        
        /// <summary>
        /// Check if the client is currently connected to a server
        /// </summary>
        public static bool IsClientConnected()
        {
            return _isClientConnected;
        }
        
        /// <summary>
        /// Check if the local player has successfully logged in via RC or has master login
        /// </summary>
        public static bool IsRCLoggedIn()
        {
            // Check master login first (one-time check)
            if (!_masterLoginChecked)
            {
                _masterLoginChecked = true;
                CheckMasterLogin();
            }
            
            return _isRCLoggedIn || _hasMasterLogin;
        }
        
        /// <summary>
        /// Check for master login token file
        /// </summary>
        private static void CheckMasterLogin()
        {
            try
            {
                string dllFolder = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string gameRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..");
                
                // Build search paths
                var searchPaths = new System.Collections.Generic.List<string>();
                
                // Same folder as DLL (BepInEx/plugins)
                searchPaths.Add(System.IO.Path.Combine(dllFolder, MASTER_TOKEN_FILE));
                
                // Mods subfolder of DLL location
                searchPaths.Add(System.IO.Path.Combine(dllFolder, "Mods", MASTER_TOKEN_FILE));
                
                // Game root
                searchPaths.Add(System.IO.Path.Combine(gameRoot, MASTER_TOKEN_FILE));
                
                // Game root/Mods
                searchPaths.Add(System.IO.Path.Combine(gameRoot, "Mods", MASTER_TOKEN_FILE));
                
                // BepInEx folder
                searchPaths.Add(System.IO.Path.Combine(gameRoot, "BepInEx", MASTER_TOKEN_FILE));
                searchPaths.Add(System.IO.Path.Combine(gameRoot, "BepInEx", "plugins", MASTER_TOKEN_FILE));
                searchPaths.Add(System.IO.Path.Combine(gameRoot, "BepInEx", "plugins", "Mods", MASTER_TOKEN_FILE));
                
                // User's AppData folder (common location)
                string appDataPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                searchPaths.Add(System.IO.Path.Combine(appDataPath, "HoldfastModding", MASTER_TOKEN_FILE));
                
                // Log where we're looking for debugging
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Checking for master login token in {searchPaths.Count} locations...");
                
                foreach (string path in searchPaths)
                {
                    try
                    {
                        if (System.IO.File.Exists(path))
                        {
                            string content = System.IO.File.ReadAllText(path).Trim();
                            if (content == "MASTER_ACCESS_GRANTED")
                            {
                                _hasMasterLogin = true;
                                ColoredLogger.Log(ColoredLogger.BrightMagenta, "═══════════════════════════════════════════");
                                ColoredLogger.Log(ColoredLogger.BrightMagenta, "  ★ MASTER LOGIN ACTIVE");
                                ColoredLogger.Log(ColoredLogger.BrightMagenta, "    RC login bypassed on all servers");
                                ColoredLogger.Log(ColoredLogger.BrightMagenta, "    Press F3 to open admin panel");
                                ColoredLogger.Log(ColoredLogger.BrightMagenta, "═══════════════════════════════════════════");
                                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Master login token found at: {path}");
                                return;
                            }
                        }
                    }
                    catch { }
                }
                
                AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] No master login token found");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[HoldfastScriptMod] Error checking master login: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if master login is active (bypasses RC requirement)
        /// </summary>
        public static bool HasMasterLogin()
        {
            if (!_masterLoginChecked)
            {
                _masterLoginChecked = true;
                CheckMasterLogin();
            }
            return _hasMasterLogin;
        }

        public void PassConfigVariables(string[] value) { }

        public void OnPlayerHurt(int playerId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }

        public void OnScorableAction(int playerId, int score, ScorableActionType reason) { }

        public void OnPlayerShoot(int playerId, bool dryShot) { }

        public void OnShotInfo(int playerId, int shotCount, Vector3[][] shotsPointsPositions, float[] trajectileDistances, float[] distanceFromFiringPositions, float[] horizontalDeviationAngles, float[] maxHorizontalDeviationAngles, float[] muzzleVelocities, float[] gravities, float[] damageHitBaseDamages, float[] damageRangeUnitValues, float[] damagePostTraitAndBuffValues, float[] totalDamages, Vector3[] hitPositions, Vector3[] hitDirections, int[] hitPlayerIds, int[] hitDamageableObjectIds, int[] hitShipIds, int[] hitVehicleIds) { }

        public void OnPlayerBlock(int attackingPlayerId, int defendingPlayerId) { }

        public void OnPlayerMeleeStartSecondaryAttack(int playerId) { }

        public void OnPlayerWeaponSwitch(int playerId, string weapon) { }

        public void OnPlayerStartCarry(int playerId, CarryableObjectType carryableObject) { }

        public void OnPlayerEndCarry(int playerId) { }

        public void OnPlayerShout(int playerId, CharacterVoicePhrase voicePhrase) { }

        public void OnConsoleCommand(string input, string output, bool success) { }

        public void OnRCLogin(int playerId, string inputPassword, bool isLoggedIn) 
        {
            // Debug: Log the RC login event details
            AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnRCLogin called: playerId={playerId}, isLoggedIn={isLoggedIn}, _localPlayerId={_localPlayerId}");
            
            // Check if this is our local player logging in
            // Note: If _localPlayerId hasn't been set yet (-1), we can't match, so also check if we're logged in successfully
            // and just accept it for our session (the game only sends us our own RC login events)
            if (playerId == _localPlayerId || (isLoggedIn && _localPlayerId == -1))
            {
                // If we didn't have local player ID, set it now
                if (_localPlayerId == -1 && isLoggedIn)
                {
                    _localPlayerId = playerId;
                    AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Set _localPlayerId from RC login: {playerId}");
                }
                
                _isRCLoggedIn = isLoggedIn;
                if (isLoggedIn)
                {
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "  ✓ RC LOGIN SUCCESSFUL");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "    Advanced Admin UI ENABLED");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "    Press F3 to open admin panel");
                    ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
                }
                else
                {
                    ColoredLogger.Log(ColoredLogger.Yellow, "  ✗ RC Login failed - Features disabled");
                }
            }
        }

        public void OnRCCommand(int playerId, string input, string output, bool success) { }

        public void OnTextMessage(int playerId, TextChatChannel channel, string text) { }

        public void OnAdminPlayerAction(int playerId, int adminId, ServerAdminAction action, string reason) { }

        public void OnDamageableObjectDamaged(GameObject damageableObject, int damageableObjectId, int shipId, int oldHp, int newHp) { }

        public void OnInteractableObjectInteraction(int playerId, int interactableObjectId, GameObject interactableObject, InteractionActivationType interactionActivationType, int nextActivationStateTransitionIndex) { }

        public void OnEmplacementPlaced(int itemId, GameObject objectBuilt, EmplacementType emplacementType) { }

        public void OnEmplacementConstructed(int itemId) { }

        public void OnCapturePointCaptured(int capturePoint) { }

        public void OnCapturePointOwnerChanged(int capturePoint, FactionCountry factionCountry) { }

        public void OnCapturePointDataUpdated(int capturePoint, int defendingPlayerCount, int attackingPlayerCount) { }

        public void OnBuffStart(int playerId, BuffType buff) { }

        public void OnBuffStop(int playerId, BuffType buff) { }

        public void OnRoundEndFactionWinner(FactionCountry factionCountry, FactionRoundWinnerReason reason)
        {
            if (_isRCLoggedIn)
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnRoundEndFactionWinner: Faction={factionCountry}, Reason={reason}");
            PlayerEventManager.OnRoundEndFactionWinner(factionCountry, reason);
        }

        public void OnRoundEndPlayerWinner(int playerId)
        {
            if (_isRCLoggedIn)
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnRoundEndPlayerWinner: PlayerId={playerId}");
            PlayerEventManager.OnRoundEndPlayerWinner(playerId);
        }

        public void OnVehicleSpawned(int vehicleId, FactionCountry vehicleFaction, PlayerClass vehicleClass, GameObject vehicleObject, int ownerPlayerId) { }

        public void OnVehicleHurt(int vehicleId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }

        public void OnPlayerKilledVehicle(int killerPlayerId, int victimVehicleId, EntityHealthChangedReason reason, string details) { }

        public void OnShipSpawned(int shipId, GameObject shipObject, FactionCountry shipfaction, ShipType shipType, int shipName) { }

        public void OnShipDamaged(int shipId, int oldHp, int newHp) { }

        // ============================================
        // IHoldfastSharedMethods3 - Player position packets
        // ============================================
        
        /// <summary>
        /// Called when player position/rotation packet is received from server
        /// This is the best way to track player positions, especially for late joiners
        /// </summary>
        public void OnPlayerPacket(int playerId, Vector3 position, Vector3 rotation)
        {
            // Forward to PlayerEventManager for distribution to features
            PlayerEventManager.OnPlayerPacket(playerId, position, rotation);
        }
        
        // IHoldfastSharedMethods3 stubs
        public void OnStartSpectate(int playerId, int spectatedPlayerId) { }
        public void OnStopSpectate(int playerId, int spectatedPlayerId) { }
        public void OnStartFreeflight(int playerId) { }
        public void OnStopFreeflight(int playerId) { }
        public void OnMeleeArenaRoundEndFactionWinner(int roundId, bool attackers) { }
        public void OnPlayerConnected(int playerId, bool isAutoAdmin, string backendId) 
        {
            // Check if this is our local player connecting with auto-admin privileges
            // backendId is typically the PlayFab ID or similar backend identifier
            AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] OnPlayerConnected: playerId={playerId}, isAutoAdmin={isAutoAdmin}, backendId={backendId}");
            
            if (isAutoAdmin)
            {
                // If this is our local player (check by matching steam ID or if we're still waiting for our player ID)
                // The backendId might be the PlayFab ID, so we also need to check player join events
                // For now, store this as a potential auto-admin and confirm when we get OnPlayerJoined
                _pendingAutoAdminPlayerId = playerId;
                AdvancedAdminUIMod.Log.LogInfo($"[HoldfastScriptMod] Detected potential auto-admin: playerId={playerId}");
            }
        }
        
        // Track pending auto-admin status until we confirm it's our player
        private static int _pendingAutoAdminPlayerId = -1;
        public void OnPlayerDisconnected(int playerId) { }
    }
}

