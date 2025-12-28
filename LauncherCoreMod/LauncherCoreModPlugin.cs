using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HoldfastSharedMethods;
using UnityEngine;
using UnityEngine.UI;

namespace LauncherCoreMod
{
    /// <summary>
    /// Separate runner MonoBehaviour that lives on its own GameObject.
    /// This ensures Update() is called even if BepInEx_Manager gets deactivated.
    /// </summary>
    public class CoreModRunner : MonoBehaviour
    {
        private bool _hasLoggedFirstUpdate = false;
        private int _updateCount = 0;
        
        void Awake()
        {
            LauncherCoreModPlugin.Log?.LogInfo("[CoreModRunner] Awake() called - component initializing");
        }
        
        void Start()
        {
            LauncherCoreModPlugin.Log?.LogInfo("[CoreModRunner] Start() called - component is active!");
        }
        
        void OnEnable()
        {
            LauncherCoreModPlugin.Log?.LogInfo("[CoreModRunner] OnEnable() - runner enabled");
        }
        
        void Update()
        {
            _updateCount++;
            
            try
            {
                if (!_hasLoggedFirstUpdate)
                {
                    _hasLoggedFirstUpdate = true;
                    LauncherCoreModPlugin.Log?.LogInfo($"[CoreModRunner] First Update() confirmed - runner is active! Instance={LauncherCoreModPlugin.Instance != null}");
                }
                
                if (_updateCount == 100)
                {
                    LauncherCoreModPlugin.Log?.LogInfo("[CoreModRunner] 100 updates processed");
                }
                
                if (LauncherCoreModPlugin.Instance != null)
                {
                    LauncherCoreModPlugin.Instance.DoUpdate();
                }
            }
            catch (Exception ex)
            {
                if (_updateCount < 5)
                {
                    LauncherCoreModPlugin.Log?.LogError($"[CoreModRunner] Exception in Update: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
        
        void OnApplicationQuit()
        {
            LauncherCoreModPlugin.Instance?.DoOnApplicationQuit();
        }
    }
    
    /// <summary>
    /// Core mod for the Holdfast Modding Launcher
    /// Provides essential features for all launcher users:
    /// - Server browser filtering (hides official servers for non-master users)
    /// - Game event dispatching via IHoldfastSharedMethods (for other mods to subscribe to)
    /// - Master login verification
    /// </summary>
    [BepInPlugin("com.xarkanoth.launchercoremod", "Launcher Core Mod", "1.0.7")]
    public class LauncherCoreModPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        public static LauncherCoreModPlugin Instance { get; private set; }
        
        private ServerBrowserFilter _serverBrowserFilter;
        private GameEventDispatcher _eventDispatcher;
        private GameObject _runnerObject;
        
        void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("Launcher Core Mod loaded!");
            
            // Create a persistent runner GameObject that won't be disabled
            _runnerObject = new GameObject("LauncherCoreModRunner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
            _runnerObject.AddComponent<CoreModRunner>();
            Log.LogInfo("[Awake] Created persistent runner GameObject");
            
            // Initialize server browser filter
            _serverBrowserFilter = new ServerBrowserFilter();
            _serverBrowserFilter.Initialize();
            
            // Initialize game event dispatcher
            _eventDispatcher = new GameEventDispatcher();
            Log.LogInfo("[Awake] Game event dispatcher initialized");
        }
        
        public void DoUpdate()
        {
            _serverBrowserFilter?.OnUpdate();
            _eventDispatcher?.OnUpdate();
        }
        
        public void DoOnApplicationQuit()
        {
            _serverBrowserFilter?.Shutdown();
        }
    }
    
    #region Game Event System
    
    /// <summary>
    /// Static class that exposes game events for other mods to subscribe to.
    /// This is the central hub for all Holdfast game events.
    /// </summary>
    public static class GameEvents
    {
        // ==========================================
        // CONNECTION EVENTS
        // ==========================================
        
        /// <summary>Fired when the client connects to a server. Args: steamId</summary>
        public static event Action<ulong> OnConnectedToServer;
        
        /// <summary>Fired when the client disconnects from a server</summary>
        public static event Action OnDisconnectedFromServer;
        
        // ==========================================
        // PLAYER EVENTS
        // ==========================================
        
        /// <summary>Fired when any player joins. Args: playerId, steamId, name, regimentTag, isBot</summary>
        public static event Action<int, ulong, string, string, bool> OnPlayerJoined;
        
        /// <summary>Fired when any player leaves. Args: playerId</summary>
        public static event Action<int> OnPlayerLeft;
        
        /// <summary>Fired when any player spawns. Args: playerId, spawnSectionId, faction, playerClass, uniformId, playerObject</summary>
        public static event Action<int, int, FactionCountry, PlayerClass, int, GameObject> OnPlayerSpawned;
        
        /// <summary>Fired when local player spawns. Args: playerId, faction, playerClass</summary>
        public static event Action<int, FactionCountry, PlayerClass> OnLocalPlayerSpawned;
        
        /// <summary>Fired when player position update received. Args: playerId, position, rotation</summary>
        public static event Action<int, Vector3, Vector3> OnPlayerPacket;
        
        /// <summary>Fired when a player kills another. Args: killerPlayerId, victimPlayerId, reason, details</summary>
        public static event Action<int, int, EntityHealthChangedReason, string> OnPlayerKilledPlayer;
        
        // ==========================================
        // ROUND EVENTS
        // ==========================================
        
        /// <summary>Fired when round details are received. Args: roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType</summary>
        public static event Action<int, string, string, FactionCountry, FactionCountry, GameplayMode, GameType> OnRoundDetails;
        
        /// <summary>Fired when round ends. Args: winningFaction, reason</summary>
        public static event Action<FactionCountry, FactionRoundWinnerReason> OnRoundEndFactionWinner;
        
        // ==========================================
        // ADMIN EVENTS
        // ==========================================
        
        /// <summary>Fired when RC login occurs. Args: playerId, isLoggedIn</summary>
        public static event Action<int, bool> OnRCLogin;
        
        /// <summary>Fired when a player connects (IHoldfastSharedMethods3). Args: playerId, isAutoAdmin, backendId</summary>
        public static event Action<int, bool, string> OnPlayerConnected;
        
        // ==========================================
        // INTERNAL DISPATCH METHODS (called by GameEventDispatcher)
        // ==========================================
        
        internal static void RaiseConnectedToServer(ulong steamId) => OnConnectedToServer?.Invoke(steamId);
        internal static void RaiseDisconnectedFromServer() => OnDisconnectedFromServer?.Invoke();
        internal static void RaisePlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot) 
            => OnPlayerJoined?.Invoke(playerId, steamId, name, regimentTag, isBot);
        internal static void RaisePlayerLeft(int playerId) => OnPlayerLeft?.Invoke(playerId);
        internal static void RaisePlayerSpawned(int playerId, int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject)
            => OnPlayerSpawned?.Invoke(playerId, spawnSectionId, faction, playerClass, uniformId, playerObject);
        internal static void RaiseLocalPlayerSpawned(int playerId, FactionCountry faction, PlayerClass playerClass)
            => OnLocalPlayerSpawned?.Invoke(playerId, faction, playerClass);
        internal static void RaisePlayerPacket(int playerId, Vector3 position, Vector3 rotation)
            => OnPlayerPacket?.Invoke(playerId, position, rotation);
        internal static void RaisePlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details)
            => OnPlayerKilledPlayer?.Invoke(killerPlayerId, victimPlayerId, reason, details);
        internal static void RaiseRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
            => OnRoundDetails?.Invoke(roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType);
        internal static void RaiseRoundEndFactionWinner(FactionCountry faction, FactionRoundWinnerReason reason)
            => OnRoundEndFactionWinner?.Invoke(faction, reason);
        internal static void RaiseRCLogin(int playerId, bool isLoggedIn) => OnRCLogin?.Invoke(playerId, isLoggedIn);
        internal static void RaisePlayerConnected(int playerId, bool isAutoAdmin, string backendId)
            => OnPlayerConnected?.Invoke(playerId, isAutoAdmin, backendId);
    }
    
