using System;
using System.Collections.Generic;
using UnityEngine;
using AdvancedAdminUI.Utils;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Teleport feature - teleportation is now handled via drag-and-drop on the AdminWindow minimap
    /// This class provides the backend functionality for executing teleport commands
    /// </summary>
    public class TeleportFeature : IAdminFeature
    {
        public string FeatureName => "Teleport";
        public bool IsEnabled { get; private set; }
        
        // Local player tracking (for potential "teleport to me" functionality)
        private GameObject _localPlayerObject;
        private int _localPlayerId = -1;
        private Vector3 _playerPosition = Vector3.zero;
        private bool _localPlayerSearched = false;
        
        public void Enable()
        {
            IsEnabled = true;
            _localPlayerSearched = false;
            _localPlayerObject = null;
            
            // Subscribe to connection changes
            PlayerEventManager._onClientConnectionChangedCallbacks.Add(OnClientConnectionChanged);
            
            AdvancedAdminUIMod.Log.LogInfo("[Teleport] Feature enabled - use minimap drag-and-drop to teleport players");
        }
        
        public void Disable()
        {
            IsEnabled = false;
            
            // Unsubscribe from events
            PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            AdvancedAdminUIMod.Log.LogInfo("[Teleport] Feature disabled");
        }
        
        private void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Reset local player tracking
                _localPlayerObject = null;
                _localPlayerSearched = false;
                _localPlayerId = -1;
                _playerPosition = Vector3.zero;
            }
        }
        
        public void OnApplicationQuit()
        {
            // Clean up if needed
        }
        
        // Throttle updates to reduce lag
        private float _lastUpdateTime = 0f;
        private const float UPDATE_INTERVAL = 0.5f; // Only need occasional updates
        
        public void OnUpdate()
        {
            if (!IsEnabled) return;
            
            // Throttle updates - we only need player position occasionally
            float currentTime = Time.time;
            if (currentTime - _lastUpdateTime < UPDATE_INTERVAL)
                return;
            _lastUpdateTime = currentTime;
            
            // Update local player position (for potential "teleport to me" functionality)
            UpdateLocalPlayerPosition();
        }
        
        public void OnGUI()
        {
            // Teleport UI has been moved to the AdminWindow minimap
            // Drag-and-drop a player on the minimap to teleport them
            // This method is intentionally empty
        }
        
        private void UpdateLocalPlayerPosition()
        {
            if (_localPlayerObject == null || !_localPlayerObject.activeInHierarchy)
            {
                if (!_localPlayerSearched)
                {
                    _localPlayerSearched = true;
                    _localPlayerObject = FindLocalPlayer();
                }
                else if (_localPlayerObject == null)
                {
                    _localPlayerObject = FindLocalPlayerFromTracker();
                }
            }
            
            if (_localPlayerObject != null)
            {
                _playerPosition = _localPlayerObject.transform.position;
            }
        }
        
        private GameObject FindLocalPlayerFromTracker()
        {
            var players = PlayerTracker.GetPlayersReadOnly();
            foreach (var kvp in players)
            {
                var playerData = kvp.Value;
                if (playerData.PlayerObject != null && 
                    playerData.PlayerObject.name.Contains("Owner"))
                {
                    _localPlayerId = playerData.PlayerId;
                    return playerData.PlayerObject;
                }
            }
            return null;
        }
        
        private GameObject FindLocalPlayer()
        {
            // First try PlayerTracker (cheap)
            var result = FindLocalPlayerFromTracker();
            if (result != null) return result;
            
            // Fallback: look for main camera parent
            if (Camera.main != null)
            {
                var parent = Camera.main.transform.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("Player"))
                    {
                        return parent.gameObject;
                    }
                    parent = parent.parent;
                }
            }
            
            // Last resort: Find by name
            var localPlayer = GameObject.Find("Player - Owner");
            if (localPlayer != null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(localPlayer.name, @"#(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                {
                    _localPlayerId = id;
                }
                return localPlayer;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get local player position (for "teleport to me" functionality)
        /// </summary>
        public Vector3 GetLocalPlayerPosition()
        {
            return _playerPosition;
        }
        
        /// <summary>
        /// Get local player ID
        /// </summary>
        public int GetLocalPlayerId()
        {
            return _localPlayerId;
        }
    }
}
