using System;
using System.Collections.Generic;

using UnityEngine;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Feature to execute RC (Remote Console) commands
    /// </summary>
    public class RCCommandsFeature : IAdminFeature
    {
        public string FeatureName => "RC Commands";
        public bool IsEnabled { get; private set; }
        
        private bool _showUI = false;
        private Rect _windowRect = new Rect(100, 300, 500, 400);
        private string _commandInput = "";
        private Vector2 _scrollPosition = Vector2.zero;
        private List<string> _commandHistory = new List<string>();
        private List<string> _outputLog = new List<string>();
        private const int MAX_LOG_LINES = 100;
        
        public void Enable()
        {
            IsEnabled = true;
            AddLog("RC Commands ready. Use the game console (~ key) first if commands don't work.");
        }
        
        public void Disable()
        {
            IsEnabled = false;
            _showUI = false;
        }
        
        public void OnUpdate()
        {
            if (!IsEnabled)
                return;
                
            // Toggle UI with F4
            if (Input.GetKeyDown(KeyCode.F4))
            {
                _showUI = !_showUI;
            }
        }
        
        public void OnGUI()
        {
            if (!IsEnabled || !_showUI)
                return;
                
            _windowRect = GUILayout.Window(54321, _windowRect, DrawRCWindow, "RC Commands (F4 to close)", GUILayout.Width(500), GUILayout.Height(400));
            
            // Clamp window to screen
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }
        
        private void DrawRCWindow(int windowID)
        {
            GUILayout.BeginVertical();
            
            // Header
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 16;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label("Remote Console Commands", headerStyle);
            
            GUILayout.Space(5);
            
            // Status indicator
            bool isReady = AdvancedAdminUI.Utils.CommandExecutor.IsReady;
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 11;
            statusStyle.normal.textColor = isReady ? Color.green : Color.yellow;
            GUILayout.Label(isReady ? "● Console Ready" : "○ Console not found (open game console with ~ key)", statusStyle);
            
            GUILayout.Space(10);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(5);
            
            // Output log (scrollable)
            GUILayout.Label("Output:", GUILayout.Height(20));
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
            
            GUIStyle logStyle = new GUIStyle(GUI.skin.label);
            logStyle.fontSize = 11;
            logStyle.wordWrap = true;
            logStyle.normal.textColor = Color.white;
            
            if (_outputLog.Count == 0)
            {
                GUILayout.Label("No commands executed yet.", logStyle);
            }
            else
            {
                foreach (string line in _outputLog)
                {
                    GUILayout.Label(line, logStyle);
                }
            }
            
            GUILayout.EndScrollView();
            
            GUILayout.Space(5);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(5);
            
            // Command input
            GUILayout.BeginHorizontal();
            GUILayout.Label("Command:", GUILayout.Width(70));
            
            GUI.SetNextControlName("RCCommandInput");
            _commandInput = GUILayout.TextField(_commandInput, GUILayout.ExpandWidth(true));
            
            // Execute button
            GUI.enabled = !string.IsNullOrWhiteSpace(_commandInput);
            if (GUILayout.Button("Execute", GUILayout.Width(80)))
            {
                ExecuteCommand(_commandInput);
                _commandInput = "";
                GUI.FocusControl("RCCommandInput");
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            // Command history
            if (_commandHistory.Count > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label("Recent Commands:", GUILayout.Height(20));
                
                // Show last 5 commands
                int startIndex = Mathf.Max(0, _commandHistory.Count - 5);
                for (int i = startIndex; i < _commandHistory.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUIStyle historyStyle = new GUIStyle(GUI.skin.label);
                    historyStyle.fontSize = 10;
                    historyStyle.normal.textColor = Color.gray;
                    GUILayout.Label(_commandHistory[i], historyStyle);
                    
                    if (GUILayout.Button("Use", GUILayout.Width(50)))
                    {
                        _commandInput = _commandHistory[i];
                        GUI.FocusControl("RCCommandInput");
                    }
                    GUILayout.EndHorizontal();
                }
            }
            
            GUILayout.Space(5);
            
            // Help text
            GUIStyle helpStyle = new GUIStyle(GUI.skin.label);
            helpStyle.fontSize = 9;
            helpStyle.normal.textColor = Color.gray;
            helpStyle.wordWrap = true;
            GUILayout.Label("Note: RC commands require server admin access. Common commands: rc login <password>, rc kick <player>, rc broadcast <message>", helpStyle);
            
            GUILayout.EndVertical();
            
            // Make window draggable
            GUI.DragWindow();
            
            // Handle Enter key to execute command
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                if (!string.IsNullOrWhiteSpace(_commandInput))
                {
                    ExecuteCommand(_commandInput);
                    _commandInput = "";
                    GUI.FocusControl("RCCommandInput");
                    Event.current.Use();
                }
            }
        }
        
        private void ExecuteCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;
                
            // Add to history
            _commandHistory.Add(command);
            if (_commandHistory.Count > 20)
            {
                _commandHistory.RemoveAt(0);
            }
            
            AddLog($"> {command}");
            
            try
            {
                // Use the simplified CommandExecutor
                AdvancedAdminUI.Utils.CommandExecutor.InvokeCommand(command);
                
                if (AdvancedAdminUI.Utils.CommandExecutor.IsReady)
                {
                    AddLog("Command sent!");
                }
                else
                {
                    AddLog("Warning: Console not found. Open the game console first (~ key).");
                    // Try to reinitialize
                    AdvancedAdminUI.Utils.CommandExecutor.Reinitialize();
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
        }
        
        private void AddLog(string message)
        {
            _outputLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_outputLog.Count > MAX_LOG_LINES)
            {
                _outputLog.RemoveAt(0);
            }
            
            // Auto-scroll to bottom
            _scrollPosition.y = float.MaxValue;
        }
        
        public void OnApplicationQuit()
        {
            // Cleanup
        }
    }
}
