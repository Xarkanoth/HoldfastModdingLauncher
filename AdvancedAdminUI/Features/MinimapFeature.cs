using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using AdvancedAdminUI.Utils;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Custom capture bounds for a specific map
    /// </summary>
    [Serializable]
    public class MapCaptureBounds
    {
        public string MapName;
        public float MinX, MinZ, MaxX, MaxZ; // World coordinates
        
        public MapCaptureBounds() { }
        
        public MapCaptureBounds(string mapName, float minX, float minZ, float maxX, float maxZ)
        {
            MapName = mapName;
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }
    }
    
    /// <summary>
    /// Minimap feature that displays player positions on a top-down map view in the AdminWindow GUI
    /// Shows player icons only on the minimap, not in-game
    /// </summary>
    public class MinimapFeature : IAdminFeature
    {
        public string FeatureName => "Minimap";
        
        private bool _isEnabled = false;
        
        // Minimap settings
        public const int MINIMAP_SIZE_DEFAULT = 700; // Default size (will scale with window)
        public const float MINIMAP_SCALE_FACTOR = 0.85f; // Scale to 85% of available space
        public const int MINIMAP_SIZE_MIN = 400; // Minimum size (allows smaller windows)
        public const int MINIMAP_SIZE_MAX = 1400; // Maximum size
        private const float ICON_SIZE = 6f; // Size of player icons on minimap
        
        // World bounds from terrain (for photo capture and player positioning)
        private Vector3 _terrainMin = Vector3.zero;
        private Vector3 _terrainMax = Vector3.zero;
        private Vector3 _terrainCenter = Vector3.zero;
        private float _terrainSize = 0f;
        private bool _terrainFound = false;
        private bool _terrainSearched = false;
        
        // Player icon colors by faction
        private Dictionary<string, Color> _factionColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        
        // Aerial camera for map capture - captured ONCE per round
        private Texture2D _mapTexture;
        private bool _hasMapCapture = false;
        private const int MAP_CAPTURE_SIZE = 2048; // 2K resolution for terrain (4K was causing lag)
        private const float CAMERA_HEIGHT = 1000f; // Higher to capture whole terrain
        
        // Layer mask to exclude players from photo
        private int _noPlayersLayerMask = ~0;
        
        // Delayed capture after round start
        private bool _pendingCapture = false;
        private float _captureDelayStartTime = 0f;
        private const float CAPTURE_DELAY_SECONDS = 30f; // Wait 30 seconds after round start for map to fully load
        
        // Custom map bounds - saved per map name
        private Dictionary<string, MapCaptureBounds> _customMapBounds = new Dictionary<string, MapCaptureBounds>(StringComparer.OrdinalIgnoreCase);
        private string _currentMapName = "";
        private static readonly string BoundsConfigPath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "map_bounds.json");
        
        // Selection mode for defining custom bounds
        private bool _isSelectionMode = false;
        private Vector2 _selectionStart = Vector2.zero;
        private Vector2 _selectionEnd = Vector2.zero;
        private bool _isSelecting = false;
        
        // Flag to trigger recapture on main thread (set from WinForms thread, read from Unity thread)
        private volatile bool _pendingRecaptureFromSelection = false;
        
        // Version counter to detect when map has been recaptured (for AdminWindow cache invalidation)
        private int _mapCaptureVersion = 0;
        
        // Pending teleport request (set from WinForms thread, executed on Unity thread)
        private volatile bool _hasPendingTeleport = false;
        private int _pendingTeleportPlayerId = -1;
        private float _pendingTeleportMinimapX = 0f;
        private float _pendingTeleportMinimapY = 0f;
        private int _pendingTeleportMinimapSize = 0;
        private string _pendingTeleportPlayerName = "";
        
        // Public properties for AdminWindow
        public bool IsSelectionMode => _isSelectionMode;
        public Vector2 SelectionStart => _selectionStart;
        public Vector2 SelectionEnd => _selectionEnd;
        public bool IsSelecting => _isSelecting;
        public string CurrentMapName => _currentMapName;
        public bool HasCustomBounds => !string.IsNullOrEmpty(_currentMapName) && _customMapBounds.ContainsKey(_currentMapName);
        
        public bool IsEnabled => _isEnabled;
        public bool HasMapCapture => _hasMapCapture;
        public Texture2D MapTexture => _mapTexture;
        public int MapCaptureVersion => _mapCaptureVersion;
        
        public void Enable()
        {
            _isEnabled = true;
            InitializeFactionColors();
            LoadCustomBounds();
            
            // Reset terrain search so we find terrain fresh
            _terrainSearched = false;
            _terrainFound = false;
            
            // Clear any existing capture so we take a fresh photo
            _hasMapCapture = false;
            
            // Find terrain - this will trigger a new capture in OnUpdate
            FindTerrain();
            
            // Subscribe to round details to re-capture map on new rounds
            PlayerEventManager._onRoundDetailsCallbacks.Add(OnRoundDetailsReceived);
            
            // Subscribe to client connection changes to reset state when leaving server
            PlayerEventManager._onClientConnectionChangedCallbacks.Add(OnClientConnectionChanged);
            
            AdvancedAdminUIMod.Log.LogInfo("[Minimap] Enabled - will capture new map photo");
        }
        
        public void Disable()
        {
            _isEnabled = false;
            
            // Unsubscribe from events
            PlayerEventManager._onRoundDetailsCallbacks.Remove(OnRoundDetailsReceived);
            PlayerEventManager._onClientConnectionChangedCallbacks.Remove(OnClientConnectionChanged);
            
            // Clear the map capture
            _hasMapCapture = false;
            _pendingCapture = false;
            _terrainFound = false;
            _terrainSearched = false;
            
            // Destroy the texture to free memory
            if (_mapTexture != null)
            {
                UnityEngine.Object.Destroy(_mapTexture);
                _mapTexture = null;
            }
            
            AdvancedAdminUIMod.Log.LogInfo("[Minimap] Disabled - cleared map photo");
        }
        
        /// <summary>
        /// Called when client connects to or disconnects from a server
        /// </summary>
        private void OnClientConnectionChanged(bool isConnected)
        {
            if (!isConnected)
            {
                // Disconnected from server - reset all state
                AdvancedAdminUIMod.Log.LogInfo("[Minimap] Disconnected from server - resetting map state");
                
                _currentMapName = "";
                _terrainFound = false;
                _terrainSearched = false;
                _hasMapCapture = false;
                _pendingCapture = false;
                _isSelectionMode = false;
                _isSelecting = false;
                
                // Destroy map texture
                if (_mapTexture != null)
                {
                    UnityEngine.Object.Destroy(_mapTexture);
                    _mapTexture = null;
                }
            }
            else
            {
                // Connected to server - will wait for OnRoundDetails
                AdvancedAdminUIMod.Log.LogInfo("[Minimap] Connected to server - waiting for round details");
            }
        }
        
        /// <summary>
        /// Called when OnRoundDetails is received - schedule a new map capture
        /// </summary>
        private void OnRoundDetailsReceived(int roundId, string serverName, string mapName, 
            HoldfastSharedMethods.FactionCountry attackingFaction, HoldfastSharedMethods.FactionCountry defendingFaction, 
            HoldfastSharedMethods.GameplayMode gameplayMode, HoldfastSharedMethods.GameType gameType)
        {
            // Skip dummy round details (from round end events)
            if (roundId < 0 || string.IsNullOrEmpty(mapName))
            {
                return;
            }
            
            _currentMapName = mapName;
            bool hasCustom = _customMapBounds.ContainsKey(mapName);
            AdvancedAdminUIMod.Log.LogInfo($"[Minimap] New round started! Map: {mapName}, hasCustomBounds: {hasCustom}");
            AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Scheduling map capture in {CAPTURE_DELAY_SECONDS} seconds...");
            
            // Reset for new map - destroys old texture
            OnNewRound();
            
            // Schedule delayed capture
            _pendingCapture = true;
            _captureDelayStartTime = Time.time;
        }
        
        /// <summary>
        /// Called when a new round starts - resets terrain detection to capture the new map
        /// </summary>
        public void OnNewRound()
        {
            // Reset terrain detection so we find the new map's terrain
            _terrainFound = false;
            _terrainSearched = false;
            _hasMapCapture = false;
            
            // Destroy old map texture
            if (_mapTexture != null)
            {
                UnityEngine.Object.Destroy(_mapTexture);
                _mapTexture = null;
            }
        }
        
        /// <summary>
        /// Queue a teleport request from the WinForms thread to be executed on Unity main thread
        /// This is thread-safe - call from any thread
        /// </summary>
        public void QueueTeleport(int playerId, string playerName, float minimapX, float minimapY, int minimapSize)
        {
            _pendingTeleportPlayerId = playerId;
            _pendingTeleportPlayerName = playerName;
            _pendingTeleportMinimapX = minimapX;
            _pendingTeleportMinimapY = minimapY;
            _pendingTeleportMinimapSize = minimapSize;
            _hasPendingTeleport = true; // Set flag LAST to ensure all values are written
            
            AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Queued teleport for player {playerId} ({playerName})");
        }
        
        /// <summary>
        /// Execute the pending teleport - MUST be called from Unity main thread
        /// </summary>
        private void ExecutePendingTeleport()
        {
            try
            {
                int playerId = _pendingTeleportPlayerId;
                string playerName = _pendingTeleportPlayerName;
                
                // Convert minimap position to world coordinates (uses Physics.Raycast - must be on main thread)
                Vector3 worldPos = MinimapToWorld(
                    new Vector2(_pendingTeleportMinimapX, _pendingTeleportMinimapY), 
                    _pendingTeleportMinimapSize
                );
                
                if (worldPos == Vector3.zero)
                {
                    AdvancedAdminUIMod.Log.LogWarning("[Minimap] Cannot teleport - terrain not found");
                    return;
                }
                
                // Execute teleport command (Holdfast format: rc teleport <id> x,y,z with commas)
                string command = $"rc teleport {playerId} {worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}";
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Teleporting {playerName} (ID:{playerId}) to ({worldPos.x:F1}, {worldPos.y:F1}, {worldPos.z:F1})");
                
                // Execute via CommandExecutor (must be on main thread)
                Utils.CommandExecutor.InvokeCommand(command);
                
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] ✓ Teleport command sent for {playerName}");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[Minimap] Teleport failed: {ex.Message}");
            }
        }
        
        public void OnUpdate()
        {
            if (!_isEnabled)
                return;
            
            // Process pending teleport from WinForms thread (MUST be on Unity main thread)
            if (_hasPendingTeleport)
            {
                _hasPendingTeleport = false;
                ExecutePendingTeleport();
            }
            
            // Check for pending recapture from selection mode (triggered from WinForms thread)
            if (_pendingRecaptureFromSelection)
            {
                _pendingRecaptureFromSelection = false;
                AdvancedAdminUIMod.Log.LogInfo("[Minimap] Recapturing with custom bounds...");
                
                _hasMapCapture = false;
                _terrainSearched = false;
                FindTerrain(); // Will now use custom bounds
                
                if (_terrainFound)
                {
                    CaptureMapPhoto();
                    AdvancedAdminUIMod.Log.LogInfo("[Minimap] ✓ Map photo captured with custom bounds!");
                }
                return; // Don't process other captures this frame
            }
            
            // Check for pending delayed capture (after OnRoundDetails)
            if (_pendingCapture)
            {
                float elapsed = Time.time - _captureDelayStartTime;
                if (elapsed >= CAPTURE_DELAY_SECONDS)
                {
                    _pendingCapture = false;
                    AdvancedAdminUIMod.Log.LogInfo($"[Minimap] {CAPTURE_DELAY_SECONDS}s delay elapsed - capturing new map photo for: {_currentMapName}");
                    
                    // Reset terrain search to find the new terrain
                    _terrainSearched = false;
                    _terrainFound = false;
                    FindTerrain();
                    
                    if (_terrainFound)
                    {
                        CaptureMapPhoto();
                        AdvancedAdminUIMod.Log.LogInfo("[Minimap] ✓ Map photo captured successfully!");
                    }
                    else
                    {
                        AdvancedAdminUIMod.Log.LogWarning("[Minimap] Terrain not found after delay, will retry on next update");
                    }
                }
            }
            
            // Try to find terrain if not found yet
            if (!_terrainFound && !_terrainSearched)
            {
                FindTerrain();
            }
            
            // Capture map ONCE after terrain is found (per round)
            if (_terrainFound && !_hasMapCapture && !_pendingCapture)
            {
                CaptureMapPhoto();
            }
        }
        
        /// <summary>
        /// Find the Unity Terrain and get its bounds
        /// </summary>
        private void FindTerrain()
        {
            _terrainSearched = true;
            
            try
            {
                // Check for custom bounds for this map first
                if (!string.IsNullOrEmpty(_currentMapName) && _customMapBounds.TryGetValue(_currentMapName, out var customBounds))
                {
                    AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Using custom bounds for '{_currentMapName}'");
                    _terrainMin = new Vector3(customBounds.MinX, 0, customBounds.MinZ);
                    _terrainMax = new Vector3(customBounds.MaxX, 0, customBounds.MaxZ);
                    
                    float width = customBounds.MaxX - customBounds.MinX;
                    float depth = customBounds.MaxZ - customBounds.MinZ;
                    
                    _terrainCenter = new Vector3(
                        customBounds.MinX + width / 2f,
                        CAMERA_HEIGHT,
                        customBounds.MinZ + depth / 2f
                    );
                    _terrainSize = Mathf.Max(width, depth) / 2f;
                    _terrainFound = true;
                    
                    // Still need to set up layer mask
                    SetupLayerMask();
                    return;
                }
                
                // Find all terrains in the scene
                Terrain[] terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
                
                if (terrains == null || terrains.Length == 0)
                    return;
                
                // Use the first/main terrain, or combine all terrain bounds
                Terrain mainTerrain = terrains[0];
                
                if (mainTerrain.terrainData == null)
                    return;
                
                // Get terrain bounds
                Vector3 terrainPos = mainTerrain.transform.position;
                Vector3 terrainSize = mainTerrain.terrainData.size;
                
                _terrainMin = terrainPos;
                _terrainMax = terrainPos + terrainSize;
                _terrainCenter = new Vector3(
                    terrainPos.x + terrainSize.x / 2f,
                    CAMERA_HEIGHT,
                    terrainPos.z + terrainSize.z / 2f
                );
                _terrainSize = Mathf.Max(terrainSize.x, terrainSize.z) / 2f;
                
                SetupLayerMask();
                _terrainFound = true;
            }
            catch { }
        }
        
        /// <summary>
        /// Set up layer mask to exclude player layers
        /// </summary>
        private void SetupLayerMask()
        {
            // Build culling mask that excludes common player layers
            // Typical player layers: 8 (Player), 9 (Characters), etc.
            _noPlayersLayerMask = ~0; // Start with everything
            
            // Try to find and exclude player-related layers
            int[] playerLayers = { 8, 9, 10, 11, 12 }; // Common player/character layers
            foreach (int layer in playerLayers)
            {
                string layerName = LayerMask.LayerToName(layer);
                if (!string.IsNullOrEmpty(layerName) && 
                    (layerName.ToLower().Contains("player") || 
                     layerName.ToLower().Contains("character") ||
                     layerName.ToLower().Contains("actor") ||
                     layerName.ToLower().Contains("unit")))
                {
                    _noPlayersLayerMask &= ~(1 << layer);
                }
            }
            
            // Also try excluding by common layer numbers used for players
            _noPlayersLayerMask &= ~(1 << 8);  // Often "Player" layer
            _noPlayersLayerMask &= ~(1 << 9);  // Often "Characters" layer
        }
        
        public void OnGUI()
        {
            // Minimap is now displayed in AdminWindow, not as in-game overlay
            // This method is kept for IAdminFeature interface compatibility
        }
        
        public void OnApplicationQuit()
        {
            _isEnabled = false;
            if (_mapTexture != null)
            {
                UnityEngine.Object.Destroy(_mapTexture);
                _mapTexture = null;
            }
        }
        
        #region Custom Bounds Selection
        
        /// <summary>
        /// Enter selection mode - user can drag a rectangle to define capture bounds
        /// </summary>
        public void EnterSelectionMode()
        {
            _isSelectionMode = true;
            _isSelecting = false;
            _selectionStart = Vector2.zero;
            _selectionEnd = Vector2.zero;
            AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Entered selection mode for map: {_currentMapName}");
        }
        
        /// <summary>
        /// Exit selection mode without saving
        /// </summary>
        public void ExitSelectionMode()
        {
            _isSelectionMode = false;
            _isSelecting = false;
            AdvancedAdminUIMod.Log.LogInfo("[Minimap] Exited selection mode");
        }
        
        /// <summary>
        /// Called when user starts dragging (mouse down)
        /// </summary>
        public void StartSelection(Vector2 minimapPos)
        {
            if (!_isSelectionMode) return;
            _isSelecting = true;
            _selectionStart = minimapPos;
            _selectionEnd = minimapPos;
        }
        
        /// <summary>
        /// Called while user is dragging (mouse move)
        /// </summary>
        public void UpdateSelection(Vector2 minimapPos)
        {
            if (!_isSelecting) return;
            _selectionEnd = minimapPos;
        }
        
        /// <summary>
        /// Called when user finishes dragging (mouse up) - saves the bounds
        /// Thread-safe - can be called from WinForms thread
        /// </summary>
        public void FinishSelection(Vector2 minimapPos, int minimapSize)
        {
            if (!_isSelecting) return;
            _selectionEnd = minimapPos;
            _isSelecting = false;
            
            // Convert minimap coordinates to world coordinates (thread-safe version - no Physics.Raycast)
            var (startX, startZ) = MinimapToWorldXZ(_selectionStart, minimapSize);
            var (endX, endZ) = MinimapToWorldXZ(_selectionEnd, minimapSize);
            
            // Create bounds (ensure min < max)
            float minX = Mathf.Min(startX, endX);
            float maxX = Mathf.Max(startX, endX);
            float minZ = Mathf.Min(startZ, endZ);
            float maxZ = Mathf.Max(startZ, endZ);
            
            // Minimum size check
            if (maxX - minX < 50f || maxZ - minZ < 50f)
            {
                AdvancedAdminUIMod.Log.LogWarning("[Minimap] Selection too small, please drag a larger area");
                return;
            }
            
            // Save the bounds for this map
            var bounds = new MapCaptureBounds(_currentMapName, minX, minZ, maxX, maxZ);
            _customMapBounds[_currentMapName] = bounds;
            
            // Save to file (this is safe from any thread)
            try
            {
                SaveCustomBounds();
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Saved custom bounds for '{_currentMapName}': X({minX:F0} to {maxX:F0}), Z({minZ:F0} to {maxZ:F0})");
            }
            catch (System.Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[Minimap] Error saving bounds: {ex.Message}");
            }
            
            // Exit selection mode
            _isSelectionMode = false;
            
            // Flag for recapture on main Unity thread (don't call Unity APIs from WinForms thread!)
            _pendingRecaptureFromSelection = true;
        }
        
        /// <summary>
        /// Clear custom bounds for current map
        /// </summary>
        public void ClearCustomBounds()
        {
            if (string.IsNullOrEmpty(_currentMapName)) return;
            
            if (_customMapBounds.Remove(_currentMapName))
            {
                try
                {
                    SaveCustomBounds();
                    AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Cleared custom bounds for '{_currentMapName}'");
                }
                catch (System.Exception ex)
                {
                    AdvancedAdminUIMod.Log.LogWarning($"[Minimap] Error saving bounds: {ex.Message}");
                }
                
                // Flag for recapture on main Unity thread (don't call Unity APIs from WinForms thread!)
                _pendingRecaptureFromSelection = true;
            }
        }
        
        /// <summary>
        /// Convert minimap coordinates to world X,Z coordinates (no height sampling)
        /// Thread-safe - can be called from WinForms thread
        /// </summary>
        public (float x, float z) MinimapToWorldXZ(Vector2 minimapPos, int minimapSize)
        {
            if (!_terrainFound) return (0, 0);
            
            float terrainWidth = _terrainMax.x - _terrainMin.x;
            float terrainDepth = _terrainMax.z - _terrainMin.z;
            
            float normalizedX = minimapPos.x / minimapSize;
            float normalizedZ = 1f - (minimapPos.y / minimapSize); // Flip Y back to Z
            
            float worldX = _terrainMin.x + normalizedX * terrainWidth;
            float worldZ = _terrainMin.z + normalizedZ * terrainDepth;
            
            return (worldX, worldZ);
        }
        
        /// <summary>
        /// Convert minimap coordinates to world coordinates with proper height sampling
        /// WARNING: Uses Physics.Raycast - MUST be called from Unity main thread only!
        /// </summary>
        public Vector3 MinimapToWorld(Vector2 minimapPos, int minimapSize)
        {
            if (!_terrainFound) return Vector3.zero;
            
            float terrainWidth = _terrainMax.x - _terrainMin.x;
            float terrainDepth = _terrainMax.z - _terrainMin.z;
            
            float normalizedX = minimapPos.x / minimapSize;
            float normalizedZ = 1f - (minimapPos.y / minimapSize); // Flip Y back to Z
            
            float worldX = _terrainMin.x + normalizedX * terrainWidth;
            float worldZ = _terrainMin.z + normalizedZ * terrainDepth;
            
            // Sample terrain height at this position
            float worldY = SampleTerrainHeight(worldX, worldZ);
            
            return new Vector3(worldX, worldY, worldZ);
        }
        
        /// <summary>
        /// Sample the terrain height at a given world X,Z position
        /// Uses raycast first, falls back to terrain sampling
        /// Filters out world border planes and other non-terrain objects
        /// </summary>
        private float SampleTerrainHeight(float worldX, float worldZ)
        {
            float defaultHeight = 10f; // Default height above ground
            
            try
            {
                // First try: RaycastAll from high above to find all potential ground hits
                // Then filter out world borders and pick the best one
                Vector3 rayStart = new Vector3(worldX, 500f, worldZ);
                RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 1000f);
                
                if (hits.Length > 0)
                {
                    // Sort by Y position (highest first) and find valid ground
                    System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));
                    
                    foreach (var hit in hits)
                    {
                        // Skip world border planes and other unwanted objects
                        if (IsWorldBorderOrInvalid(hit))
                            continue;
                        
                        return hit.point.y + 1f; // 1 unit above ground
                    }
                }
                
                // Second try: Use Unity terrain sampling if terrain exists
                Terrain terrain = Terrain.activeTerrain;
                if (terrain != null && terrain.terrainData != null)
                {
                    float height = terrain.SampleHeight(new Vector3(worldX, 0, worldZ));
                    return height + terrain.transform.position.y + 1f; // 1 unit above terrain
                }
                
                // Fallback: Return a reasonable default
                return defaultHeight;
            }
            catch
            {
                return defaultHeight;
            }
        }
        
        // Known world border object name patterns (lowercase for comparison)
        private static readonly string[] WorldBorderPatterns = new string[]
        {
            "border",
            "boundary", 
            "barrier",
            "invisible",
            "blocker",
            "killzone",
            "kill_zone",
            "outofbounds",
            "out_of_bounds",
            "outofboundscolliders",
            "playeroutofbounds",
            "worldborder",
            "mapborder",
            "maplimit",
            "deathzone",
            "death_zone",
            "oob"
        };
        
        /// <summary>
        /// Check if a raycast hit is a world border plane or other invalid teleport target
        /// </summary>
        private bool IsWorldBorderOrInvalid(RaycastHit hit)
        {
            if (hit.collider == null)
                return true;
            
            GameObject obj = hit.collider.gameObject;
            
            // Check the object itself and all parents for border patterns
            if (IsObjectOrParentWorldBorder(obj))
                return true;
            
            // Check if it's a trigger collider (often used for borders/zones)
            if (hit.collider.isTrigger)
                return true;
            
            // Check layer - some maps put borders on specific layers
            int layer = obj.layer;
            string layerName = LayerMask.LayerToName(layer).ToLower();
            if (layerName.Contains("ignore") || 
                layerName.Contains("trigger") ||
                layerName.Contains("invisible") ||
                layerName.Contains("intangible"))
            {
                return true;
            }
            
            // Check for flat planes at extreme heights (likely world borders)
            if (hit.point.y > 200f || hit.point.y < -50f)
            {
                if (IsFlatPlane(obj))
                    return true;
            }
            
            // Check if the collider is a very large box collider (often used as invisible walls)
            BoxCollider boxCollider = hit.collider as BoxCollider;
            if (boxCollider != null)
            {
                Vector3 size = boxCollider.size;
                // If it's a thin but very wide/long collider, it's likely a border wall
                if ((size.x > 100f || size.z > 100f) && size.y < 5f)
                    return true;
                if ((size.x > 100f || size.y > 100f) && size.z < 5f)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if the object or any of its parents match world border patterns
        /// </summary>
        private bool IsObjectOrParentWorldBorder(GameObject obj)
        {
            Transform current = obj.transform;
            int depth = 0;
            const int maxDepth = 10; // Don't go too far up the hierarchy
            
            while (current != null && depth < maxDepth)
            {
                string name = current.gameObject.name.ToLower();
                
                foreach (string pattern in WorldBorderPatterns)
                {
                    if (name.Contains(pattern))
                        return true;
                }
                
                current = current.parent;
                depth++;
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if an object is a flat plane mesh
        /// </summary>
        private bool IsFlatPlane(GameObject obj)
        {
            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name.ToLower();
                if (meshName.Contains("plane") || meshName.Contains("quad"))
                    return true;
                
                // Check if mesh is very flat by examining bounds
                Bounds bounds = meshFilter.sharedMesh.bounds;
                float minDimension = Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z);
                float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                
                // If one dimension is very small compared to others, it's likely a plane
                if (minDimension < 0.1f && maxDimension > 10f)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Load custom bounds from config file
        /// </summary>
        private void LoadCustomBounds()
        {
            try
            {
                if (!File.Exists(BoundsConfigPath))
                {
                    AdvancedAdminUIMod.Log.LogInfo("[Minimap] No custom bounds config found, using defaults");
                    return;
                }
                
                string json = File.ReadAllText(BoundsConfigPath);
                // Simple JSON parsing - format: {"MapName":{"MinX":0,"MinZ":0,"MaxX":100,"MaxZ":100},...}
                // Using basic string parsing since we don't have a JSON library
                
                _customMapBounds.Clear();
                
                // Parse each map entry
                int startIdx = 0;
                while ((startIdx = json.IndexOf("\"MapName\"", startIdx)) >= 0)
                {
                    try
                    {
                        // Find map name
                        int nameStart = json.IndexOf("\"", json.IndexOf(":", startIdx)) + 1;
                        int nameEnd = json.IndexOf("\"", nameStart);
                        string mapName = json.Substring(nameStart, nameEnd - nameStart);
                        
                        // Find coordinates
                        float minX = ParseJsonFloat(json, "MinX", startIdx);
                        float minZ = ParseJsonFloat(json, "MinZ", startIdx);
                        float maxX = ParseJsonFloat(json, "MaxX", startIdx);
                        float maxZ = ParseJsonFloat(json, "MaxZ", startIdx);
                        
                        _customMapBounds[mapName] = new MapCaptureBounds(mapName, minX, minZ, maxX, maxZ);
                        AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Loaded custom bounds for '{mapName}'");
                        
                        startIdx = nameEnd;
                    }
                    catch
                    {
                        startIdx++;
                    }
                }
                
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Loaded {_customMapBounds.Count} custom map bounds");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[Minimap] Error loading custom bounds: {ex.Message}");
            }
        }
        
        private float ParseJsonFloat(string json, string key, int startAfter)
        {
            int keyIdx = json.IndexOf($"\"{key}\"", startAfter);
            if (keyIdx < 0) return 0;
            int colonIdx = json.IndexOf(":", keyIdx);
            int commaIdx = json.IndexOf(",", colonIdx);
            int braceIdx = json.IndexOf("}", colonIdx);
            int endIdx = (commaIdx >= 0 && commaIdx < braceIdx) ? commaIdx : braceIdx;
            string valueStr = json.Substring(colonIdx + 1, endIdx - colonIdx - 1).Trim();
            return float.TryParse(valueStr, out float result) ? result : 0;
        }
        
        /// <summary>
        /// Save custom bounds to config file (formatted JSON for easy reading)
        /// </summary>
        private void SaveCustomBounds()
        {
            try
            {
                // Build formatted JSON that's easy to read and copy
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"MapBounds\": [");
                
                int idx = 0;
                foreach (var kvp in _customMapBounds)
                {
                    string comma = (idx < _customMapBounds.Count - 1) ? "," : "";
                    sb.AppendLine($"    {{");
                    sb.AppendLine($"      \"MapName\": \"{kvp.Key}\",");
                    sb.AppendLine($"      \"MinX\": {kvp.Value.MinX:F1},");
                    sb.AppendLine($"      \"MinZ\": {kvp.Value.MinZ:F1},");
                    sb.AppendLine($"      \"MaxX\": {kvp.Value.MaxX:F1},");
                    sb.AppendLine($"      \"MaxZ\": {kvp.Value.MaxZ:F1}");
                    sb.AppendLine($"    }}{comma}");
                    idx++;
                }
                
                sb.AppendLine("  ],");
                sb.AppendLine("");
                sb.AppendLine("  \"CSharpCode\": \"// Add these to the _defaultMapBounds dictionary in MinimapFeature.cs:\"");
                sb.AppendLine("}");
                
                File.WriteAllText(BoundsConfigPath, sb.ToString());
                
                // Also write a separate C# code file for easy copy-paste
                string csharpPath = Path.Combine(Path.GetDirectoryName(BoundsConfigPath) ?? "", "map_bounds_code.txt");
                var codeSb = new System.Text.StringBuilder();
                codeSb.AppendLine("// Copy these lines to add as default bounds in MinimapFeature.cs:");
                codeSb.AppendLine("// Add to the constructor or Enable() method:");
                codeSb.AppendLine();
                foreach (var kvp in _customMapBounds)
                {
                    codeSb.AppendLine($"_defaultMapBounds[\"{kvp.Key}\"] = new MapCaptureBounds(\"{kvp.Key}\", {kvp.Value.MinX:F1}f, {kvp.Value.MinZ:F1}f, {kvp.Value.MaxX:F1}f, {kvp.Value.MaxZ:F1}f);");
                }
                File.WriteAllText(csharpPath, codeSb.ToString());
                
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Saved {_customMapBounds.Count} custom map bounds to:");
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap]   JSON: {BoundsConfigPath}");
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap]   Code: {csharpPath}");
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[Minimap] Error saving custom bounds: {ex.Message}");
            }
        }
        
        #endregion
        
        /// <summary>
        /// Captures an aerial photo of the entire terrain (called automatically every 5 seconds)
        /// Excludes players from the photo
        /// </summary>
        private void CaptureMapPhoto()
        {
            if (!_terrainFound)
                return;
            
            GameObject cameraObj = null;
            Camera camera = null;
            RenderTexture renderTexture = null;
            RenderTexture previousActive = RenderTexture.active;
            
            try
            {
                // Get main camera to copy rendering settings
                Camera mainCamera = Camera.main;
                
                // Create temporary camera positioned above terrain center
                // Use a slight angle instead of straight down to help with lighting/shaders
                cameraObj = new GameObject("MinimapCamera_Temp");
                cameraObj.tag = "MainCamera"; // Some shaders only render for MainCamera
                cameraObj.transform.position = _terrainCenter;
                cameraObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Face down
                
                camera = cameraObj.AddComponent<Camera>();
                
                // Try PERSPECTIVE camera instead of orthographic - many shaders don't work with ortho
                camera.orthographic = false;
                
                // Calculate height and FOV to capture entire terrain
                // FOV = 2 * atan(terrainSize / height) in degrees
                // For 90 degree FOV, height = terrainSize (captures exactly the terrain width)
                // Use slightly higher to add margin
                float perspectiveHeight = _terrainSize * 1.2f;
                cameraObj.transform.position = new Vector3(_terrainCenter.x, perspectiveHeight, _terrainCenter.z);
                
                // Calculate FOV needed: FOV = 2 * atan(terrainSize / height) * (180/PI)
                float fovRadians = 2f * Mathf.Atan(_terrainSize / perspectiveHeight);
                float fovDegrees = fovRadians * Mathf.Rad2Deg;
                camera.fieldOfView = Mathf.Clamp(fovDegrees, 60f, 120f); // Clamp to reasonable range
                
                camera.nearClipPlane = 10f;
                camera.farClipPlane = perspectiveHeight + 500f;
                camera.clearFlags = CameraClearFlags.Skybox; // Use skybox for proper lighting
                camera.backgroundColor = new Color(0.2f, 0.4f, 0.6f); // Blue fallback for water areas
                camera.enabled = false;
                
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Camera setup: Perspective, Height={perspectiveHeight}, FOV=60, TerrainSize={_terrainSize}");
                
                // Copy ALL rendering settings from main camera for proper water/shader rendering
                if (mainCamera != null)
                {
                    camera.renderingPath = mainCamera.renderingPath;
                    camera.allowHDR = mainCamera.allowHDR;
                    camera.allowMSAA = mainCamera.allowMSAA;
                    camera.useOcclusionCulling = false; // Disable occlusion for aerial view
                    
                    // Water shaders need depth textures
                    camera.depthTextureMode = mainCamera.depthTextureMode | DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
                    
                    AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Copied settings from main camera: RenderPath={mainCamera.renderingPath}, HDR={mainCamera.allowHDR}");
                }
                else
                {
                    camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
                    camera.renderingPath = RenderingPath.Forward;
                    AdvancedAdminUIMod.Log.LogInfo("[Minimap] No main camera found, using Forward rendering");
                }
                
                // Save current ambient settings
                var savedAmbientMode = RenderSettings.ambientMode;
                var savedAmbientLight = RenderSettings.ambientLight;
                var savedAmbientIntensity = RenderSettings.ambientIntensity;
                
                // Boost ambient lighting temporarily for better terrain visibility
                RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, 1.0f);
                
                // Build culling mask: include EVERYTHING except players and admin UI
                // Start with all layers enabled
                int cullingMask = ~0; // All 32 layers
                
                // Only exclude these specific layers:
                cullingMask &= ~(1 << 5);  // UI layer (our AdminCylinders)
                cullingMask &= ~(1 << 8);  // Often Player layer
                cullingMask &= ~(1 << 9);  // Often Characters layer
                
                // Try to find and exclude player-specific layers by name
                for (int i = 0; i < 32; i++)
                {
                    string layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        string lower = layerName.ToLower();
                        // Exclude player/character layers
                        if (lower.Contains("player") || lower.Contains("character") || 
                            lower.Contains("actor") || lower.Contains("ragdoll"))
                        {
                            cullingMask &= ~(1 << i);
                        }
                        // Make sure to INCLUDE environment layers
                        else if (lower.Contains("water") || lower.Contains("terrain") ||
                                 lower.Contains("default") || lower.Contains("environment") ||
                                 lower.Contains("ground") || lower.Contains("static") ||
                                 lower.Contains("prop") || lower.Contains("object"))
                        {
                            cullingMask |= (1 << i);
                        }
                    }
                }
                
                // Always include layer 0 (Default) and 4 (Water)
                cullingMask |= (1 << 0);
                cullingMask |= (1 << 4); // Water layer
                
                // Find and explicitly include ocean/water GameObjects
                GameObject[] allObjs = UnityEngine.Object.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjs)
                {
                    if (obj != null && obj.name != null)
                    {
                        string lower = obj.name.ToLower();
                        if (lower.Contains("ocean") || lower.Contains("water") || lower.Contains("sea"))
                        {
                            int objLayer = obj.layer;
                            cullingMask |= (1 << objLayer);
                            AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Found ocean/water object: '{obj.name}' on layer {objLayer} ({LayerMask.LayerToName(objLayer)})");
                        }
                    }
                }
                
                camera.cullingMask = cullingMask;
                
                // Create temporary RenderTexture with depth buffer and antialiasing
                renderTexture = new RenderTexture(MAP_CAPTURE_SIZE, MAP_CAPTURE_SIZE, 24, RenderTextureFormat.ARGB32);
                renderTexture.antiAliasing = 4; // 4x MSAA for smoother edges
                renderTexture.filterMode = FilterMode.Bilinear;
                camera.targetTexture = renderTexture;
                
                // Force terrain to update for this camera
                Terrain[] terrains = Terrain.activeTerrains;
                foreach (var terrain in terrains)
                {
                    if (terrain != null)
                    {
                        terrain.drawHeightmap = true;
                        terrain.drawTreesAndFoliage = true;
                    }
                }
                
                // Temporarily disable all player GameObjects and admin indicators
                var disabledObjects = new List<GameObject>();
                var allPlayers = PlayerTracker.GetAllPlayers();
                foreach (var kvp in allPlayers)
                {
                    var playerData = kvp.Value;
                    if (playerData?.PlayerObject != null && playerData.PlayerObject.activeSelf)
                    {
                        playerData.PlayerObject.SetActive(false);
                        disabledObjects.Add(playerData.PlayerObject);
                    }
                }
                
                // Also disable any admin indicator objects by name pattern
                GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj != null && obj.activeSelf)
                    {
                        string name = obj.name;
                        if (name.StartsWith("RamboIndicator_") || 
                            name.StartsWith("AFKIndicator_") || 
                            name.StartsWith("AdminCylinder_") ||
                            name.StartsWith("Connection_") ||
                            name.StartsWith("LineGroupOval_"))
                        {
                            obj.SetActive(false);
                            disabledObjects.Add(obj);
                        }
                    }
                }
                
                // Log what layers we're rendering
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Camera culling mask: {cullingMask} (binary: {Convert.ToString(cullingMask, 2)})");
                AdvancedAdminUIMod.Log.LogInfo($"[Minimap] Camera position: {camera.transform.position}, FOV: {camera.fieldOfView}");
                
                // Render
                camera.Render();
                
                // Restore ambient settings
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientLight = savedAmbientLight;
                RenderSettings.ambientIntensity = savedAmbientIntensity;
                
                // Re-enable all disabled objects
                foreach (var obj in disabledObjects)
                {
                    if (obj != null)
                    {
                        obj.SetActive(true);
                    }
                }
                
                // Destroy old texture
                if (_mapTexture != null)
                {
                    UnityEngine.Object.Destroy(_mapTexture);
                    _mapTexture = null;
                }
                
                // Read pixels
                _mapTexture = new Texture2D(MAP_CAPTURE_SIZE, MAP_CAPTURE_SIZE, TextureFormat.RGB24, false);
                RenderTexture.active = renderTexture;
                _mapTexture.ReadPixels(new Rect(0, 0, MAP_CAPTURE_SIZE, MAP_CAPTURE_SIZE), 0, 0);
                _mapTexture.Apply();
                
                _hasMapCapture = true;
                _mapCaptureVersion++; // Increment so AdminWindow knows to reload the image
            }
            catch { }
            finally
            {
                // Always cleanup
                RenderTexture.active = previousActive;
                
                if (camera != null)
                    camera.targetTexture = null;
                
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.Destroy(renderTexture);
                }
                
                if (cameraObj != null)
                    UnityEngine.Object.Destroy(cameraObj);
            }
        }
        
        /// <summary>
        /// Gets the raw pixel data of the captured map texture
        /// </summary>
        public byte[] GetMapTextureBytes()
        {
            if (_mapTexture == null || !_hasMapCapture)
                return null;
            
            try
            {
                return _mapTexture.GetRawTextureData();
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Gets the dimensions of the captured map texture
        /// </summary>
        public (int width, int height) GetMapTextureDimensions()
        {
            if (_mapTexture == null)
                return (0, 0);
            return (_mapTexture.width, _mapTexture.height);
        }
        
        private void InitializeFactionColors()
        {
            // Faction colors - bright, high-contrast colors for visibility on any map
            _factionColors["British"] = new Color(1.0f, 0.2f, 0.2f);      // Bright red
            _factionColors["French"] = new Color(0.3f, 0.5f, 1.0f);       // Bright blue
            _factionColors["Prussian"] = new Color(0.9f, 0.9f, 0.9f);     // White/light gray (visible on any bg)
            _factionColors["Russian"] = new Color(0.2f, 1.0f, 0.3f);      // Bright green
            _factionColors["Austrian"] = new Color(1.0f, 1.0f, 0.3f);     // Bright yellow
            _factionColors["Italian"] = new Color(0.3f, 1.0f, 0.8f);      // Bright cyan/teal
            _factionColors["Allied"] = new Color(0.5f, 0.8f, 1.0f);       // Light blue
            _factionColors["Central"] = new Color(1.0f, 0.6f, 0.2f);      // Bright orange
        }
        
        
        // Public data structures for AdminWindow to use
        public class MinimapPlayerData
        {
            public Vector2 Position { get; set; }
            public Color Color { get; set; }
            public string FactionName { get; set; }
            public string ClassName { get; set; }
            public int PlayerId { get; set; }
            public string PlayerName { get; set; }
            public string RegimentTag { get; set; }
        }
        
        // PERFORMANCE: Reuse list to avoid GC pressure
        private List<MinimapPlayerData> _cachedMinimapData = new List<MinimapPlayerData>(256);
        
        /// <summary>
        /// Get all player data for minimap display (called from AdminWindow)
        /// Uses terrain bounds for positioning
        /// PERFORMANCE: Uses read-only player access and reuses list
        /// </summary>
        public List<MinimapPlayerData> GetMinimapPlayerData(int minimapSize)
        {
            _cachedMinimapData.Clear();
            
            if (!_terrainFound)
                return _cachedMinimapData;
            
            // PERFORMANCE: Use read-only access to avoid copying 200+ players
            var allPlayers = PlayerTracker.GetPlayersReadOnly();
            
            foreach (var kvp in allPlayers)
            {
                var playerData = kvp.Value;
                
                // Skip dead/invalid players
                if (playerData?.PlayerTransform == null)
                    continue;
                
                // Use LastKnownPosition if available (faster than accessing transform every frame)
                Vector3 worldPos = playerData.LastKnownPosition;
                if (worldPos == Vector3.zero && playerData.PlayerTransform != null)
                {
                    try { worldPos = playerData.PlayerTransform.position; }
                    catch { continue; } // Object destroyed
                }
                
                Vector2 minimapPos = WorldToMinimap(worldPos, minimapSize);
                
                // Skip if position is outside terrain bounds
                if (minimapPos.x < 0 || minimapPos.y < 0 || 
                    minimapPos.x > minimapSize || minimapPos.y > minimapSize)
                    continue;
                
                _cachedMinimapData.Add(new MinimapPlayerData
                {
                    Position = minimapPos,
                    Color = GetFactionColor(playerData.FactionName),
                    FactionName = playerData.FactionName ?? "",
                    ClassName = playerData.ClassName ?? "",
                    PlayerId = kvp.Key,
                    PlayerName = playerData.PlayerName ?? "",
                    RegimentTag = playerData.RegimentTag ?? ""
                });
            }
            
            return _cachedMinimapData;
        }
        
        /// <summary>
        /// Get terrain bounds for minimap scaling
        /// </summary>
        public (Vector3 min, Vector3 max, bool calculated) GetWorldBounds()
        {
            return (_terrainMin, _terrainMax, _terrainFound);
        }
        
        /// <summary>
        /// Converts world position to minimap coordinates using terrain bounds
        /// </summary>
        public Vector2 WorldToMinimap(Vector3 worldPos, int minimapSize)
        {
            if (!_terrainFound)
                return Vector2.zero;
            
            // Calculate terrain dimensions
            float terrainWidth = _terrainMax.x - _terrainMin.x;
            float terrainDepth = _terrainMax.z - _terrainMin.z;
            
            if (terrainWidth <= 0 || terrainDepth <= 0)
                return Vector2.zero;
            
            // Normalize world position to 0-1 range based on terrain
            float normalizedX = (worldPos.x - _terrainMin.x) / terrainWidth;
            float normalizedZ = (worldPos.z - _terrainMin.z) / terrainDepth;
            
            // Convert to minimap coordinates
            float minimapX = normalizedX * minimapSize;
            float minimapY = (1f - normalizedZ) * minimapSize; // Flip Y (Z is depth)
            
            return new Vector2(minimapX, minimapY);
        }
        
        private Color GetFactionColor(string factionName)
        {
            if (string.IsNullOrEmpty(factionName))
                return Color.white;
            
            if (_factionColors.TryGetValue(factionName, out Color color))
                return color;
            
            // Default color based on hash of faction name
            int hash = factionName.GetHashCode();
            return new Color(
                (hash & 0xFF) / 255f,
                ((hash >> 8) & 0xFF) / 255f,
                ((hash >> 16) & 0xFF) / 255f
            );
        }
        
    }
}

