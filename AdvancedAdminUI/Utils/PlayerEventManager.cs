using System;
using System.Collections.Generic;

using UnityEngine;
using HoldfastSharedMethods;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// PlayerEventManager that exposes public static callback lists
    /// Similar to the pattern used in SpawnHookMod example
    /// Features can directly subscribe: PlayerEventManager._onPlayerSpawnedCallbacks.Add(OnPlayerSpawned);
    /// Events are received from HoldfastScriptMod which implements IHoldfastSharedMethods
    /// </summary>
    public static class PlayerEventManager
    {
        // Public static callback list - can be directly accessed like in the example
        public static List<Action<int, GameObject>> _onPlayerSpawnedCallbacks = new List<Action<int, GameObject>>();
        
        // Extended callback list with full spawn data
        public static List<Action<int, int, object, object, int, GameObject>> _onPlayerSpawnedExtendedCallbacks = new List<Action<int, int, object, object, int, GameObject>>();
        
        // Callback list for player killed events
        public static List<Action<int, int, EntityHealthChangedReason, string>> _onPlayerKilledPlayerCallbacks = new List<Action<int, int, EntityHealthChangedReason, string>>();
        
        // Callback list for round details events (new round started)
        public static List<Action<int, string, string, FactionCountry, FactionCountry, GameplayMode, GameType>> _onRoundDetailsCallbacks = new List<Action<int, string, string, FactionCountry, FactionCountry, GameplayMode, GameType>>();
        
        // Callback list for player joined events
        public static List<Action<int, ulong, string, string, bool>> _onPlayerJoinedCallbacks = new List<Action<int, ulong, string, string, bool>>();
        
        // Callback list for player left events
        public static List<Action<int>> _onPlayerLeftCallbacks = new List<Action<int>>();
        
        // Callback list for round end events (same cleanup as OnRoundDetails)
        public static List<Action<FactionCountry, FactionRoundWinnerReason>> _onRoundEndFactionWinnerCallbacks = new List<Action<FactionCountry, FactionRoundWinnerReason>>();
        public static List<Action<int>> _onRoundEndPlayerWinnerCallbacks = new List<Action<int>>();
        
        // Callback list for player position packets (IHoldfastSharedMethods3)
        public static List<Action<int, Vector3, Vector3>> _onPlayerPacketCallbacks = new List<Action<int, Vector3, Vector3>>();
        
        // Callback list for client connection state changes (connected/disconnected from server)
        // bool = isConnected (true = joined server, false = left server)
        public static List<Action<bool>> _onClientConnectionChangedCallbacks = new List<Action<bool>>();

        private static bool _initialized = false;
        
        // Throttle packet logging to avoid spam
        private static float _lastPacketLogTime = 0f;
        private static int _packetCount = 0;

        /// <summary>
        /// Initialize the PlayerEventManager
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
        }
        
        /// <summary>
        /// Clears all registered callbacks. MUST be called before hot reload to prevent duplicate registrations.
        /// </summary>
        public static void ClearAllCallbacks()
        {
            _onPlayerSpawnedCallbacks.Clear();
            _onPlayerSpawnedExtendedCallbacks.Clear();
            _onPlayerKilledPlayerCallbacks.Clear();
            _onRoundDetailsCallbacks.Clear();
            _onPlayerJoinedCallbacks.Clear();
            _onPlayerLeftCallbacks.Clear();
            _onRoundEndFactionWinnerCallbacks.Clear();
            _onRoundEndPlayerWinnerCallbacks.Clear();
            _onPlayerPacketCallbacks.Clear();
            _onClientConnectionChangedCallbacks.Clear();
            _initialized = false;
        }

        /// <summary>
        /// Called by HoldfastScriptMod when OnPlayerSpawned is received from Holdfast
        /// </summary>
        internal static void OnPlayerSpawned(int playerId, int spawnSectionId, object playerFaction, object playerClass, int uniformId, GameObject playerObject)
        {
            if (playerObject == null)
                return;

            // Invoke extended callbacks with full data first
            foreach (var callback in _onPlayerSpawnedExtendedCallbacks)
            {
                try
                {
                    callback(playerId, spawnSectionId, playerFaction, playerClass, uniformId, playerObject);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnPlayerSpawned extended callback: {ex.Message}");
                }
            }

            // Invoke simplified callbacks (playerId, GameObject) for backward compatibility
            foreach (var callback in _onPlayerSpawnedCallbacks)
            {
                try
                {
                    callback(playerId, playerObject);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnPlayerSpawned callback: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnPlayerKilledPlayer is received from Holdfast
        /// </summary>
        internal static void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details)
        {
            // Invoke all registered callbacks
            foreach (var callback in _onPlayerKilledPlayerCallbacks)
            {
                try
                {
                    callback(killerPlayerId, victimPlayerId, reason, details);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnPlayerKilledPlayer callback: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnRoundDetails is received from Holdfast
        /// </summary>
        internal static void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            // Invoke all registered callbacks
            foreach (var callback in _onRoundDetailsCallbacks)
            {
                try
                {
                    callback(roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnRoundDetails callback: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnPlayerJoined is received from Holdfast
        /// </summary>
        internal static void OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            // Invoke all registered callbacks
            foreach (var callback in _onPlayerJoinedCallbacks)
            {
                try
                {
                    callback(playerId, steamId, name, regimentTag, isBot);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnPlayerJoined callback: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnPlayerLeft is received from Holdfast
        /// </summary>
        internal static void OnPlayerLeft(int playerId)
        {
            // Invoke all registered callbacks
            foreach (var callback in _onPlayerLeftCallbacks)
            {
                try
                {
                    callback(playerId);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnPlayerLeft callback: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnRoundEndFactionWinner is received from Holdfast
        /// Does NOT trigger cleanup - that happens when the NEW round starts (OnRoundDetails)
        /// This prevents massive lag from destroying 200+ GameObjects at once
        /// </summary>
        internal static void OnRoundEndFactionWinner(FactionCountry factionCountry, FactionRoundWinnerReason reason)
        {
            // Invoke all registered callbacks
            foreach (var callback in _onRoundEndFactionWinnerCallbacks)
            {
                try
                {
                    callback(factionCountry, reason);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnRoundEndFactionWinner callback: {ex.Message}");
                }
            }
            
            // DON'T trigger OnRoundDetails here - cleanup will happen on new round start
            // This prevents lag spike from destroying many objects at once
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnRoundEndPlayerWinner is received from Holdfast
        /// Does NOT trigger cleanup - that happens when the NEW round starts (OnRoundDetails)
        /// This prevents massive lag from destroying 200+ GameObjects at once
        /// </summary>
        internal static void OnRoundEndPlayerWinner(int playerId)
        {
            // Invoke all registered callbacks
            foreach (var callback in _onRoundEndPlayerWinnerCallbacks)
            {
                try
                {
                    callback(playerId);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnRoundEndPlayerWinner callback: {ex.Message}");
                }
            }
            
            // DON'T trigger OnRoundDetails here - cleanup will happen on new round start
            // This prevents lag spike from destroying many objects at once
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnPlayerPacket is received from Holdfast (IHoldfastSharedMethods3)
        /// This provides real-time player position and rotation data - works for all players including late joiners!
        /// </summary>
        internal static void OnPlayerPacket(int playerId, Vector3 position, Vector3 rotation)
        {
            _packetCount++;
            
            // Log occasionally to confirm packets are being received
            if (Time.time - _lastPacketLogTime > 10f)
            {
                AdvancedAdminUIMod.Log.LogInfo($"[PlayerEventManager] Received {_packetCount} player packets in last 10 seconds");
                _lastPacketLogTime = Time.time;
                _packetCount = 0;
            }
            
            // Update PlayerTracker with the position data
            PlayerTracker.UpdatePlayerPosition(playerId, position, rotation);
            
            // Invoke all registered callbacks
            foreach (var callback in _onPlayerPacketCallbacks)
            {
                try
                {
                    callback(playerId, position, rotation);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnPlayerPacket callback: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Called by HoldfastScriptMod when OnIsClient is received from Holdfast
        /// Notifies all features when the client connects to or disconnects from a server
        /// </summary>
        internal static void OnClientConnectionChanged(bool isConnected)
        {
            string status = isConnected ? "CONNECTED to server" : "DISCONNECTED from server";
            AdvancedAdminUIMod.Log.LogInfo($"[PlayerEventManager] Client {status}");
            
            // Invoke all registered callbacks
            foreach (var callback in _onClientConnectionChangedCallbacks)
            {
                try
                {
                    callback(isConnected);
                }
                catch (Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[PlayerEventManager] Error in OnClientConnectionChanged callback: {ex.Message}");
                }
            }
        }
    }
}

