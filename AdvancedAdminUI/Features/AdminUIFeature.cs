using System.Collections.Generic;

using UnityEngine;
using AdvancedAdminUI.Features;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Main admin UI that displays all features and allows toggling them
    /// Uses a simple WinForms window (no Windows API hooks) for second monitor support
    /// </summary>
    public class AdminUIFeature : IAdminFeature
    {
        public string FeatureName => "Admin UI";
        
        private bool _isEnabled = true; // UI is always enabled
        private bool _uiVisible = false; // Hidden by default - press F3 to show (prevents blocking game UI)
        private AdminWindow _adminWindow;
        private Dictionary<string, IAdminFeature> _features;
        
        public bool IsEnabled => _isEnabled;
        
        public void SetFeatures(Dictionary<string, IAdminFeature> features)
        {
            _features = features;
            if (_adminWindow != null)
            {
                _adminWindow.SetFeatures(features);
            }
        }

        public void Enable()
        {
            _isEnabled = true;
            
            // Subscribe to client connection changes to hide UI on disconnect
            AdvancedAdminUI.Utils.PlayerEventManager._onClientConnectionChangedCallbacks.Add(OnClientConnectionChanged);
            
            // Window is created lazily on first F3 press
        }

        public void Disable()
        {
            _isEnabled = false;
            _uiVisible = false;
            
            // Unsubscribe from events
            AdvancedAdminUI.Utils.PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            if (_adminWindow != null)
            {
                _adminWindow.HideWindow();
            }
        }
        
        /// <summary>
        /// Called when client connects to or disconnects from a server
        /// </summary>
        private void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Hide the UI on disconnect
                _uiVisible = false;
                if (_adminWindow != null)
                {
                    _adminWindow.HideWindow();
                }
                
                AdvancedAdminUIMod.Log.LogInfo("[AdminUI] Disconnected from server - hiding admin panel");
            }
        }

        public void OnUpdate()
        {
            // Toggle UI visibility and all mod features with F3
            if (Input.GetKeyDown(KeyCode.F3))
            {
                // RC login required to use the admin UI
                if (!AdvancedAdminUI.Utils.HoldfastScriptMod.IsRCLoggedIn())
                {
                    // Message is handled by the main mod to prevent duplicates
                    return;
                }
                
                bool isConnected = AdvancedAdminUI.Utils.HoldfastScriptMod.IsClientConnected();
                
                // Fallback: if OnIsClient wasn't called but we have players tracked, assume we're connected
                if (!isConnected && AdvancedAdminUI.Utils.PlayerTracker.GetPlayerCount() > 0)
                {
                    isConnected = true;
                }
                
                // Toggle visibility state
                _uiVisible = !_uiVisible;
                
                if (_uiVisible)
                {
                    // Opening panel - enable all features
                    if (!isConnected)
                    {
                        AdvancedAdminUIMod.Log.LogInfo("[AdminUI] Warning: Not connected to a server. Some features may not work correctly.");
                    }
                    
                    AdvancedAdminUIMod.Log.LogInfo("[AdminUI] Enabling all mod features...");
                    
                    // Enable all features (except AdminUI itself)
                    if (_features != null)
                    {
                        foreach (var kvp in _features)
                        {
                            if (kvp.Key != "adminui" && !kvp.Value.IsEnabled)
                            {
                                try
                                {
                                    kvp.Value.Enable();
                                    AdvancedAdminUIMod.Log.LogInfo($"[AdminUI] Enabled: {kvp.Value.FeatureName}");
                                }
                                catch (System.Exception ex)
                                {
                                    AdvancedAdminUIMod.Log.LogWarning($"[AdminUI] Error enabling {kvp.Value.FeatureName}: {ex.Message}");
                                }
                            }
                        }
                    }
                    
                    // Create window lazily on first F3 press
                    if (_adminWindow == null)
                    {
                        _adminWindow = new AdminWindow();
                        _adminWindow.SetFeatures(_features);
                    }
                    
                    _adminWindow.ShowWindow();
                }
                else
                {
                    // Closing panel - disable all features
                    AdvancedAdminUIMod.Log.LogInfo("[AdminUI] Disabling all mod features...");
                    
                    // Disable all features (except AdminUI itself)
                    if (_features != null)
                    {
                        foreach (var kvp in _features)
                        {
                            if (kvp.Key != "adminui" && kvp.Value.IsEnabled)
                            {
                                try
                                {
                                    kvp.Value.Disable();
                                    AdvancedAdminUIMod.Log.LogInfo($"[AdminUI] Disabled: {kvp.Value.FeatureName}");
                                }
                                catch (System.Exception ex)
                                {
                                    AdvancedAdminUIMod.Log.LogWarning($"[AdminUI] Error disabling {kvp.Value.FeatureName}: {ex.Message}");
                                }
                            }
                        }
                    }
                    
                    if (_adminWindow != null)
                    {
                        _adminWindow.HideWindow();
                    }
                }
            }
        }

        public void OnGUI()
        {
            // WinForms window renders on its own thread - nothing needed here
        }

        public void OnApplicationQuit()
        {
            // Clean up admin window
            if (_adminWindow != null)
            {
                _adminWindow.CloseWindow();
                _adminWindow = null;
            }
        }
        
        public bool IsUIVisible => _uiVisible;
    }
}

