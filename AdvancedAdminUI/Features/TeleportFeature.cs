using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AdvancedAdminUI.Utils;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Teleport feature - allows admins to teleport players to coordinates or to themselves
    /// Shows grid coordinates for current position and mouse hover position
    /// </summary>
    public class TeleportFeature : IAdminFeature
    {
        public string FeatureName => "Teleport";
        public bool IsEnabled { get; private set; }
        
        // UI state
        private bool _showTeleportWindow = false;
        private Rect _windowRect = new Rect(Screen.width - 420, 100, 400, 500);
        private Vector2 _scrollPosition = Vector2.zero;
        
        // Coordinate display
        private Vector3 _playerPosition = Vector3.zero;
        private Vector3 _mouseWorldPosition = Vector3.zero;
        private bool _hasValidMousePosition = false;
        private string _lastRaycastInfo = "";
        
        // Teleport input
        private string _targetInput = ""; // Player ID, "all", faction name, etc.
        private string _coordX = "";
        private string _coordY = "";
        private string _coordZ = "";
        private bool _teleportToMe = false;
        
        // Command history
        private List<string> _commandHistory = new List<string>();
        private const int MAX_HISTORY = 10;
        
        // Styles
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _coordLabelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _infoBoxStyle;
        private bool _stylesInitialized = false;
        
        // Local player tracking
        private GameObject _localPlayerObject;
        private int _localPlayerId = -1;
        
        // Raycast settings
        private const float RAYCAST_HEIGHT = 500f; // Start raycast from this height above
        private const float RAYCAST_DISTANCE = 1000f; // Max raycast distance
        private LayerMask _groundLayerMask;
        
        public void Enable()
        {
            IsEnabled = true;
            _localPlayerSearched = false; // Reset search flag
            _localPlayerObject = null; // Clear cached player
            
            _groundLayerMask = LayerMask.GetMask("Default", "Terrain", "Ground", "Water");
            
            // If layer mask is 0, use everything except UI layers
            if (_groundLayerMask == 0)
            {
                _groundLayerMask = ~(LayerMask.GetMask("UI", "Ignore Raycast"));
            }
            
            // Subscribe to connection changes
            PlayerEventManager._onClientConnectionChangedCallbacks.Add(OnClientConnectionChanged);
            
            AdvancedAdminUIMod.Log.LogInfo("[Teleport] Feature enabled");
        }
        
        public void Disable()
        {
            IsEnabled = false;
            _showTeleportWindow = false;
            
            // Unsubscribe from events
            PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            AdvancedAdminUIMod.Log.LogInfo("[Teleport] Feature disabled");
        }
        
        /// <summary>
        /// Called when client connects to or disconnects from a server
        /// </summary>
        private void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Reset local player tracking
                _localPlayerObject = null;
                _localPlayerSearched = false;
                _localPlayerId = -1;
                _showTeleportWindow = false;
                _playerPosition = Vector3.zero;
                _mouseWorldPosition = Vector3.zero;
                _hasValidMousePosition = false;
            }
        }
        
        public void OnApplicationQuit()
        {
            // Clean up if needed
        }
        
        // Throttle updates to reduce lag
        private float _lastUpdateTime = 0f;
        private const float UPDATE_INTERVAL = 0.1f; // 10 FPS for position updates
        private bool _localPlayerSearched = false;
        
        public void OnUpdate()
        {
            if (!IsEnabled) return;
            
            // Toggle window with T key when admin UI is open
            if (Input.GetKeyDown(KeyCode.T) && !IsTypingInTextField())
            {
                _showTeleportWindow = !_showTeleportWindow;
            }
            
            // Throttle expensive updates
            float currentTime = Time.time;
            if (currentTime - _lastUpdateTime < UPDATE_INTERVAL)
                return;
            _lastUpdateTime = currentTime;
            
            // Update local player position (throttled)
            UpdateLocalPlayerPosition();
            
            // Update mouse world position via raycast (only when window open and throttled)
            if (_showTeleportWindow)
            {
                UpdateMouseWorldPosition();
            }
        }
        
        private bool IsTypingInTextField()
        {
            // Check if we're focused on a text field
            return GUIUtility.keyboardControl != 0;
        }
        
        private void UpdateLocalPlayerPosition()
        {
            // Try to find local player if we don't have one (only search once per session)
            if (_localPlayerObject == null || !_localPlayerObject.activeInHierarchy)
            {
                // Don't repeatedly search - it's expensive
                if (!_localPlayerSearched)
                {
                    _localPlayerSearched = true;
                    _localPlayerObject = FindLocalPlayer();
                }
                else if (_localPlayerObject == null)
                {
                    // Try again using PlayerTracker (cheap)
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
            // Use PlayerTracker - much cheaper than FindObjectsOfType
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
            
            // Fallback: look for main camera parent (cheap)
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
            
            // Last resort: Find by name (expensive - only do once)
            var localPlayer = GameObject.Find("Player - Owner");
            if (localPlayer != null)
            {
                _localPlayerId = ExtractPlayerId(localPlayer.name);
                return localPlayer;
            }
            
            return null;
        }
        
        private int ExtractPlayerId(string name)
        {
            // Extract ID from "Player - Local (#5)" or similar
            var match = System.Text.RegularExpressions.Regex.Match(name, @"#(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
            {
                return id;
            }
            return -1;
        }
        
        private void UpdateMouseWorldPosition()
        {
            if (Camera.main == null)
            {
                _hasValidMousePosition = false;
                return;
            }
            
            // Don't raycast if mouse is over the teleport window
            Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_windowRect.Contains(mousePos))
            {
                _hasValidMousePosition = false;
                _lastRaycastInfo = "Mouse over window";
                return;
            }
            
            // Create ray from camera through mouse position
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            
            // First try: direct raycast from camera
            if (Physics.Raycast(ray, out RaycastHit hit, RAYCAST_DISTANCE, _groundLayerMask))
            {
                _mouseWorldPosition = hit.point;
                _hasValidMousePosition = true;
                _lastRaycastInfo = $"Hit: {hit.collider.name}";
                return;
            }
            
            // Second try: raycast down from a point along the ray at height +100
            Vector3 farPoint = ray.GetPoint(500f); // Get a point far along the ray
            Vector3 highPoint = new Vector3(farPoint.x, farPoint.y + RAYCAST_HEIGHT, farPoint.z);
            
            if (Physics.Raycast(highPoint, Vector3.down, out RaycastHit downHit, RAYCAST_DISTANCE, _groundLayerMask))
            {
                _mouseWorldPosition = downHit.point;
                _hasValidMousePosition = true;
                _lastRaycastInfo = $"Down hit: {downHit.collider.name}";
                return;
            }
            
            // Third try: use terrain height if available
            if (Terrain.activeTerrain != null)
            {
                Vector3 worldPoint = ray.GetPoint(100f);
                float terrainHeight = Terrain.activeTerrain.SampleHeight(worldPoint);
                _mouseWorldPosition = new Vector3(worldPoint.x, terrainHeight, worldPoint.z);
                _hasValidMousePosition = true;
                _lastRaycastInfo = "Terrain sample";
                return;
            }
            
            _hasValidMousePosition = false;
            _lastRaycastInfo = "No ground hit";
        }
        
        public void OnGUI()
        {
            if (!IsEnabled) return;
            
            InitializeStyles();
            
            // Draw coordinate overlay when window is open
            if (_showTeleportWindow)
            {
                DrawCoordinateOverlay();
                _windowRect = GUI.Window(9876, _windowRect, DrawTeleportWindow, "", _windowStyle);
            }
        }
        
        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            
            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 25, 10)
            };
            
            var bgTex = MakeTex(2, 2, new Color(0.1f, 0.1f, 0.12f, 0.95f));
            _windowStyle.normal.background = bgTex;
            _windowStyle.onNormal.background = bgTex;
            
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
            
            _coordLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.4f, 0.9f, 1f) }, // Cyan
                alignment = TextAnchor.MiddleLeft
            };
            
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.4f, 0.9f, 1f) },
                alignment = TextAnchor.MiddleCenter
            };
            
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.textColor = new Color(0.4f, 0.9f, 1f);
            
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            
            _infoBoxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 5, 5)
            };
            _infoBoxStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            
            _stylesInitialized = true;
        }
        
        private void DrawCoordinateOverlay()
        {
            // Draw mouse position coordinates near cursor when hovering over world
            if (_hasValidMousePosition)
            {
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y; // Convert to GUI coords
                
                string coordText = $"X:{_mouseWorldPosition.x:F1} Y:{_mouseWorldPosition.y:F1} Z:{_mouseWorldPosition.z:F1}";
                
                GUIStyle overlayStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                overlayStyle.normal.textColor = new Color(1f, 0.9f, 0.3f); // Yellow
                overlayStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
                
                Vector2 size = overlayStyle.CalcSize(new GUIContent(coordText));
                Rect overlayRect = new Rect(mousePos.x + 15, mousePos.y + 15, size.x + 16, size.y + 8);
                
                // Keep on screen
                if (overlayRect.xMax > Screen.width) overlayRect.x = Screen.width - overlayRect.width;
                if (overlayRect.yMax > Screen.height) overlayRect.y = Screen.height - overlayRect.height;
                
                GUI.Box(overlayRect, coordText, overlayStyle);
            }
        }
        
        private void DrawTeleportWindow(int windowId)
        {
            float y = 5;
            float padding = 5;
            float width = _windowRect.width - 20;
            
            // Header
            GUI.Label(new Rect(0, y, width + 20, 25), "TELEPORT CONTROLS", _headerStyle);
            y += 30;
            
            // Close button
            if (GUI.Button(new Rect(_windowRect.width - 25, 5, 20, 20), "X", _buttonStyle))
            {
                _showTeleportWindow = false;
            }
            
            // === Your Position ===
            GUI.Label(new Rect(padding, y, width, 20), "YOUR POSITION:", _labelStyle);
            y += 20;
            
            string yourCoords = $"X: {_playerPosition.x:F2}  Y: {_playerPosition.y:F2}  Z: {_playerPosition.z:F2}";
            GUI.Label(new Rect(padding, y, width, 25), yourCoords, _coordLabelStyle);
            y += 25;
            
            if (GUI.Button(new Rect(padding, y, 120, 25), "Copy Coords", _buttonStyle))
            {
                GUIUtility.systemCopyBuffer = $"{_playerPosition.x:F2} {_playerPosition.y:F2} {_playerPosition.z:F2}";
                AdvancedAdminUIMod.Log.LogInfo("[Teleport] Coordinates copied to clipboard");
            }
            
            if (GUI.Button(new Rect(padding + 130, y, 120, 25), "Use My Coords", _buttonStyle))
            {
                _coordX = _playerPosition.x.ToString("F2");
                _coordY = _playerPosition.y.ToString("F2");
                _coordZ = _playerPosition.z.ToString("F2");
            }
            y += 35;
            
            // === Mouse Position ===
            GUI.Label(new Rect(padding, y, width, 20), "MOUSE WORLD POSITION:", _labelStyle);
            y += 20;
            
            if (_hasValidMousePosition)
            {
                string mouseCoords = $"X: {_mouseWorldPosition.x:F2}  Y: {_mouseWorldPosition.y:F2}  Z: {_mouseWorldPosition.z:F2}";
                GUI.Label(new Rect(padding, y, width, 25), mouseCoords, _coordLabelStyle);
                y += 25;
                
                if (GUI.Button(new Rect(padding, y, 120, 25), "Use Mouse Coords", _buttonStyle))
                {
                    _coordX = _mouseWorldPosition.x.ToString("F2");
                    _coordY = _mouseWorldPosition.y.ToString("F2");
                    _coordZ = _mouseWorldPosition.z.ToString("F2");
                }
            }
            else
            {
                GUI.Label(new Rect(padding, y, width, 25), "(Hover over map to get coords)", _labelStyle);
                y += 25;
            }
            y += 30;
            
            // Separator
            GUI.Box(new Rect(padding, y, width, 2), "");
            y += 10;
            
            // === Teleport Controls ===
            GUI.Label(new Rect(padding, y, width, 20), "TELEPORT COMMAND:", _headerStyle);
            y += 25;
            
            // Target input
            GUI.Label(new Rect(padding, y, 60, 20), "Target:", _labelStyle);
            _targetInput = GUI.TextField(new Rect(padding + 65, y, width - 70, 22), _targetInput, _textFieldStyle);
            y += 25;
            
            GUI.Label(new Rect(padding, y, width, 18), "ID, 'all', or faction (e.g., 'british', 'french')", 
                new GUIStyle(_labelStyle) { fontSize = 10, fontStyle = FontStyle.Italic, normal = { textColor = Color.gray } });
            y += 22;
            
            // Teleport to me toggle
            _teleportToMe = GUI.Toggle(new Rect(padding, y, width, 22), _teleportToMe, "  Teleport to me (ignore coordinates below)");
            y += 28;
            
            // Coordinate inputs (only show if not teleporting to me)
            GUI.enabled = !_teleportToMe;
            GUI.Label(new Rect(padding, y, width, 20), "Destination Coordinates:", _labelStyle);
            y += 22;
            
            // X, Y, Z inputs on same row
            float fieldWidth = (width - 60) / 3;
            GUI.Label(new Rect(padding, y, 20, 22), "X:", _labelStyle);
            _coordX = GUI.TextField(new Rect(padding + 20, y, fieldWidth - 5, 22), _coordX, _textFieldStyle);
            
            GUI.Label(new Rect(padding + fieldWidth + 25, y, 20, 22), "Y:", _labelStyle);
            _coordY = GUI.TextField(new Rect(padding + fieldWidth + 45, y, fieldWidth - 5, 22), _coordY, _textFieldStyle);
            
            GUI.Label(new Rect(padding + fieldWidth * 2 + 50, y, 20, 22), "Z:", _labelStyle);
            _coordZ = GUI.TextField(new Rect(padding + fieldWidth * 2 + 70, y, fieldWidth - 5, 22), _coordZ, _textFieldStyle);
            y += 30;
            GUI.enabled = true;
            
            // Execute button
            if (GUI.Button(new Rect(padding, y, width, 35), "▶ EXECUTE TELEPORT", _buttonStyle))
            {
                ExecuteTeleport();
            }
            y += 45;
            
            // Separator
            GUI.Box(new Rect(padding, y, width, 2), "");
            y += 10;
            
            // Quick teleport buttons
            GUI.Label(new Rect(padding, y, width, 20), "QUICK ACTIONS:", _labelStyle);
            y += 22;
            
            float btnWidth = (width - 10) / 2;
            if (GUI.Button(new Rect(padding, y, btnWidth, 25), "Teleport All to Me", _buttonStyle))
            {
                TeleportAllToMe();
            }
            if (GUI.Button(new Rect(padding + btnWidth + 10, y, btnWidth, 25), "Teleport Team to Me", _buttonStyle))
            {
                TeleportTeamToMe();
            }
            y += 35;
            
            // Command history
            GUI.Label(new Rect(padding, y, width, 20), "RECENT COMMANDS:", _labelStyle);
            y += 22;
            
            float historyHeight = _windowRect.height - y - 10;
            _scrollPosition = GUI.BeginScrollView(
                new Rect(padding, y, width, historyHeight),
                _scrollPosition,
                new Rect(0, 0, width - 20, _commandHistory.Count * 22)
            );
            
            for (int i = _commandHistory.Count - 1; i >= 0; i--)
            {
                int reverseIndex = _commandHistory.Count - 1 - i;
                GUI.Label(new Rect(0, reverseIndex * 22, width - 20, 20), _commandHistory[i], 
                    new GUIStyle(_labelStyle) { fontSize = 10 });
            }
            
            GUI.EndScrollView();
            
            // Make window draggable
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 25));
        }
        
        private void ExecuteTeleport()
        {
            if (string.IsNullOrWhiteSpace(_targetInput))
            {
                AdvancedAdminUIMod.Log.LogWarning("[Teleport] No target specified");
                return;
            }
            
            string command;
            
            if (_teleportToMe)
            {
                // Teleport target(s) to local player's position
                command = BuildTeleportCommand(_targetInput, _playerPosition);
            }
            else
            {
                // Parse coordinates
                if (!float.TryParse(_coordX, out float x) ||
                    !float.TryParse(_coordY, out float y) ||
                    !float.TryParse(_coordZ, out float z))
                {
                    AdvancedAdminUIMod.Log.LogWarning("[Teleport] Invalid coordinates");
                    return;
                }
                
                command = BuildTeleportCommand(_targetInput, new Vector3(x, y, z));
            }
            
            if (!string.IsNullOrEmpty(command))
            {
                ExecuteRCCommand(command);
                AddToHistory(command);
            }
        }
        
        private string BuildTeleportCommand(string target, Vector3 position)
        {
            // Holdfast RC command format: rc teleportplayer <playerId> <x> <y> <z>
            // For multiple players, we need to send multiple commands
            
            string posStr = $"{position.x:F2} {position.y:F2} {position.z:F2}";
            
            // Check if target is a number (single player ID)
            if (int.TryParse(target.Trim(), out int playerId))
            {
                return $"teleportplayer {playerId} {posStr}";
            }
            
            // Handle special targets
            string lowerTarget = target.Trim().ToLower();
            
            if (lowerTarget == "all")
            {
                // Get all player IDs and teleport each
                var allPlayers = PlayerTracker.GetAllPlayers();
                foreach (var player in allPlayers)
                {
                    if (player.Value.PlayerId != _localPlayerId) // Don't teleport self
                    {
                        ExecuteRCCommand($"teleportplayer {player.Value.PlayerId} {posStr}");
                    }
                }
                return $"[Teleported {allPlayers.Count} players]";
            }
            
            // Check for faction names
            var factionPlayers = GetPlayersByFaction(lowerTarget);
            if (factionPlayers.Count > 0)
            {
                foreach (var player in factionPlayers)
                {
                    if (player.PlayerId != _localPlayerId)
                    {
                        ExecuteRCCommand($"teleportplayer {player.PlayerId} {posStr}");
                    }
                }
                return $"[Teleported {factionPlayers.Count} {lowerTarget} players]";
            }
            
            // Try as comma-separated IDs
            if (target.Contains(","))
            {
                var ids = target.Split(',');
                int count = 0;
                foreach (var idStr in ids)
                {
                    if (int.TryParse(idStr.Trim(), out int id))
                    {
                        ExecuteRCCommand($"teleportplayer {id} {posStr}");
                        count++;
                    }
                }
                return $"[Teleported {count} players]";
            }
            
            AdvancedAdminUIMod.Log.LogWarning($"[Teleport] Unknown target: {target}");
            return null;
        }
        
        private List<PlayerTracker.PlayerData> GetPlayersByFaction(string faction)
        {
            var result = new List<PlayerTracker.PlayerData>();
            var allPlayers = PlayerTracker.GetAllPlayers();
            
            foreach (var kvp in allPlayers)
            {
                var player = kvp.Value;
                string playerFaction = player.FactionName?.ToLower() ?? "";
                
                if (playerFaction.Contains(faction) || faction.Contains(playerFaction))
                {
                    result.Add(player);
                }
            }
            
            return result;
        }
        
        private void TeleportAllToMe()
        {
            _targetInput = "all";
            _teleportToMe = true;
            ExecuteTeleport();
        }
        
        private void TeleportTeamToMe()
        {
            // Get local player's faction
            var localPlayer = PlayerTracker.GetPlayer(_localPlayerId);
            if (localPlayer != null && !string.IsNullOrEmpty(localPlayer.FactionName))
            {
                _targetInput = localPlayer.FactionName.ToLower();
                _teleportToMe = true;
                ExecuteTeleport();
            }
            else
            {
                AdvancedAdminUIMod.Log.LogWarning("[Teleport] Could not determine local player's faction");
            }
        }
        
        private void ExecuteRCCommand(string command)
        {
            // Execute via console - the game's RC system
            // Format: rc <command>
            string fullCommand = $"rc {command}";
            
            AdvancedAdminUIMod.Log.LogInfo($"[Teleport] Executing: {fullCommand}");
            
            // Try to find and execute via the game's console system
            try
            {
                // Look for console execution method via reflection
                var consoleType = Type.GetType("HoldfastGame.UI.Console, Assembly-CSharp");
                if (consoleType != null)
                {
                    var instanceProp = consoleType.GetProperty("Instance", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (instanceProp != null)
                    {
                        var console = instanceProp.GetValue(null);
                        if (console != null)
                        {
                            var executeMethod = consoleType.GetMethod("ExecuteCommand",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            
                            if (executeMethod != null)
                            {
                                executeMethod.Invoke(console, new object[] { fullCommand });
                                return;
                            }
                        }
                    }
                }
                
                // Alternative: Try GameConsole
                var gameConsoleType = Type.GetType("GameConsole, Assembly-CSharp");
                if (gameConsoleType != null)
                {
                    var executeMethod = gameConsoleType.GetMethod("Execute",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (executeMethod != null)
                    {
                        executeMethod.Invoke(null, new object[] { fullCommand });
                        return;
                    }
                }
                
                AdvancedAdminUIMod.Log.LogWarning("[Teleport] Could not find console to execute command. Copy and paste manually.");
                GUIUtility.systemCopyBuffer = fullCommand;
                
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[Teleport] Error executing command: {ex.Message}");
            }
        }
        
        private void AddToHistory(string command)
        {
            _commandHistory.Add($"[{DateTime.Now:HH:mm:ss}] {command}");
            if (_commandHistory.Count > MAX_HISTORY)
            {
                _commandHistory.RemoveAt(0);
            }
        }
        
        private Texture2D MakeTex(int width, int height, Color color)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = color;
            
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}


