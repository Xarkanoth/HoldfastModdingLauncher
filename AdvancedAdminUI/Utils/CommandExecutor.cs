using System;

using UnityEngine;
using UnityEngine.UI;

namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// Simple utility class for executing Holdfast console commands
    /// Finds the Game Console Panel InputField and invokes commands through it
    /// </summary>
    public static class CommandExecutor
    {
        private const string CONSOLE_PANEL_NAME = "Game Console Panel";
        private static InputField _inputField = null;
        private static bool _initialized = false;
        
        /// <summary>
        /// Execute a console command (e.g., "rc login password" or "rc carbonPlayers spawnSpecific British Rifleman")
        /// </summary>
        public static void InvokeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;
            
            try
            {
                if (!_initialized || _inputField == null)
                {
                    Initialize();
                }
                
                if (_inputField == null)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[CommandExecutor] Console InputField not found - cannot execute: {command}");
                    return;
                }
                
                _inputField.onEndEdit.Invoke(command);
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[CommandExecutor] Error executing command '{command}': {ex.Message}");
            }
        }
        
        /// <summary>
        /// Legacy method name for backwards compatibility
        /// </summary>
        public static bool ExecuteCommand(string command)
        {
            InvokeCommand(command);
            return _inputField != null;
        }
        
        /// <summary>
        /// Check if the console InputField has been found
        /// </summary>
        public static bool IsReady => _inputField != null;
        
        /// <summary>
        /// Force re-initialization (useful if console wasn't loaded yet)
        /// </summary>
        public static void Reinitialize()
        {
            _initialized = false;
            _inputField = null;
            Initialize();
        }
        
        private static void Initialize()
        {
            if (_initialized && _inputField != null)
                return;
            
            _initialized = true;
            _inputField = FindConsoleInputField();
            
            if (_inputField != null)
            {
                AdvancedAdminUIMod.Log.LogInfo("[CommandExecutor] ✓ Console InputField found and ready");
            }
        }
        
        private static InputField FindConsoleInputField()
        {
            try
            {
                // Search all canvases (including inactive ones) for the Game Console Panel
                Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
                
                foreach (Canvas canvas in canvases)
                {
                    if (canvas == null)
                        continue;
                    
                    // Find the one named "Game Console Panel"
                    if (!string.Equals(canvas.name, CONSOLE_PANEL_NAME, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    // Get the InputField from within this panel
                    InputField inputField = canvas.GetComponentInChildren<InputField>(true);
                    
                    if (inputField != null)
                    {
                        AdvancedAdminUIMod.Log.LogInfo($"[CommandExecutor] Found Game Console Panel InputField");
                        return inputField;
                    }
                }
                
                AdvancedAdminUIMod.Log.LogWarning("[CommandExecutor] Game Console Panel not found - console may not be loaded yet");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[CommandExecutor] Error finding console: {ex.Message}");
            }
            
            return null;
        }
    }
}
