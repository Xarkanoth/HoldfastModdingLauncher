using System;
using System.Linq;
using System.Reflection;

using UnityEngine;
using HoldfastSharedMethods;
using HarmonyLib;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// Harmony patches to capture admin events BEFORE mod loader registration
    /// These patch the game's internal event dispatcher to catch OnPlayerConnected and OnRCLogin early
    /// </summary>
    public static class AdminEventPatches
    {
        private static Harmony _harmony;
        private static bool _patchesApplied = false;
        
        // Store captured auto-admin status before we're fully registered
        public static bool CapturedAutoAdmin { get; private set; } = false;
        public static int CapturedAutoAdminPlayerId { get; private set; } = -1;
        public static bool CapturedRCLogin { get; private set; } = false;
        public static int CapturedRCLoginPlayerId { get; private set; } = -1;
        
        public static void ApplyPatches()
        {
            if (_patchesApplied) return;
            
            try
            {
                _harmony = new Harmony("com.xarkanoth.advancedadminui.adminevents");
                
                // Find the ClientModLoaderManager class which dispatches events to mods
                Assembly assemblyCSharp = Assembly.Load("Assembly-CSharp");
                
                // Try to find method that calls OnPlayerConnected on mods
                Type clientModLoaderType = assemblyCSharp.GetType("HoldfastGame.ClientModLoaderManager");
                if (clientModLoaderType == null)
                {
                    AdvancedAdminUIMod.Log.LogInfo("[AdminEventPatches] ClientModLoaderManager not found - trying alternative types");
                    
                    // List types that might handle events
                    foreach (Type t in assemblyCSharp.GetTypes())
                    {
                        string name = t.Name.ToLower();
                        if (name.Contains("modloader") || name.Contains("rclogin") || name.Contains("admin"))
                        {
                            AdvancedAdminUIMod.Log.LogInfo($"[AdminEventPatches] Found potential type: {t.FullName}");
                        }
                    }
                }
                else
                {
                    AdvancedAdminUIMod.Log.LogInfo("[AdminEventPatches] Found ClientModLoaderManager - looking for event methods");
                    
                    // Look for methods that dispatch OnPlayerConnected or OnRCLogin
                    foreach (MethodInfo method in clientModLoaderType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        string methodName = method.Name.ToLower();
                        if (methodName.Contains("playerconnected") || methodName.Contains("rclogin") || methodName.Contains("autoadmin"))
                        {
                            AdvancedAdminUIMod.Log.LogInfo($"[AdminEventPatches] Found method: {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                        }
                    }
                }
                
                // Try to find and patch the RC login handler directly
                Type[] allTypes = assemblyCSharp.GetTypes();
                foreach (Type t in allTypes)
                {
                    if (t.Name.Contains("RC") || t.Name.Contains("Admin") || t.Name.Contains("Console"))
                    {
                        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        {
                            string mName = m.Name.ToLower();
                            if (mName.Contains("login") || mName.Contains("autoadmin") || mName.Contains("onplayerconnected"))
                            {
                                AdvancedAdminUIMod.Log.LogInfo($"[AdminEventPatches] Potential patch target: {t.Name}.{m.Name}");
                                
                                if (mName.Contains("login") && m.GetParameters().Length >= 2)
                                {
                                    try
                                    {
                                        var postfix = new HarmonyMethod(typeof(AdminEventPatches).GetMethod(nameof(OnRCLoginPostfix), BindingFlags.Static | BindingFlags.NonPublic));
                                        _harmony.Patch(m, postfix: postfix);
                                        AdvancedAdminUIMod.Log.LogInfo($"[AdminEventPatches] ✓ Patched {t.Name}.{m.Name}");
                                    }
                                    catch (Exception ex)
                                    {
                                        AdvancedAdminUIMod.Log.LogInfo($"[AdminEventPatches] Could not patch {t.Name}.{m.Name}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                
                _patchesApplied = true;
                AdvancedAdminUIMod.Log.LogInfo("[AdminEventPatches] Patch exploration complete");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[AdminEventPatches] Error applying patches: {ex.Message}");
            }
        }
        
        private static void OnRCLoginPostfix(object[] __args)
        {
            try
            {
                ColoredLogger.Log(ColoredLogger.BrightYellow, $"[AdminEventPatches] ★ Captured login method call with {__args?.Length ?? 0} args");
                
                // Try to extract player ID from args and set the captured flag
                if (__args != null && __args.Length >= 1)
                {
                    if (__args[0] is int playerId)
                    {
                        CapturedRCLogin = true;
                        CapturedRCLoginPlayerId = playerId;
                        ColoredLogger.Log(ColoredLogger.BrightGreen, $"[AdminEventPatches] Captured RC login for player ID: {playerId}");
                    }
                }
            }
            catch { }
        }
        
        public static void CheckCapturedAdminStatus()
        {
            if (CapturedAutoAdmin && CapturedAutoAdminPlayerId >= 0)
            {
                ColoredLogger.Log(ColoredLogger.BrightGreen, $"[AdminEventPatches] Found captured auto-admin status for player {CapturedAutoAdminPlayerId}");
                GameEventBridge.SetAutoAdminFromCapture(CapturedAutoAdminPlayerId);
            }
            
            if (CapturedRCLogin && CapturedRCLoginPlayerId >= 0)
            {
                ColoredLogger.Log(ColoredLogger.BrightGreen, $"[AdminEventPatches] Found captured RC login for player {CapturedRCLoginPlayerId}");
                GameEventBridge.SetRCLoginFromCapture(CapturedRCLoginPlayerId);
            }
        }
        
        public static void Reset()
        {
            CapturedAutoAdmin = false;
            CapturedAutoAdminPlayerId = -1;
            CapturedRCLogin = false;
            CapturedRCLoginPlayerId = -1;
        }
    }

    /// <summary>
    /// Bridge class that subscribes to LauncherCoreMod's GameEvents and forwards them to AdvancedAdminUI's PlayerEventManager.
    /// This replaces the old IHoldfastSharedMethods implementation approach.
    /// </summary>
    public static class GameEventBridge
    {
        private static bool _isInitialized = false;
        private static bool _isClientConnected = false;
        private static ulong _localSteamId = 0;
        private static int _localPlayerId = -1;
        
        // RC Login tracking
        private static bool _isRCLoggedIn = false;
        private static int _pendingAutoAdminPlayerId = -1;
        
        // Master login
        private static bool _masterLoginChecked = false;
        private static bool _hasMasterLogin = false;
        private const string MASTER_TOKEN_FILE = "master_login.token";
        
        // Assembly reference for LauncherCoreMod
        private static Assembly _coreModAssembly;
        private static Type _gameEventsType;
        private static Type _masterLoginType;
        private static Type _eventReceiverType;
        
        /// <summary>
        /// Initialize the bridge - subscribe to LauncherCoreMod's events
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            
            try
            {
                // Load LauncherCoreMod assembly
                _coreModAssembly = Assembly.Load("LauncherCoreMod");
                if (_coreModAssembly == null)
                {
                    AdvancedAdminUIMod.Log.LogWarning("[GameEventBridge] LauncherCoreMod assembly not found!");
                    return;
                }
                
                _gameEventsType = _coreModAssembly.GetType("LauncherCoreMod.GameEvents");
                _masterLoginType = _coreModAssembly.GetType("LauncherCoreMod.MasterLoginManager");
                _eventReceiverType = _coreModAssembly.GetType("LauncherCoreMod.HoldfastEventReceiver");
                
                if (_gameEventsType == null)
                {
                    AdvancedAdminUIMod.Log.LogWarning("[GameEventBridge] GameEvents type not found!");
                    return;
                }
                
                // Subscribe to events
                SubscribeToEvent("OnConnectedToServer", nameof(HandleConnectedToServer));
                SubscribeToEvent("OnDisconnectedFromServer", nameof(HandleDisconnectedFromServer));
                SubscribeToEvent("OnPlayerJoined", nameof(HandlePlayerJoined));
                SubscribeToEvent("OnPlayerLeft", nameof(HandlePlayerLeft));
                SubscribeToEvent("OnPlayerSpawned", nameof(HandlePlayerSpawned));
                SubscribeToEvent("OnPlayerPacket", nameof(HandlePlayerPacket));
                SubscribeToEvent("OnPlayerKilledPlayer", nameof(HandlePlayerKilledPlayer));
                SubscribeToEvent("OnRoundDetails", nameof(HandleRoundDetails));
                SubscribeToEvent("OnRoundEndFactionWinner", nameof(HandleRoundEndFactionWinner));
                SubscribeToEvent("OnRCLogin", nameof(HandleRCLogin));
                SubscribeToEvent("OnPlayerConnected", nameof(HandlePlayerConnected));
                
                // Check master login
                CheckMasterLogin();
                
                _isInitialized = true;
                AdvancedAdminUIMod.Log.LogInfo("[GameEventBridge] ✓ Subscribed to LauncherCoreMod events");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[GameEventBridge] Error initializing: {ex.Message}");
            }
        }
        
        private static void SubscribeToEvent(string eventName, string handlerMethodName)
        {
            try
            {
                var eventInfo = _gameEventsType.GetEvent(eventName);
                if (eventInfo == null)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[GameEventBridge] Event not found: {eventName}");
                    return;
                }
                
                var handlerMethod = typeof(GameEventBridge).GetMethod(handlerMethodName, 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (handlerMethod == null)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[GameEventBridge] Handler not found: {handlerMethodName}");
                    return;
                }
                
                var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, handlerMethod);
                eventInfo.AddEventHandler(null, handler);
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[GameEventBridge] Failed to subscribe to {eventName}: {ex.Message}");
            }
        }
        
        // ==========================================
        // Event Handlers - Forward to PlayerEventManager
        // ==========================================
        
        private static void HandleConnectedToServer(ulong steamId)
        {
            _isClientConnected = true;
            _localSteamId = steamId;
            
            ColoredLogger.Log(ColoredLogger.BrightCyan, "═══════════════════════════════════════════");
            ColoredLogger.Log(ColoredLogger.BrightCyan, "  CONNECTED TO SERVER");
            ColoredLogger.Log(ColoredLogger.BrightCyan, $"  SteamId: {steamId}");
            ColoredLogger.Log(ColoredLogger.BrightCyan, "═══════════════════════════════════════════");
            
            PlayerEventManager.OnClientConnectionChanged(true);
        }
        
        private static void HandleDisconnectedFromServer()
        {
            bool wasLoggedIn = _isRCLoggedIn;
            _isClientConnected = false;
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
            
            AdvancedAdminUIMod.Instance?.ResetRCLoginPromptFlag();
            PlayerEventManager.OnClientConnectionChanged(false);
        }
        
        private static void HandlePlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            // Track our local player ID by matching Steam ID
            if (steamId == _localSteamId && !isBot)
            {
                _localPlayerId = playerId;
                ColoredLogger.Log(ColoredLogger.BrightCyan, $"[GameEventBridge] ★ LOCAL PLAYER IDENTIFIED: Id={playerId}, Name={name}");
                
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
            
            PlayerEventManager.OnPlayerJoined(playerId, steamId, name, regimentTag, isBot);
        }
        
        private static void HandlePlayerLeft(int playerId)
        {
            PlayerEventManager.OnPlayerLeft(playerId);
        }
        
        private static void HandlePlayerSpawned(int playerId, int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            PlayerEventManager.OnPlayerSpawned(playerId, spawnSectionId, faction, playerClass, uniformId, playerObject);
        }
        
        private static void HandlePlayerPacket(int playerId, Vector3 position, Vector3 rotation)
        {
            PlayerEventManager.OnPlayerPacket(playerId, position, rotation);
        }
        
        private static void HandlePlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details)
        {
            PlayerEventManager.OnPlayerKilledPlayer(killerPlayerId, victimPlayerId, reason, details);
        }
        
        private static void HandleRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            PlayerEventManager.OnRoundDetails(roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType);
        }
        
        private static void HandleRoundEndFactionWinner(FactionCountry faction, FactionRoundWinnerReason reason)
        {
            PlayerEventManager.OnRoundEndFactionWinner(faction, reason);
        }
        
        private static void HandleRCLogin(int playerId, bool isLoggedIn)
        {
            ColoredLogger.Log(ColoredLogger.BrightYellow, "═══════════════════════════════════════════");
            ColoredLogger.Log(ColoredLogger.BrightYellow, $"  ★ OnRCLogin EVENT RECEIVED ★");
            ColoredLogger.Log(ColoredLogger.BrightYellow, $"  playerId={playerId}, isLoggedIn={isLoggedIn}");
            ColoredLogger.Log(ColoredLogger.BrightYellow, $"  _localPlayerId={_localPlayerId}");
            ColoredLogger.Log(ColoredLogger.BrightYellow, "═══════════════════════════════════════════");
            
            if (isLoggedIn)
            {
                _isRCLoggedIn = true;
                _localPlayerId = playerId;
                
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
        
        private static void HandlePlayerConnected(int playerId, bool isAutoAdmin, string backendId)
        {
            ColoredLogger.Log(ColoredLogger.BrightYellow, $"[GameEventBridge] ★ OnPlayerConnected: playerId={playerId}, isAutoAdmin={isAutoAdmin}");
            
            if (isAutoAdmin)
            {
                _pendingAutoAdminPlayerId = playerId;
                ColoredLogger.Log(ColoredLogger.BrightMagenta, "═══════════════════════════════════════════");
                ColoredLogger.Log(ColoredLogger.BrightMagenta, $"  ★ AUTO-ADMIN DETECTED: PlayerId={playerId}");
                ColoredLogger.Log(ColoredLogger.BrightMagenta, "═══════════════════════════════════════════");
            }
        }
        
        // ==========================================
        // Public API
        // ==========================================
        
        /// <summary>Check if client is connected to a server</summary>
        public static bool IsClientConnected() => _isClientConnected;
        
        /// <summary>Check if the local player has RC login or master login</summary>
        public static bool IsRCLoggedIn()
        {
            if (!_masterLoginChecked)
            {
                _masterLoginChecked = true;
                CheckMasterLogin();
            }
            return _isRCLoggedIn || _hasMasterLogin;
        }
        
        /// <summary>Check if master login is active</summary>
        public static bool HasMasterLogin()
        {
            if (!_masterLoginChecked)
            {
                _masterLoginChecked = true;
                CheckMasterLogin();
            }
            return _hasMasterLogin;
        }
        
        /// <summary>Get local player ID</summary>
        public static int GetLocalPlayerId() => _localPlayerId;
        
        /// <summary>Called when auto-admin was captured before events were connected</summary>
        public static void SetAutoAdminFromCapture(int playerId)
        {
            _pendingAutoAdminPlayerId = playerId;
            AdvancedAdminUIMod.Log.LogInfo($"[GameEventBridge] Received captured auto-admin for player {playerId}");
            
            if (_localPlayerId >= 0 && _localPlayerId == playerId)
            {
                _isRCLoggedIn = true;
                ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
                ColoredLogger.Log(ColoredLogger.BrightGreen, "  ✓ AUTO-ADMIN DETECTED (from capture)");
                ColoredLogger.Log(ColoredLogger.BrightGreen, "    Advanced Admin UI ENABLED");
                ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
            }
        }
        
        /// <summary>Called when RC login was captured before events were connected</summary>
        public static void SetRCLoginFromCapture(int playerId)
        {
            _isRCLoggedIn = true;
            _localPlayerId = playerId;
            ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
            ColoredLogger.Log(ColoredLogger.BrightGreen, "  ✓ RC LOGIN DETECTED (from capture)");
            ColoredLogger.Log(ColoredLogger.BrightGreen, "    Advanced Admin UI ENABLED");
            ColoredLogger.Log(ColoredLogger.BrightGreen, "═══════════════════════════════════════════");
        }
        
        private static void CheckMasterLogin()
        {
            try
            {
                // First try LauncherCoreMod's MasterLoginManager
                if (_masterLoginType != null)
                {
                    var isMasterLoggedInMethod = _masterLoginType.GetMethod("IsMasterLoggedIn", 
                        BindingFlags.Public | BindingFlags.Static);
                    if (isMasterLoggedInMethod != null)
                    {
                        _hasMasterLogin = (bool)isMasterLoggedInMethod.Invoke(null, null);
                        if (_hasMasterLogin)
                        {
                            ColoredLogger.Log(ColoredLogger.BrightMagenta, "═══════════════════════════════════════════");
                            ColoredLogger.Log(ColoredLogger.BrightMagenta, "  ★ MASTER LOGIN ACTIVE");
                            ColoredLogger.Log(ColoredLogger.BrightMagenta, "    RC login bypassed on all servers");
                            ColoredLogger.Log(ColoredLogger.BrightMagenta, "    Press F3 to open admin panel");
                            ColoredLogger.Log(ColoredLogger.BrightMagenta, "═══════════════════════════════════════════");
                            return;
                        }
                    }
                }
                
                // Fallback: Check for token file directly
                string dllFolder = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string gameRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..");
                
                var searchPaths = new System.Collections.Generic.List<string>
                {
                    System.IO.Path.Combine(dllFolder, MASTER_TOKEN_FILE),
                    System.IO.Path.Combine(dllFolder, "Mods", MASTER_TOKEN_FILE),
                    System.IO.Path.Combine(gameRoot, MASTER_TOKEN_FILE),
                    System.IO.Path.Combine(gameRoot, "BepInEx", "plugins", MASTER_TOKEN_FILE),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                        "HoldfastModding", MASTER_TOKEN_FILE)
                };
                
                AdvancedAdminUIMod.Log.LogInfo($"[GameEventBridge] Checking for master login token in {searchPaths.Count} locations...");
                
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
                                AdvancedAdminUIMod.Log.LogInfo($"[GameEventBridge] Master login token found at: {path}");
                                return;
                            }
                        }
                    }
                    catch { }
                }
                
                AdvancedAdminUIMod.Log.LogInfo("[GameEventBridge] No master login token found");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[GameEventBridge] Error checking master login: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Legacy compatibility class - forwards calls to GameEventBridge
    /// This maintains API compatibility with features that still use HoldfastScriptMod.IsRCLoggedIn() etc.
    /// </summary>
    public static class HoldfastScriptMod
    {
        public static bool IsClientConnected() => GameEventBridge.IsClientConnected();
        public static bool IsRCLoggedIn() => GameEventBridge.IsRCLoggedIn();
        public static bool HasMasterLogin() => GameEventBridge.HasMasterLogin();
        
        public static void SetAutoAdminFromCapture(int playerId) => GameEventBridge.SetAutoAdminFromCapture(playerId);
        public static void SetRCLoginFromCapture(int playerId) => GameEventBridge.SetRCLoginFromCapture(playerId);
        
        // Legacy registration methods - now handled by LauncherCoreMod
        public static bool Register()
        {
            // Registration is now handled by LauncherCoreMod
            // Just initialize our bridge to subscribe to events
            GameEventBridge.Initialize();
            return true;
        }
        
        public static void ForceReRegister()
        {
            // No longer needed - LauncherCoreMod handles registration
            AdvancedAdminUIMod.Log.LogInfo("[HoldfastScriptMod] ForceReRegister called - now handled by LauncherCoreMod");
        }
        
        public static bool NeedsReRegistration() => false;
        public static void ClearReRegistrationFlag() { }
        public static bool IsStillRegistered() => true;
    }
}