    /// <summary>
    /// IHoldfastSharedMethods3 interface - defined here since it may not be in referenced DLLs
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
    /// Handles registration with Holdfast's mod system and dispatches events to subscribers
    /// </summary>
    public class GameEventDispatcher
    {
        private HoldfastEventReceiver _receiver;
        private bool _registered = false;
        private float _lastRegistrationAttempt = 0f;
        private const float REGISTRATION_RETRY_INTERVAL = 2f;
        private bool _loggedWaiting = false;
        private int _updateCallCount = 0;
        private bool _loggedFirstUpdate = false;
        
        public void OnUpdate()
        {
            _updateCallCount++;
            
            if (!_loggedFirstUpdate)
            {
                _loggedFirstUpdate = true;
                LauncherCoreModPlugin.Log?.LogInfo("[GameEventDispatcher] First OnUpdate() call received!");
            }
            
            // Try to register with the game if not yet registered
            if (!_registered && Time.time - _lastRegistrationAttempt > REGISTRATION_RETRY_INTERVAL)
            {
                _lastRegistrationAttempt = Time.time;
                TryRegister();
            }
        }
        
        private void TryRegister()
        {
            if (_receiver != null && HoldfastEventReceiver.IsStillRegistered())
            {
                _registered = true;
                return;
            }
            
            if (!_loggedWaiting)
            {
                LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] Waiting for ClientModLoaderManager...");
                _loggedWaiting = true;
            }
            
