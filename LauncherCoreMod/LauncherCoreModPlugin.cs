using System;
using System.Collections;
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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LauncherCoreMod
{
    /// <summary>
    /// Persistent runner MonoBehaviour on its own GameObject.
    /// Created after first scene load to avoid being destroyed during BepInEx chainloader.
    /// </summary>
    public class LauncherCoreRunner : MonoBehaviour
    {
        private LauncherCoreModPlugin _plugin;
        private bool _firstUpdate = true;
        private int _updateCount = 0;
        
        public void Initialize(LauncherCoreModPlugin plugin)
        {
            _plugin = plugin;
            LauncherCoreModPlugin.Log?.LogInfo("[LauncherCoreRunner] Initialized and ready");
        }
        
        void OnDestroy()
        {
            LauncherCoreModPlugin.Log?.LogError("[LauncherCoreRunner] OnDestroy called! Will re-create on next scene load.");
        }
        
        void Update()
        {
            _updateCount++;
            if (_firstUpdate)
            {
                _firstUpdate = false;
                LauncherCoreModPlugin.Log?.LogInfo($"[LauncherCoreRunner] First Update() call! Frame={Time.frameCount} Time={Time.time:F2}");
            }
            
            if (_plugin != null)
            {
                try
                {
                    _plugin.DoUpdate();
                }
                catch (Exception ex)
                {
                    LauncherCoreModPlugin.Log?.LogError($"[LauncherCoreRunner] Exception in Update: {ex}");
                }
            }
        }
    }
    
    /// <summary>
    /// Core mod for the Holdfast Modding Launcher
    /// Provides essential features for all launcher users:
    /// - Server browser filtering (hides official servers for non-master users)
    /// - Game event dispatching via IHoldfastSharedMethods (for other mods to subscribe to)
    /// - Master login verification
    /// </summary>
    [BepInPlugin("com.xarkanoth.launchercoremod", "Launcher Core Mod", "1.0.13")]
    public class LauncherCoreModPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }
        public static LauncherCoreModPlugin Instance { get; private set; }
        
        private ServerBrowserFilter _serverBrowserFilter;
        private GameEventDispatcher _eventDispatcher;
        private GameObject _runnerObject;
        private bool _runnerCreated = false;
        
        void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("Launcher Core Mod loaded!");
            
            // Initialize server browser filter (doesn't need MonoBehaviour)
            _serverBrowserFilter = new ServerBrowserFilter();
            _serverBrowserFilter.Initialize();
            
            // Initialize game event dispatcher
            _eventDispatcher = new GameEventDispatcher();
            Log.LogInfo("[Awake] Game event dispatcher initialized");
            
            // DO NOT create runner GameObjects here - they get destroyed during chainloader cleanup.
            // Instead, wait for first scene load and also start a coroutine on the plugin itself.
            SceneManager.sceneLoaded += OnSceneLoaded;
            Log.LogInfo("[Awake] Subscribed to SceneManager.sceneLoaded - will create runner after first scene loads");
            
            // Start the main work loop as a coroutine on the plugin's own MonoBehaviour
            // (plugin lives on BepInEx's manager object which is never destroyed)
            StartCoroutine(MainLoopCoroutine());
            Log.LogInfo("[Awake] Started main loop coroutine on plugin object");
        }
        
        /// <summary>
        /// Called when any scene loads. Creates/recreates the persistent runner.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo($"[SceneLoaded] Scene '{scene.name}' loaded (mode={mode}). Creating runner if needed...");
            EnsureRunnerExists();
            
            // Try game event registration on every scene load
            // Pass scene info so dispatcher can force re-registration on server changes
            _eventDispatcher?.TryRegisterNow(scene.name, mode);
        }
        
        /// <summary>
        /// Creates the persistent runner GameObject if it doesn't already exist.
        /// Called after scene loads so DontDestroyOnLoad actually works.
        /// </summary>
        private void EnsureRunnerExists()
        {
            if (_runnerObject != null) return;
            
            try
            {
                _runnerObject = new GameObject("LauncherCoreModRunner");
                DontDestroyOnLoad(_runnerObject);
                var runner = _runnerObject.AddComponent<LauncherCoreRunner>();
                runner.Initialize(this);
                _runnerCreated = true;
                Log.LogInfo("[EnsureRunner] ✓ Runner GameObject created and marked DontDestroyOnLoad");
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnsureRunner] Failed to create runner: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Main work loop running as a coroutine on the plugin's own MonoBehaviour.
        /// This is the primary driver now - runs on BepInEx's own GameObject which persists.
        /// </summary>
        private IEnumerator MainLoopCoroutine()
        {
            Log.LogInfo("[MainLoop] Coroutine started - waiting for first frame...");
            yield return null; // Wait one frame for chainloader to finish
            
            Log.LogInfo("[MainLoop] First frame complete. Starting work loop.");
            
            float lastUpdate = 0f;
            int loopCount = 0;
            
            while (true)
            {
                loopCount++;
                
                // Log periodically
                if (loopCount <= 5 || loopCount % 50 == 0)
                {
                    Log.LogInfo($"[MainLoop] Tick #{loopCount} | Time={Time.time:F1} | RunnerAlive={_runnerObject != null}");
                }
                
                // Do the actual work every frame-ish (coroutine yield null = every frame)
                try
                {
                    DoUpdate();
                }
                catch (Exception ex)
                {
                    Log.LogError($"[MainLoop] Error in DoUpdate: {ex.Message}");
                }
                
                // Re-create runner if it was destroyed (scene change, etc.)
                if (_runnerCreated && _runnerObject == null)
                {
                    Log.LogWarning("[MainLoop] Runner was destroyed! Attempting re-creation...");
                    EnsureRunnerExists();
                }
                
                yield return null; // Every frame
            }
        }
        
        public void DoUpdate()
        {
            _serverBrowserFilter?.OnUpdate();
            _eventDispatcher?.OnUpdate();
        }
        
        void OnApplicationQuit()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
        // COMBAT EVENTS
        // ==========================================
        
        /// <summary>Fired when a player starts a melee secondary attack. Args: playerId</summary>
        public static event Action<int> OnPlayerMeleeStartSecondaryAttack;
        
        /// <summary>Fired when a player is hurt. Args: playerId, oldHp, newHp, reason</summary>
        public static event Action<int, byte, byte, EntityHealthChangedReason> OnPlayerHurt;
        
        /// <summary>Fired when a player blocks an attack. Args: attackingPlayerId, defendingPlayerId</summary>
        public static event Action<int, int> OnPlayerBlock;
        
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
        internal static void RaisePlayerMeleeStartSecondaryAttack(int playerId)
            => OnPlayerMeleeStartSecondaryAttack?.Invoke(playerId);
        internal static void RaisePlayerHurt(int playerId, byte oldHp, byte newHp, EntityHealthChangedReason reason)
            => OnPlayerHurt?.Invoke(playerId, oldHp, newHp, reason);
        internal static void RaisePlayerBlock(int attackingPlayerId, int defendingPlayerId)
            => OnPlayerBlock?.Invoke(attackingPlayerId, defendingPlayerId);
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
        
        /// <summary>
        /// Called from OnSceneLoaded. For ClientScene (Single mode), forces re-registration
        /// since the old ClientModLoaderManager was destroyed on server change.
        /// </summary>
        public void TryRegisterNow(string sceneName = null, LoadSceneMode mode = LoadSceneMode.Additive)
        {
            // ClientScene loaded as Single = connecting to a new server
            // The old ClientModLoaderManager is gone, so force re-registration
            if (sceneName == "ClientScene" && mode == LoadSceneMode.Single)
            {
                LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] ClientScene loaded - forcing re-registration for new server");
                _registered = false;
                _receiver = null;
                _loggedWaiting = false;
                _registrationAttempts = 0;
                HoldfastEventReceiver.ResetRegistration();
            }
            
            TryRegister();
        }
        
        private int _registrationAttempts = 0;

        public void OnUpdate()
        {
            // Try to register with the game if not yet registered
            if (!_registered && Time.time - _lastRegistrationAttempt > REGISTRATION_RETRY_INTERVAL)
            {
                _lastRegistrationAttempt = Time.time;
                _registrationAttempts++;
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
            
            // Log every 5th attempt so we know retries are happening
            if (_registrationAttempts % 5 == 0)
            {
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] Registration attempt #{_registrationAttempts}...");
            }
            
            if (HoldfastEventReceiver.Register())
            {
                _registered = true;
                _receiver = HoldfastEventReceiver.Instance;
                LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] ✓ Successfully registered with game!");

                // After registration, try to get Steam ID from Steamworks.NET and raise OnConnectedToServer.
                // OnIsClient may have already fired (and been missed), so we read it directly.
                // Only do this if we don't already know the Steam ID (first registration)
                // or if we just reset (server change - _localSteamId was cleared by ResetRegistration).
                if (HoldfastEventReceiver.LocalSteamId == 0)
                {
                    HoldfastEventReceiver.TryGetSteamIdFromSteamworks();
                }
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
        
        /// <summary>
        /// Tries to read the local Steam ID from Steamworks.NET via reflection.
        /// Called after registration if OnIsClient was missed (fired before we registered).
        /// </summary>
        public static void TryGetSteamIdFromSteamworks()
        {
            try
            {
                // Steamworks.NET is loaded by the game. Try: SteamUser.GetSteamID().m_SteamID
                Assembly steamworksAssembly = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "com.rlabrecque.steamworks.net" ||
                        asm.GetName().Name == "Steamworks.NET" ||
                        asm.GetName().Name.Contains("Steamworks"))
                    {
                        steamworksAssembly = asm;
                        break;
                    }
                }

                if (steamworksAssembly == null)
                {
                    // Steamworks types might be in Assembly-CSharp or a preloaded assembly
                    // Try to find SteamUser type in all loaded assemblies
                    Type steamUserType = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        steamUserType = asm.GetType("Steamworks.SteamUser");
                        if (steamUserType != null)
                        {
                            steamworksAssembly = asm;
                            break;
                        }
                    }

                    if (steamUserType == null)
                    {
                        LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] Steamworks.SteamUser type not found in any loaded assembly");
                        return;
                    }
                }

                Type suType = steamworksAssembly.GetType("Steamworks.SteamUser");
                if (suType == null)
                {
                    LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] Steamworks.SteamUser type not found");
                    return;
                }

                MethodInfo getSteamIdMethod = suType.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static);
                if (getSteamIdMethod == null)
                {
                    LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] SteamUser.GetSteamID method not found");
                    return;
                }

                object steamIdObj = getSteamIdMethod.Invoke(null, null);
                if (steamIdObj != null)
                {
                    // CSteamID has an m_SteamID field (ulong)
                    FieldInfo steamIdField = steamIdObj.GetType().GetField("m_SteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (steamIdField != null)
                    {
                        ulong steamId = (ulong)steamIdField.GetValue(steamIdObj);
                        if (steamId > 0)
                        {
                            _localSteamId = steamId;
                            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ Got Steam ID from Steamworks: {steamId}");
                            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                            GameEvents.RaiseConnectedToServer(steamId);
                            return;
                        }
                    }

                    // Alternative: try implicit conversion to ulong
                    try
                    {
                        ulong steamId = Convert.ToUInt64(steamIdObj);
                        if (steamId > 0)
                        {
                            _localSteamId = steamId;
                            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ Got Steam ID (converted): {steamId}");
                            LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                            GameEvents.RaiseConnectedToServer(steamId);
                            return;
                        }
                    }
                    catch { }
                }

                LauncherCoreModPlugin.Log?.LogInfo("[GameEvents] Could not extract Steam ID from Steamworks");
            }
            catch (Exception ex)
            {
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] Steamworks Steam ID lookup failed: {ex.Message}");
            }
        }

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
        
        /// <summary>
        /// Resets all registration state. Called when connecting to a new server
        /// so the old stale references don't prevent re-registration.
        /// </summary>
        public static void ResetRegistration()
        {
            Instance = null;
            _lastKnownInstancesList = null;
            _isClientConnected = false;
            _localPlayerId = -1;
            _localSteamId = 0; // Reset so TryGetSteamIdFromSteamworks will re-raise OnConnectedToServer
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
            
            // Track local player identification:
            // 1. If we have a known Steam ID, match against it (most reliable)
            // 2. NEVER match steamId==0 against _localSteamId==0 (this matches random players!)
            bool isLocalPlayer = false;
            string matchReason = "";

            if (_localSteamId > 0 && steamId == _localSteamId)
            {
                isLocalPlayer = true;
                matchReason = $"SteamId match ({steamId})";
            }
            else if (_localSteamId > 0 && steamId == 0 && !isBot)
            {
                // We have a real Steam ID but this player reports 0 - this could be us or anyone.
                // Don't match - wait for exact Steam ID match.
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Skipping steamId=0 player (we know our ID is {_localSteamId})");
            }
            else if (_localSteamId == 0 && steamId > 0 && !isBot)
            {
                // We don't know our Steam ID yet, and this player has a real one.
                // Can't determine if it's us. Skip.
            }
            // Note: _localSteamId==0 && steamId==0 is ambiguous - DON'T match (was the old bug)

            if (isLocalPlayer)
            {
                _localPlayerId = playerId;
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ═══════════════════════════════════════════");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents] ★ STEP 2: Local player identified!");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Player ID: {playerId}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Name: {name}");
                LauncherCoreModPlugin.Log?.LogInfo($"[GameEvents]   Matched via: {matchReason}");
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
        public void OnPlayerHurt(int playerId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { GameEvents.RaisePlayerHurt(playerId, oldHp, newHp, reason); }
        public void OnScorableAction(int playerId, int score, ScorableActionType reason) { }
        public void OnPlayerShoot(int playerId, bool dryShot) { }
        public void OnShotInfo(int playerId, int shotCount, Vector3[][] shotsPointsPositions, float[] trajectileDistances, float[] distanceFromFiringPositions, float[] horizontalDeviationAngles, float[] maxHorizontalDeviationAngles, float[] muzzleVelocities, float[] gravities, float[] damageHitBaseDamages, float[] damageRangeUnitValues, float[] damagePostTraitAndBuffValues, float[] totalDamages, Vector3[] hitPositions, Vector3[] hitDirections, int[] hitPlayerIds, int[] hitDamageableObjectIds, int[] hitShipIds, int[] hitVehicleIds) { }
        public void OnPlayerBlock(int attackingPlayerId, int defendingPlayerId) { GameEvents.RaisePlayerBlock(attackingPlayerId, defendingPlayerId); }
        public void OnPlayerMeleeStartSecondaryAttack(int playerId) { GameEvents.RaisePlayerMeleeStartSecondaryAttack(playerId); }
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
            var log = LauncherCoreModPlugin.Log;
            
            try
            {
                var paths = GetPossibleTokenPaths();
                log?.LogInfo($"[MasterLogin] Checking {paths.Length} token paths...");
                
                foreach (string tokenPath in paths)
                {
                    bool exists = File.Exists(tokenPath);
                    log?.LogInfo($"[MasterLogin]   Path: {tokenPath} | Exists: {exists}");
                    
                    if (exists)
                    {
                        string token = File.ReadAllText(tokenPath).Trim();
                        string preview = token.Length > 20 ? token.Substring(0, 20) + "..." : token;
                        bool valid = VerifySecureToken(token);
                        log?.LogInfo($"[MasterLogin]   Token preview: '{preview}' | Valid: {valid}");
                        
                        if (valid)
                        {
                            _isMasterLoggedIn = true;
                            log?.LogInfo($"[MasterLogin]   >>> MASTER LOGIN DETECTED from: {tokenPath}");
                            break;
                        }
                    }
                }
                
                log?.LogInfo($"[MasterLogin] Final result: IsMasterLoggedIn = {_isMasterLoggedIn}");
            }
            catch (Exception ex)
            {
                log?.LogError($"[MasterLogin] CheckToken error: {ex.Message}");
            }
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
    /// Removes official servers from the filtered data lists inside ClientLobbyManager
    /// so they never reach the virtualized LoopListView2 UI at all.
    /// Non-master users will only see community servers.
    /// </summary>
    public class ServerBrowserFilter
    {
        private static ManualLogSource _log => LauncherCoreModPlugin.Log;
        
        private static Harmony _harmony;
        private static bool _patchesApplied = false;
        
        private static FieldInfo _officialFilteredField;
        
        private const string CUSTOM_SERVER_BUTTON_PATH = "MainCanvas/Main Menu Panels/Panel Container/Play/Server Browser/Server Browser Container/Bottom Buttons Layout/Custom Server Button";
        private static bool _customServerButtonHidden = false;
        private static float _lastUiScan = 0f;
        private const float UI_SCAN_INTERVAL = 1f;
        
        public void Initialize()
        {
            _log?.LogInfo("[ServerBrowserFilter] Initializing data-level filter...");
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
                
                Type lobbyManagerType = FindType("ClientLobbyManager");
                if (lobbyManagerType == null)
                {
                    _log?.LogWarning("[ServerBrowserFilter] ClientLobbyManager type not found");
                    return;
                }
                
                _officialFilteredField = lobbyManagerType.GetField("officialServersListFiltered",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (_officialFilteredField == null)
                {
                    _log?.LogWarning("[ServerBrowserFilter] officialServersListFiltered field not found");
                    return;
                }
                
                MethodInfo updateMethod = lobbyManagerType.GetMethod("UpdateFilteredLists",
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (updateMethod != null)
                {
                    var postfix = typeof(ServerBrowserFilter).GetMethod(nameof(Postfix_UpdateFilteredLists),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    _harmony.Patch(updateMethod, postfix: new HarmonyMethod(postfix));
                    _patchesApplied = true;
                    _log?.LogInfo("[ServerBrowserFilter] Harmony postfix on ClientLobbyManager.UpdateFilteredLists applied");
                }
                else
                {
                    _log?.LogWarning("[ServerBrowserFilter] UpdateFilteredLists method not found");
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
        /// After the game rebuilds its filtered server lists, clear the official list
        /// for non-master users. TotalFilteredServers and ResolveServerInfoFiltered
        /// both read from this list, so the UI never sees them.
        /// </summary>
        private static void Postfix_UpdateFilteredLists(object __instance)
        {
            try
            {
                bool isMaster = MasterLoginManager.IsMasterLoggedIn();
                _log?.LogInfo($"[ServerBrowserFilter] Postfix called! IsMaster={isMaster}, FieldFound={_officialFilteredField != null}");
                
                if (isMaster)
                {
                    _log?.LogInfo("[ServerBrowserFilter] Master user - allowing all servers");
                    return;
                }
                if (_officialFilteredField == null)
                {
                    _log?.LogWarning("[ServerBrowserFilter] officialServersListFiltered field is null!");
                    return;
                }
                
                var list = _officialFilteredField.GetValue(__instance) as System.Collections.IList;
                _log?.LogInfo($"[ServerBrowserFilter] Official filtered list: {(list != null ? list.Count.ToString() + " servers" : "NULL")}");
                
                if (list != null && list.Count > 0)
                {
                    _log?.LogInfo($"[ServerBrowserFilter] CLEARING {list.Count} official servers for non-master user");
                    list.Clear();
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[ServerBrowserFilter] Postfix error: {ex.Message}");
            }
        }
        
        public void OnUpdate()
        {
            if (MasterLoginManager.IsMasterLoggedIn()) return;
            
            if (Time.time - _lastUiScan > UI_SCAN_INTERVAL)
            {
                _lastUiScan = Time.time;
                HideCustomServerButton();
            }
        }
        
        private void HideCustomServerButton()
        {
            if (_customServerButtonHidden) return;
            
            try
            {
                GameObject customBtn = GameObject.Find(CUSTOM_SERVER_BUTTON_PATH);
                if (customBtn != null)
                {
                    customBtn.SetActive(false);
                    _customServerButtonHidden = true;
                    _log?.LogInfo("[ServerBrowserFilter] Hidden Custom Server Button");
                }
            }
            catch { }
        }
    }
    
    #endregion
}