            if (HoldfastEventReceiver.Register())
            {
                _registered = true;
                _receiver = HoldfastEventReceiver.Instance;
                LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] ✓ Successfully registered with game!");
            }
        }
    }
    
    /// <summary>
    /// Implements IHoldfastSharedMethods and IHoldfastSharedMethods3 to receive events from Holdfast
    /// Dispatches events to the static GameEvents class for other mods to consume
    /// </summary>
    public class HoldfastEventReceiver : IHoldfastSharedMethods, IHoldfastSharedMethods3
    {
        public static HoldfastEventReceiver Instance { get; private set; }
        private static object _lastKnownInstancesList = null;
        
        // Client state tracking
        private static bool _isClientConnected = false;
        private static ulong _localSteamId = 0;
        private static int _localPlayerId = -1;
        private static bool _isMasterLoggedIn = false;
        
        /// <summary>Check if connected to a server</summary>
        public static bool IsClientConnected => _isClientConnected;
        
        /// <summary>Local player's Steam ID</summary>
        public static ulong LocalSteamId => _localSteamId;
        
        /// <summary>Local player's in-game ID (-1 if not known)</summary>
        public static int LocalPlayerId => _localPlayerId;
        
        /// <summary>Check if master login is active</summary>
        public static bool IsMasterLoggedIn => _isMasterLoggedIn || MasterLoginManager.IsMasterLoggedIn();
        
        public static bool Register()
        {
            // If we're already successfully registered, verify and return
            if (Instance != null && IsStillRegistered())
            {
                return true;
            }
            
            Instance = null;
            _lastKnownInstancesList = null;
            
            try
            {
                Assembly assemblyCSharp = Assembly.Load("Assembly-CSharp");
                
                // Try to find the mod loader manager
                Type modLoaderManagerType = assemblyCSharp.GetType("HoldfastGame.ClientModLoaderManager")
                    ?? assemblyCSharp.GetType("HoldfastGame.ServerModLoaderManager")
                    ?? assemblyCSharp.GetType("ClientModLoaderManager")
                    ?? assemblyCSharp.GetType("ServerModLoaderManager");
                
                if (modLoaderManagerType == null)
                {
                    LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] ModLoaderManager type not found - will retry later");
                    return false;
                }
                
                // Find the ModLoaderManager instance in the scene
                UnityEngine.Object loader = UnityEngine.Object.FindObjectOfType(modLoaderManagerType);
                
                if (loader == null)
                {
                    LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] {modLoaderManagerType.Name} not found in scene - will retry (normal until in-game)");
                    return false;
                }
                
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] Found {modLoaderManagerType.Name}, attempting registration...");
                
                // Get the holdfastSharedMethodInstances field
                FieldInfo instancesField = modLoaderManagerType.GetField("holdfastSharedMethodInstances",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                object instances = null;
                
                if (instancesField != null)
                {
                    instances = instancesField.GetValue(loader);
                }
                else
                {
                    PropertyInfo instancesProp = modLoaderManagerType.GetProperty("holdfastSharedMethodInstances",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (instancesProp != null)
                    {
                        instances = instancesProp.GetValue(loader);
                    }
                }
                
                if (instances == null)
                {
                    LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] holdfastSharedMethodInstances is null - will retry later");
                    return false;
                }
                
                // Call Add on the collection
                Type instancesType = instances.GetType();
                MethodInfo addMethod = instancesType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                if (addMethod == null)
                {
                    LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] Could not find Add method on {instancesType.Name}");
                    return false;
                }
                
                // Create our instance and add it
                Instance = new HoldfastEventReceiver();
                addMethod.Invoke(instances, new object[] { Instance });
                
                // Store reference to instances list for later verification
                _lastKnownInstancesList = instances;
                
                LauncherCoreModPlugin.Log?.LogInfo("═══════════════════════════════════════════");
                LauncherCoreModPlugin.Log?.LogInfo($"  ✓ GAME EVENTS REGISTERED");
                LauncherCoreModPlugin.Log?.LogInfo($"    Now receiving Holdfast events!");
                LauncherCoreModPlugin.Log?.LogInfo("═══════════════════════════════════════════");
                return true;
            }
            catch (Exception ex)
            {
                LauncherCoreModPlugin.Log?.LogError($"[GameEvents] Error during registration: {ex.Message}");
                return false;
            }
        }
        
        public static bool IsStillRegistered()
        {
            if (Instance == null || _lastKnownInstancesList == null)
                return false;
            
            try
            {
                Type listType = _lastKnownInstancesList.GetType();
                MethodInfo containsMethod = listType.GetMethod("Contains");
                if (containsMethod != null)
                {
                    return (bool)containsMethod.Invoke(_lastKnownInstancesList, new object[] { Instance });
                }
            }
            catch { }
            return false;
        }
        
        // ==========================================
        // IHoldfastSharedMethods Implementation
        // ==========================================
        
        public void OnIsClient(bool client, ulong steamId)
        {
            bool wasConnected = _isClientConnected;
            _isClientConnected = client;
            
            if (client)
            {
                _localSteamId = steamId;
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ STEP 1: OnIsClient - Connected!");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Local Steam ID: {steamId}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                GameEvents.RaiseConnectedToServer(steamId);
            }
            else
            {
                if (wasConnected)
                {
                    LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] Disconnected from server - resetting state");
                    GameEvents.RaiseDisconnectedFromServer();
                }
                
                // Reset state
                _localPlayerId = -1;
                _localSteamId = 0;
                Instance = null;
                _lastKnownInstancesList = null;
            }
        }
        
        public void OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            // Log all player joins for debugging
            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] OnPlayerJoined: Id={playerId}, SteamId={steamId}, Name={name}, IsBot={isBot}");
            
            // Track local player - steamId is 0 for the local player (server doesn't send your own ID)
            // Or it matches our Steam ID if we captured it from OnIsClient
            if (!isBot && (steamId == 0 || (_localSteamId > 0 && steamId == _localSteamId)))
            {
                _localPlayerId = playerId;
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ STEP 2: Local player identified!");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Player ID: {playerId}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Name: {name}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Matched via: {(steamId == 0 ? "SteamId=0" : "SteamId match")}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
            }
            
            GameEvents.RaisePlayerJoined(playerId, steamId, name, regimentTag, isBot);
        }
        
        public void OnPlayerLeft(int playerId)
        {
            GameEvents.RaisePlayerLeft(playerId);
        }
        
        public void OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry playerFaction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            // Log spawn for debugging
            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] OnPlayerSpawned: Id={playerId}, Class={playerClass}, HasObject={playerObject != null}");
            
            GameEvents.RaisePlayerSpawned(playerId, spawnSectionId, playerFaction, playerClass, uniformId, playerObject);
            
            // Check if this is our local player
            bool isLocalPlayer = playerId == _localPlayerId;
            
            // Fallback: If we never identified local player, use first spawn with playerObject
            if (!isLocalPlayer && _localPlayerId == -1 && playerObject != null)
            {
                _localPlayerId = playerId;
                isLocalPlayer = true;
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ Local player assumed from first spawn with object: Id={playerId}");
            }
            
            if (isLocalPlayer)
            {
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ STEP 3: Local player spawned!");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Player ID: {playerId}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Class: {playerClass}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Faction: {playerFaction}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   → Triggering rangefinder setup!");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                GameEvents.RaiseLocalPlayerSpawned(playerId, playerFaction, playerClass);
            }
        }
        
        public void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] Round: {mapName} ({gameplayMode})");
            GameEvents.RaiseRoundDetails(roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType);
        }
        
        public void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details)
        {
            GameEvents.RaisePlayerKilledPlayer(killerPlayerId, victimPlayerId, reason, details);
        }
        
        public void OnRCLogin(int playerId, string inputPassword, bool isLoggedIn)
        {
            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] RC Login: playerId={playerId}, isLoggedIn={isLoggedIn}");
            GameEvents.RaiseRCLogin(playerId, isLoggedIn);
        }
        
        public void OnRoundEndFactionWinner(FactionCountry factionCountry, FactionRoundWinnerReason reason)
        {
            GameEvents.RaiseRoundEndFactionWinner(factionCountry, reason);
        }
        
        // IHoldfastSharedMethods3 Implementation
        public void OnPlayerPacket(int playerId, Vector3 position, Vector3 rotation)
        {
            GameEvents.RaisePlayerPacket(playerId, position, rotation);
        }
        
        public void OnPlayerConnected(int playerId, bool isAutoAdmin, string backendId)
        {
            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] Player connected: Id={playerId}, AutoAdmin={isAutoAdmin}");
            GameEvents.RaisePlayerConnected(playerId, isAutoAdmin, backendId);
        }
        
        public void OnPlayerDisconnected(int playerId) { }
        public void OnStartSpectate(int playerId, int spectatedPlayerId) { }
        public void OnStopSpectate(int playerId, int spectatedPlayerId) { }
        public void OnStartFreeflight(int playerId) { }
        public void OnStopFreeflight(int playerId) { }
        public void OnMeleeArenaRoundEndFactionWinner(int roundId, bool attackers) { }
        
        // Empty IHoldfastSharedMethods stubs
        public void OnUpdateTimeRemaining(float time) { }
        public void OnUpdateSyncedTime(double time) { }
        public void OnUpdateElapsedTime(float time) { }
        public void OnIsServer(bool server) { }
        public void OnSyncValueState(int value) { }
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
        public void OnRoundEndPlayerWinner(int playerId) { }
        public void OnVehicleSpawned(int vehicleId, FactionCountry vehicleFaction, PlayerClass vehicleClass, GameObject vehicleObject, int ownerPlayerId) { }
        public void OnVehicleHurt(int vehicleId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }
        public void OnPlayerKilledVehicle(int killerPlayerId, int victimVehicleId, EntityHealthChangedReason reason, string details) { }
        public void OnShipSpawned(int shipId, GameObject shipObject, FactionCountry shipfaction, ShipType shipType, int shipName) { }
        public void OnShipDamaged(int shipId, int oldHp, int newHp) { }
    }
    
    #endregion
    
    #region Master Login Manager
    
    /// <summary>
    /// Manages master login token verification
    /// </summary>
    public static class MasterLoginManager
    {
        private static bool _checked = false;
        private static bool _isMasterLoggedIn = false;
        private static float _lastCheck = 0f;
        private const float CHECK_INTERVAL = 5f;
        
        private const string HASH_SALT = "HF_MODDING_2024_XARK";
        private const string LOGIN_TOKEN_FILE = "master_login.token";
        
        public static bool IsMasterLoggedIn()
        {
            if (!_checked || Time.time - _lastCheck > CHECK_INTERVAL)
            {
                _lastCheck = Time.time;
                _checked = true;
                CheckToken();
            }
            return _isMasterLoggedIn;
        }
        
        private static void CheckToken()
        {
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
                            break;
                        }
                    }
                }
            }
            catch { }
        }
        
        private static string[] GetPossibleTokenPaths()
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
        
        private static bool VerifySecureToken(string token)
        {
            if (token == "MASTER_ACCESS_GRANTED") return true;
            if (token == CreateExpectedToken(DateTime.UtcNow)) return true;
            if (token == CreateExpectedToken(DateTime.UtcNow.AddDays(-1))) return true;
            return false;
        }
        
        private static string CreateExpectedToken(DateTime date)
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
    }
    
    #endregion
    
    #region Server Browser Filter
    
    /// <summary>
    /// Filters official "Anvil Game Studios Official" servers from the server browser
    /// unless the user is master logged in via the launcher.
    /// </summary>
    public class ServerBrowserFilter
    {
        private static ManualLogSource _log => LauncherCoreModPlugin.Log;
        private static float _lastTokenCheck = 0f;
        private const float TOKEN_CHECK_INTERVAL = 5f;
        
        // Harmony
        private static Harmony _harmony;
        private static bool _patchesApplied = false;
        
        // UI paths
        private const string SERVER_BROWSER_PATH = "MainCanvas/Main Menu Panels/Panel Container/Play/Server Browser/Server Browser Container/Browser Container/Scroll View/Viewport/Content";
        private const string CUSTOM_SERVER_BUTTON_PATH = "MainCanvas/Main Menu Panels/Panel Container/Play/Server Browser/Server Browser Container/Bottom Buttons Layout/Custom Server Button";
        
        // Tracking
        private static HashSet<int> _hiddenServerInstanceIds = new HashSet<int>();
        private static bool _customServerButtonHidden = false;
        private static float _lastBrowserOpenTime = 0f;
        private static float _lastServerScan = 0f;
        private const float SERVER_SCAN_INTERVAL = 0.5f;
        
        public void Initialize()
        {
            _log?.LogInfo("[ServerBrowserFilter] Initializing...");
            ApplyHarmonyPatches();
            _log?.LogInfo($"[ServerBrowserFilter] Ready (Master login: {MasterLoginManager.IsMasterLoggedIn()})");
        }
        
        public void Shutdown()
        {
            RemoveHarmonyPatches();
        }
        
        private void ApplyHarmonyPatches()
        {
            if (_patchesApplied) return;
            
            try
            {
                _harmony = new Harmony("com.xarkanoth.launchercoremod.serverbrowserfilter");
                
                // Find HarperServerBrowserItem.Bind method
                Type browserItemType = FindType("HarperServerBrowserItem");
                if (browserItemType == null)
                {
                    _log?.LogWarning("[ServerBrowserFilter] HarperServerBrowserItem not found - using fallback filtering");
                    return;
                }
                
                MethodInfo bindMethod = browserItemType.GetMethod("Bind", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (bindMethod != null)
                {
                    var prefix = typeof(ServerBrowserFilter).GetMethod(nameof(Prefix_OnBind), 
                        BindingFlags.Static | BindingFlags.NonPublic);
                    
                    _harmony.Patch(bindMethod, prefix: new HarmonyMethod(prefix));
                    _patchesApplied = true;
                    _log?.LogInfo("[ServerBrowserFilter] ✓ Harmony patch applied to HarperServerBrowserItem.Bind");
                }
                else
                {
                    _log?.LogWarning("[ServerBrowserFilter] Bind method not found - using fallback filtering");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[ServerBrowserFilter] Failed to apply patches: {ex.Message}");
            }
        }
        
        private void RemoveHarmonyPatches()
        {
            try
            {
                if (_harmony != null && _patchesApplied)
                {
                    _harmony.UnpatchSelf();
                    _patchesApplied = false;
                    _log?.LogInfo("[ServerBrowserFilter] Harmony patches removed");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ServerBrowserFilter] Error removing patches: {ex.Message}");
            }
        }
        
        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
        
        /// <summary>
        /// Harmony PREFIX - SKIP binding official servers entirely
        /// </summary>
        private static bool Prefix_OnBind(object __instance, object serverTarget, int itemIndex)
        {
            try
            {
                if (MasterLoginManager.IsMasterLoggedIn()) return true;
                
                // Extract server name from ValueTuple<Int32, String, Int32> - Item2 is server name
                string serverName = ExtractServerNameFromTuple(serverTarget);
                
                if (ShouldFilterServer(serverName))
                {
                    var mb = __instance as MonoBehaviour;
                    if (mb != null)
                    {
                        HideServerItem(mb.transform);
                    }
                    return false; // Skip Bind entirely
                }
                else
                {
                    var mb = __instance as MonoBehaviour;
                    if (mb != null)
                    {
                        ShowServerItem(mb.transform);
                    }
                }
            }
            catch { }
            
            return true;
        }
        
        private static string ExtractServerNameFromTuple(object serverTarget)
        {
            if (serverTarget == null) return null;
            
            try
            {
                var item2Field = serverTarget.GetType().GetField("Item2");
                if (item2Field != null)
                {
                    return item2Field.GetValue(serverTarget) as string;
                }
            }
            catch { }
            
            return null;
        }
        
        private static void HideServerItem(Transform serverItem)
        {
            try
            {
                int instanceId = serverItem.gameObject.GetInstanceID();
                
                CanvasGroup canvasGroup = serverItem.gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = serverItem.gameObject.AddComponent<CanvasGroup>();
                
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                
                LayoutElement layoutElement = serverItem.gameObject.GetComponent<LayoutElement>();
                if (layoutElement == null)
                    layoutElement = serverItem.gameObject.AddComponent<LayoutElement>();
                
                layoutElement.preferredHeight = 0f;
                layoutElement.minHeight = 0f;
                layoutElement.flexibleHeight = 0f;
                layoutElement.ignoreLayout = true;
                
                serverItem.localScale = Vector3.zero;
                
                _hiddenServerInstanceIds.Add(instanceId);
            }
            catch { }
        }
        
        private static void ShowServerItem(Transform serverItem)
        {
            try
            {
                int instanceId = serverItem.gameObject.GetInstanceID();
                
                if (!_hiddenServerInstanceIds.Contains(instanceId)) return;
                
                CanvasGroup canvasGroup = serverItem.gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f;
                    canvasGroup.blocksRaycasts = true;
                    canvasGroup.interactable = true;
                }
                
                LayoutElement layoutElement = serverItem.gameObject.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredHeight = -1f;
                    layoutElement.minHeight = -1f;
                    layoutElement.flexibleHeight = -1f;
                    layoutElement.ignoreLayout = false;
                }
                
                serverItem.localScale = Vector3.one;
                
                _hiddenServerInstanceIds.Remove(instanceId);
            }
            catch { }
        }
        
        public void OnUpdate()
        {
            if (MasterLoginManager.IsMasterLoggedIn()) return;
            
            // Fallback UI filtering
            if (Time.time - _lastServerScan > SERVER_SCAN_INTERVAL)
            {
                _lastServerScan = Time.time;
                FallbackFilterServerBrowser();
            }
        }
        
        private void FallbackFilterServerBrowser()
        {
            try
            {
                GameObject contentObj = GameObject.Find(SERVER_BROWSER_PATH);
                if (contentObj == null)
                {
                    if (_hiddenServerInstanceIds.Count > 0)
                    {
                        _hiddenServerInstanceIds.Clear();
                        _customServerButtonHidden = false;
                        _lastBrowserOpenTime = 0f;
                    }
                    return;
                }
                
                if (_lastBrowserOpenTime == 0f)
                {
                    _lastBrowserOpenTime = Time.time;
                    _hiddenServerInstanceIds.Clear();
                    _customServerButtonHidden = false;
                }
                
                // Hide Custom Server Button
                if (!_customServerButtonHidden)
                {
                    GameObject customBtn = GameObject.Find(CUSTOM_SERVER_BUTTON_PATH);
                    if (customBtn != null)
                    {
                        customBtn.SetActive(false);
                        _customServerButtonHidden = true;
                        _log?.LogInfo("[ServerBrowserFilter] Hidden Custom Server Button");
                    }
                }
            }
            catch { }
        }
        
        public static bool ShouldFilterServer(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return false;
            if (MasterLoginManager.IsMasterLoggedIn()) return false;
            
            return serverName.StartsWith("Anvil Game Studios Official", StringComparison.OrdinalIgnoreCase);
        }
    }
    
    #endregion
}
