using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

using AdvancedAdminUI.Features;

namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Separate WinForms window that can be moved to different monitors
    /// Uses minimal WinForms features - no Windows API P/Invoke to avoid anti-cheat issues
    /// </summary>
    public class AdminWindow
    {
        private Form _form;
        private Thread _uiThread;
        private volatile bool _isRunning = false;
        private volatile bool _shouldShow = false;
        private volatile bool _shouldHide = false;
        
        private Dictionary<string, IAdminFeature> _features;
        
        // UI Controls
        private Panel _mainPanel;
        private FlowLayoutPanel _mainFlowPanel;
        private System.Windows.Forms.Timer _refreshTimer;
        private System.Windows.Forms.Timer _minimapTimer;
        private Dictionary<string, Button> _featureButtons = new Dictionary<string, Button>();
        private Dictionary<string, Label> _featureStatusLabels = new Dictionary<string, Label>();
        
        // Minimap controls
        private PictureBox _minimapPictureBox;
        private Panel _minimapContainer;
        private Label _playerCountLabel;
        private Label _hoverInfoLabel;  // Shows info when hovering over a player
        private int _currentMinimapSize = 700; // Default size - scales with window
        private List<MinimapFeature.MinimapPlayerData> _currentMinimapPlayers = new List<MinimapFeature.MinimapPlayerData>();
        private int _lastHoveredPlayerId = -1;
        private Image _mapBackgroundImage = null; // Captured aerial photo of the map
        private bool _lastKnownHasMapCapture = false; // Track if minimap had capture last frame
        private bool _mapBackgroundLoadFailed = false; // Prevent repeated load attempts
        private int _lastKnownMapCaptureVersion = -1; // Track map version to detect recaptures
        
        // Drag-and-drop teleport state
        private bool _isDraggingPlayer = false;
        private int _draggedPlayerId = -1;
        private string _draggedPlayerName = "";
        private PointF _dragStartPos = PointF.Empty;
        private PointF _dragCurrentPos = PointF.Empty;
        private Color _draggedPlayerColor = Color.White;
        
        // Dark theme colors
        private readonly Color _bgColor = Color.FromArgb(30, 30, 30);
        private readonly Color _panelColor = Color.FromArgb(45, 45, 45);
        private readonly Color _borderColor = Color.FromArgb(60, 60, 60);
        private readonly Color _textColor = Color.FromArgb(220, 220, 220);
        private readonly Color _accentColor = Color.FromArgb(0, 120, 215);
        private readonly Color _enabledColor = Color.FromArgb(76, 175, 80);
        private readonly Color _disabledColor = Color.FromArgb(244, 67, 54);
        
        public AdminWindow()
        {
        }
        
        public void SetFeatures(Dictionary<string, IAdminFeature> features)
        {
            _features = features;
        }
        
        public void ShowWindow()
        {
            if (!_isRunning)
            {
                StartUIThread();
            }
            _shouldShow = true;
        }
        
        public void HideWindow()
        {
            _shouldHide = true;
        }
        
        public void CloseWindow()
        {
            _isRunning = false;
            _shouldHide = true;
            
            // Give thread time to close gracefully
            if (_uiThread != null && _uiThread.IsAlive)
            {
                try
                {
                    _uiThread.Join(500);
                            }
                            catch { }
            }
        }
        
        private void StartUIThread()
        {
            _isRunning = true;
            _uiThread = new Thread(UIThreadProc);
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.IsBackground = true;
            _uiThread.Name = "AdminWindow UI Thread";
            _uiThread.Start();
        }
        
        private void UIThreadProc()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                CreateForm();
                
                // Simple message loop with show/hide handling
                while (_isRunning)
                {
                    // Process pending Windows messages
                    Application.DoEvents();
                    
                    // Handle show request
                    if (_shouldShow && _form != null && !_form.IsDisposed)
                    {
                        _shouldShow = false;
                        if (!_form.Visible)
                        {
                            try
                {
                    UpdateFeatureList();
                                _form.Show();
                                if (_refreshTimer != null) _refreshTimer.Start();
                                if (_minimapTimer != null) _minimapTimer.Start();
                            }
                            catch (Exception showEx)
                            {
                                AdvancedAdminUIMod.Log.LogError($"[AdminWindow] Error showing window: {showEx.Message}\n{showEx.StackTrace}");
                            }
                        }
                    }
                    
                    // Handle hide request
                    if (_shouldHide && _form != null && !_form.IsDisposed)
                    {
                        _shouldHide = false;
                        if (_form.Visible)
                        {
                            if (_refreshTimer != null) _refreshTimer.Stop();
                            if (_minimapTimer != null) _minimapTimer.Stop();
                            _form.Hide();
                        }
                    }
                    
                    // Sleep to avoid busy loop
                    Thread.Sleep(16); // ~60 FPS
                }
                
                // Cleanup
                if (_form != null && !_form.IsDisposed)
                {
                    if (_refreshTimer != null)
                    {
                        _refreshTimer.Stop();
                        _refreshTimer.Dispose();
                    }
                    if (_minimapTimer != null)
                    {
                        _minimapTimer.Stop();
                        _minimapTimer.Dispose();
                    }
                    _form.Close();
                    _form.Dispose();
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[AdminWindow] UI thread error: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private void CreateForm()
        {
            _form = new Form
            {
                Text = "Advanced Admin UI",
                Size = new Size(1100, 900), // Balanced window size
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimumSize = new Size(400, 500), // Allow very small windows
                TopMost = false,
                ShowInTaskbar = true,
                BackColor = _bgColor,
                ForeColor = _textColor
            };
            
            // Handle close button - just hide instead of close
            _form.FormClosing += (s, e) =>
            {
                e.Cancel = true;
                _shouldHide = true;
            };
            
            // Handle resize to update minimap - use both Resize and SizeChanged for better coverage
            _form.Resize += (s, e) => UpdateMinimapSize();
            _form.SizeChanged += (s, e) => UpdateMinimapSize();
            _form.ClientSizeChanged += (s, e) => UpdateMinimapSize();
            
            // Main panel
            _mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                    Padding = new Padding(10),
                BackColor = _bgColor
            };
            
            _mainFlowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(5),
                BackColor = _bgColor
            };
            
            _mainPanel.Controls.Add(_mainFlowPanel);
            _form.Controls.Add(_mainPanel);
            
            // Create refresh timer (but don't start it yet)
            _refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 500
            };
            _refreshTimer.Tick += (s, e) => UpdateGroupStatus();
            
            // Create minimap timer (reduced frequency for better performance)
            _minimapTimer = new System.Windows.Forms.Timer
            {
                Interval = 500  // 2 FPS for minimap - reduced for performance
            };
            _minimapTimer.Tick += (s, e) => UpdateMinimap();
        }
        
        private void UpdateFeatureList()
        {
            try
            {
                if (_form == null || _form.IsDisposed || _mainFlowPanel == null)
                return;
                
                _mainFlowPanel.Controls.Clear();
                _featureButtons.Clear();
                _featureStatusLabels.Clear();
                
                // Title
                var titleLabel = new Label
                {
                    Text = "Advanced Admin UI",
                    Font = new Font("Segoe UI", 16, FontStyle.Bold),
                    ForeColor = _accentColor,
                    AutoSize = true,
                    Padding = new Padding(0, 0, 0, 10)
                };
                _mainFlowPanel.Controls.Add(titleLabel);
                
                // Features section
                var featuresHeader = new Label
                {
                    Text = "Features",
                    Font = new Font("Segoe UI", 12, FontStyle.Bold),
                    ForeColor = _textColor,
                    AutoSize = true,
                    Padding = new Padding(0, 10, 0, 5)
                };
                _mainFlowPanel.Controls.Add(featuresHeader);
                
                if (_features != null)
                {
                    foreach (var kvp in _features)
                    {
                        CreateFeaturePanel(kvp.Key, kvp.Value);
                    }
                }
                
                // Group Status section
                var groupHeader = new Label
                {
                    Text = "Group Status",
                    Font = new Font("Segoe UI", 12, FontStyle.Bold),
                    ForeColor = _textColor,
                    AutoSize = true,
                    Padding = new Padding(0, 15, 0, 5),
                    Tag = "group_header"
                };
                _mainFlowPanel.Controls.Add(groupHeader);
                
                UpdateGroupStatus();
                
                // Minimap section
                var minimapHeader = new Label
                {
                    Text = "Minimap",
                    Font = new Font("Segoe UI", 12, FontStyle.Bold),
                    ForeColor = _textColor,
                    AutoSize = true,
                    Padding = new Padding(0, 15, 0, 5),
                    Tag = "minimap_header"
                };
                _mainFlowPanel.Controls.Add(minimapHeader);
                
                // Calculate minimap size based on window width (scale with window)
                UpdateMinimapSize();
                
                // Minimap container panel (wider to fit legend)
                _minimapContainer = new Panel
                {
                    Width = _currentMinimapSize + 180,
                    Height = _currentMinimapSize + 100, // Extra space for controls + selection buttons
                    BackColor = _panelColor,
                    Margin = new Padding(0, 3, 0, 3),
                    Tag = "minimap_container"
                };
                
                // Create PictureBox for minimap
                _minimapPictureBox = new PictureBox
                {
                    Size = new Size(_currentMinimapSize, _currentMinimapSize),
                    Location = new Point(10, 10),
                    BackColor = Color.FromArgb(20, 20, 20),
                    BorderStyle = BorderStyle.FixedSingle
                };
                
                // Add mouse handlers for hover info and selection mode
                _minimapPictureBox.MouseMove += MinimapPictureBox_MouseMove;
                _minimapPictureBox.MouseDown += MinimapPictureBox_MouseDown;
                _minimapPictureBox.MouseUp += MinimapPictureBox_MouseUp;
                _minimapPictureBox.MouseLeave += (s, e) => { 
                    _lastHoveredPlayerId = -1; 
                    
                    // Cancel any ongoing drag
                    if (_isDraggingPlayer)
                    {
                        _isDraggingPlayer = false;
                        _draggedPlayerId = -1;
                        _draggedPlayerName = "";
                        _dragStartPos = PointF.Empty;
                        _dragCurrentPos = PointF.Empty;
                        AdvancedAdminUIMod.Log.LogInfo("[AdminWindow] Drag cancelled - mouse left minimap");
                    }
                    
                    if (_hoverInfoLabel != null) 
                    {
                        _hoverInfoLabel.Text = "Click & drag a player to teleport them";
                        _hoverInfoLabel.ForeColor = Color.Gray;
                    }
                    
                    if (_minimapPictureBox != null)
                        _minimapPictureBox.Cursor = Cursors.Default;
                };
                
                _minimapContainer.Controls.Add(_minimapPictureBox);
                
                // Create legend panel
                var legendPanel = CreateLegendPanel();
                legendPanel.Location = new Point(_currentMinimapSize + 20, 10);
                _minimapContainer.Controls.Add(legendPanel);
            
                // Player count label
                _playerCountLabel = new Label
                {
                    Text = "Players: 0",
                    Font = new Font("Segoe UI", 9),
                    ForeColor = _textColor,
                    Location = new Point(10, _currentMinimapSize + 15),
                        AutoSize = true,
                    Tag = "player_count"
                };
                _minimapContainer.Controls.Add(_playerCountLabel);
                
                // Hover info label (shows player info when hovering)
                _hoverInfoLabel = new Label
                {
                    Text = "Click & drag a player to teleport them",
                    Font = new Font("Segoe UI", 9),
                    ForeColor = Color.Gray,
                    Location = new Point(120, _currentMinimapSize + 15),
                    AutoSize = true,
                    Tag = "hover_info"
                };
                _minimapContainer.Controls.Add(_hoverInfoLabel);
                
                // Add selection mode buttons
                var selectionButton = new Button
                {
                    Text = "📐 Set Capture Area",
                    Font = new Font("Segoe UI", 9),
                    Size = new Size(130, 28),
                    Location = new Point(10, _currentMinimapSize + 45),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = _textColor,
                    Tag = "selection_button"
                };
                selectionButton.FlatAppearance.BorderColor = _borderColor;
                selectionButton.Click += (s, e) => OnSetCaptureAreaClick();
                _minimapContainer.Controls.Add(selectionButton);
                
                var clearButton = new Button
                {
                    Text = "❌ Clear Custom",
                    Font = new Font("Segoe UI", 9),
                    Size = new Size(110, 28),
                    Location = new Point(150, _currentMinimapSize + 45),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = _textColor,
                    Tag = "clear_button"
                };
                clearButton.FlatAppearance.BorderColor = _borderColor;
                clearButton.Click += (s, e) => OnClearCustomAreaClick();
                _minimapContainer.Controls.Add(clearButton);
                
                _mainFlowPanel.Controls.Add(_minimapContainer);
                
                // Initial minimap update
                UpdateMinimap();
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogError($"[AdminWindow] Error updating feature list: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private Panel CreateLegendPanel()
        {
            var panel = new Panel
            {
                Width = 150,
                Height = 400,
                BackColor = Color.FromArgb(35, 35, 35),
                Tag = "legend_panel"
            };
            
            int yPos = 10;
            
            // Legend title
            var titleLabel = new Label
            {
                Text = "Class Shapes",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = _accentColor,
                Location = new Point(10, yPos),
                AutoSize = true
            };
            panel.Controls.Add(titleLabel);
            yPos += 22;
            
            // Class shape legend items - matching DrawPlayerIcon
            AddLegendItem(panel, ref yPos, "●", "Line Infantry", Color.White);
            AddLegendItem(panel, ref yPos, "★", "Officer/Flag", Color.White);
            AddLegendItem(panel, ref yPos, "◆", "Cavalry", Color.White);
            AddLegendItem(panel, ref yPos, "▲", "Skirmisher", Color.White);
            AddLegendItem(panel, ref yPos, "■", "Artillery/Sapper", Color.White);
            AddLegendItem(panel, ref yPos, "⬠", "Musician", Color.White);
            AddLegendItem(panel, ref yPos, "◉", "Guard/Grenadier", Color.White);
            
            yPos += 12;
            
            // Faction colors title
            var factionTitle = new Label
            {
                Text = "Faction Colors",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = _accentColor,
                Location = new Point(10, yPos),
                AutoSize = true
            };
            panel.Controls.Add(factionTitle);
            yPos += 22;
            
            // Faction color legend - matching MinimapFeature colors (bright, high-contrast)
            AddLegendItem(panel, ref yPos, "●", "British", Color.FromArgb(255, 51, 51));     // Bright red
            AddLegendItem(panel, ref yPos, "●", "French", Color.FromArgb(77, 128, 255));     // Bright blue
            AddLegendItem(panel, ref yPos, "●", "Prussian", Color.FromArgb(230, 230, 230)); // White/light gray
            AddLegendItem(panel, ref yPos, "●", "Russian", Color.FromArgb(51, 255, 77));     // Bright green
            AddLegendItem(panel, ref yPos, "●", "Austrian", Color.FromArgb(255, 255, 77));   // Bright yellow
            AddLegendItem(panel, ref yPos, "●", "Italian", Color.FromArgb(77, 255, 204));    // Bright cyan
            AddLegendItem(panel, ref yPos, "●", "Allied", Color.FromArgb(128, 204, 255));    // Light blue
            AddLegendItem(panel, ref yPos, "●", "Central", Color.FromArgb(255, 153, 51));    // Bright orange
            
            return panel;
        }
        
        private void AddLegendItem(Panel panel, ref int yPos, string symbol, string text, Color color)
        {
            var symbolLabel = new Label
            {
                Text = symbol,
                Font = new Font("Segoe UI", 10),
                ForeColor = color,
                Location = new Point(10, yPos),
                    AutoSize = true
                };
            panel.Controls.Add(symbolLabel);
            
            var textLabel = new Label
            {
                Text = text,
                        Font = new Font("Segoe UI", 9),
                        ForeColor = _textColor,
                Location = new Point(30, yPos + 1),
                        AutoSize = true
                    };
            panel.Controls.Add(textLabel);
            
            yPos += 18;
        }
        
        private void MinimapPictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            // Handle player dragging for teleport
            if (_isDraggingPlayer)
            {
                _dragCurrentPos = new PointF(e.X, e.Y);
                
                // Update hover info to show teleport in progress
                if (_hoverInfoLabel != null)
                {
                    _hoverInfoLabel.Text = $"🎯 Dragging {_draggedPlayerName} - Release to teleport";
                    _hoverInfoLabel.ForeColor = Color.Yellow;
                }
                
                UpdateMinimap(); // Redraw to show drag indicator
                return;
            }
            
            if (_currentMinimapPlayers == null || _currentMinimapPlayers.Count == 0 || _hoverInfoLabel == null)
                return;
            
            float dotSize = Math.Max(3f, _currentMinimapSize / 150f);
            float hoverRadius = dotSize + 8; // Larger for easier hovering since icons are smaller
            
            // Find player near cursor
            MinimapFeature.MinimapPlayerData hoveredPlayer = null;
            float closestDist = float.MaxValue;
            
            foreach (var player in _currentMinimapPlayers)
            {
                float dx = e.X - player.Position.x;
                float dy = e.Y - player.Position.y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                
                if (dist < hoverRadius && dist < closestDist)
                {
                    closestDist = dist;
                    hoveredPlayer = player;
                }
            }
            
            if (hoveredPlayer != null)
            {
                // Change cursor to hand when hovering over player
                if (_minimapPictureBox != null)
                    _minimapPictureBox.Cursor = Cursors.Hand;
                
                if (_lastHoveredPlayerId != hoveredPlayer.PlayerId)
                {
                    _lastHoveredPlayerId = hoveredPlayer.PlayerId;
                    
                    // Build player name with optional regiment tag
                    string displayName = hoveredPlayer.PlayerName;
                    if (string.IsNullOrEmpty(displayName))
                        displayName = $"Player #{hoveredPlayer.PlayerId}";
                    
                    if (!string.IsNullOrEmpty(hoveredPlayer.RegimentTag))
                        displayName = $"[{hoveredPlayer.RegimentTag}] {displayName}";
                    
                    // Update hover info label with full player info + teleport hint
                    _hoverInfoLabel.Text = $"{displayName} | {hoveredPlayer.ClassName ?? "?"} | {hoveredPlayer.FactionName ?? "?"} | 🖱️ Drag to teleport";
                    _hoverInfoLabel.ForeColor = _textColor;
                }
            }
            else
            {
                // Reset cursor when not over a player
                if (_minimapPictureBox != null)
                    _minimapPictureBox.Cursor = Cursors.Default;
                
                if (_lastHoveredPlayerId != -1)
                {
                    _lastHoveredPlayerId = -1;
                    _hoverInfoLabel.Text = "Click & drag a player to teleport them";
                    _hoverInfoLabel.ForeColor = Color.Gray;
                }
            }
            
            // Update selection during drag if in selection mode
            var minimapFeature = GetMinimapFeature();
            if (minimapFeature != null && minimapFeature.IsSelectionMode && minimapFeature.IsSelecting)
            {
                minimapFeature.UpdateSelection(new UnityEngine.Vector2(e.X, e.Y));
                UpdateMinimap(); // Redraw to show selection rectangle
            }
        }
        
        private void MinimapPictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            
            var minimapFeature = GetMinimapFeature();
            
            // Check if we're in selection mode first
            if (minimapFeature != null && minimapFeature.IsSelectionMode)
            {
                minimapFeature.StartSelection(new UnityEngine.Vector2(e.X, e.Y));
                return;
            }
            
            // Check if clicking on a player for teleport drag
            if (_currentMinimapPlayers != null && _currentMinimapPlayers.Count > 0)
            {
                float dotSize = Math.Max(3f, _currentMinimapSize / 150f);
                float clickRadius = dotSize + 10; // Slightly larger for easier clicking
                
                MinimapFeature.MinimapPlayerData clickedPlayer = null;
                float closestDist = float.MaxValue;
                
                foreach (var player in _currentMinimapPlayers)
                {
                    float dx = e.X - player.Position.x;
                    float dy = e.Y - player.Position.y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    
                    if (dist < clickRadius && dist < closestDist)
                    {
                        closestDist = dist;
                        clickedPlayer = player;
                    }
                }
                
                if (clickedPlayer != null)
                {
                    // Start dragging this player
                    _isDraggingPlayer = true;
                    _draggedPlayerId = clickedPlayer.PlayerId;
                    _draggedPlayerName = clickedPlayer.PlayerName ?? $"Player #{clickedPlayer.PlayerId}";
                    if (!string.IsNullOrEmpty(clickedPlayer.RegimentTag))
                        _draggedPlayerName = $"[{clickedPlayer.RegimentTag}] {_draggedPlayerName}";
                    
                    _dragStartPos = new PointF(clickedPlayer.Position.x, clickedPlayer.Position.y);
                    _dragCurrentPos = new PointF(e.X, e.Y);
                    _draggedPlayerColor = Color.FromArgb(
                        (int)(clickedPlayer.Color.r * 255),
                        (int)(clickedPlayer.Color.g * 255),
                        (int)(clickedPlayer.Color.b * 255)
                    );
                    
                    // Change cursor
                    if (_minimapPictureBox != null)
                        _minimapPictureBox.Cursor = Cursors.Cross;
                    
                    AdvancedAdminUIMod.Log.LogInfo($"[AdminWindow] Started dragging player: {_draggedPlayerName} (ID: {_draggedPlayerId})");
                }
            }
        }
        
        private void MinimapPictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            
            // Handle player teleport drop
            if (_isDraggingPlayer)
            {
                // Calculate distance dragged
                float dx = e.X - _dragStartPos.X;
                float dy = e.Y - _dragStartPos.Y;
                float dragDistance = (float)Math.Sqrt(dx * dx + dy * dy);
                
                // Only teleport if dragged a meaningful distance (not just a click)
                if (dragDistance > 15)
                {
                    ExecuteTeleportFromMinimap(_draggedPlayerId, new PointF(e.X, e.Y));
                }
                else
                {
                    AdvancedAdminUIMod.Log.LogInfo($"[AdminWindow] Drag cancelled - distance too short ({dragDistance:F0}px)");
                }
                
                // Reset drag state
                _isDraggingPlayer = false;
                _draggedPlayerId = -1;
                _draggedPlayerName = "";
                _dragStartPos = PointF.Empty;
                _dragCurrentPos = PointF.Empty;
                
                // Reset cursor
                if (_minimapPictureBox != null)
                    _minimapPictureBox.Cursor = Cursors.Default;
                
                // Reset hover info
                if (_hoverInfoLabel != null)
                {
                    _hoverInfoLabel.Text = "Click & drag a player to teleport them";
                    _hoverInfoLabel.ForeColor = Color.Gray;
                }
                
                UpdateMinimap();
                return;
            }
            
            var minimapFeature = GetMinimapFeature();
            if (minimapFeature != null && minimapFeature.IsSelectionMode && minimapFeature.IsSelecting)
            {
                minimapFeature.FinishSelection(new UnityEngine.Vector2(e.X, e.Y), _currentMinimapSize);
                UpdateMinimap(); // Redraw without selection rectangle
            }
        }
        
        /// <summary>
        /// Executes a teleport command for a player to a minimap position
        /// Queues the request to be executed on Unity main thread (avoids cross-thread crashes)
        /// </summary>
        private void ExecuteTeleportFromMinimap(int playerId, PointF minimapPos)
        {
            var minimapFeature = GetMinimapFeature();
            if (minimapFeature == null)
            {
                AdvancedAdminUIMod.Log.LogWarning("[AdminWindow] Cannot teleport - minimap feature not available");
                return;
            }
            
            // Queue the teleport to be executed on Unity main thread
            // This avoids crashes from calling Unity APIs (Physics.Raycast, InputField) from WinForms thread
            minimapFeature.QueueTeleport(
                playerId, 
                _draggedPlayerName,
                minimapPos.X, 
                minimapPos.Y, 
                _currentMinimapSize
            );
            
            // Show feedback in hover label (this is safe - WinForms UI on WinForms thread)
            if (_hoverInfoLabel != null)
            {
                _hoverInfoLabel.Text = $"✓ Teleporting {_draggedPlayerName}...";
                _hoverInfoLabel.ForeColor = Color.Lime;
            }
        }
        
        private void OnSetCaptureAreaClick()
        {
            var minimapFeature = GetMinimapFeature();
            if (minimapFeature == null)
            {
                AdvancedAdminUIMod.Log.LogWarning("[AdminWindow] Minimap feature not available");
                return;
            }
            
            if (string.IsNullOrEmpty(minimapFeature.CurrentMapName))
            {
                AdvancedAdminUIMod.Log.LogWarning("[AdminWindow] Map name not known yet - wait for OnRoundDetails");
                return;
            }
            
            if (minimapFeature.IsSelectionMode)
            {
                minimapFeature.ExitSelectionMode();
                AdvancedAdminUIMod.Log.LogInfo("[AdminWindow] Exited selection mode");
            }
            else
            {
                minimapFeature.EnterSelectionMode();
                AdvancedAdminUIMod.Log.LogInfo($"[AdminWindow] Drag a rectangle on the minimap to set capture area for '{minimapFeature.CurrentMapName}'");
            }
            UpdateMinimap();
        }
        
        private void OnClearCustomAreaClick()
        {
            var minimapFeature = GetMinimapFeature();
            if (minimapFeature != null)
            {
                minimapFeature.ClearCustomBounds();
            }
        }
        
        private MinimapFeature GetMinimapFeature()
        {
            if (_features != null && _features.TryGetValue("minimap", out var feature))
            {
                return feature as MinimapFeature;
            }
            return null;
        }
        
        private void UpdateMinimapSize()
        {
            try
            {
                if (_form == null || _form.IsDisposed) return;
                
                // Get available dimensions
                int formWidth = _form.ClientSize.Width;
                int formHeight = _form.ClientSize.Height;
                if (formWidth <= 0) formWidth = 800;
                if (formHeight <= 0) formHeight = 600;
                
                // Determine if we should show the legend (hide on small windows to save space)
                bool showLegend = formWidth > 700;
                int legendWidth = showLegend ? 170 : 0;
                
                // Calculate available width: form width - legend - padding - scrollbar margin
                int availableWidth = formWidth - legendWidth - 50;
                
                // Calculate height used by controls ABOVE the minimap
                int heightAboveMinimap = 0;
                if (_mainFlowPanel != null && _minimapContainer != null)
                {
                    foreach (System.Windows.Forms.Control ctrl in _mainFlowPanel.Controls)
                    {
                        // Stop counting when we reach the minimap container
                        if (ctrl == _minimapContainer)
                            break;
                        
                        // Add up all heights including margins
                        heightAboveMinimap += ctrl.Height + ctrl.Margin.Top + ctrl.Margin.Bottom;
                    }
                }
                
                // If we couldn't measure (early call), estimate
                if (heightAboveMinimap < 50)
                    heightAboveMinimap = 300; // Rough estimate: title + features + group status
                
                // Available height = form height - content above minimap - controls below minimap - padding
                int controlsBelowMinimap = 90; // Player count label + buttons + padding
                int scrollbarAndPadding = 40;
                int availableHeight = formHeight - heightAboveMinimap - controlsBelowMinimap - scrollbarAndPadding;
                
                // Use the smaller dimension to ensure nothing gets cut off
                int targetSize = Math.Min(availableWidth, availableHeight);
                
                // Clamp to reasonable bounds - allow very small (150px) for tiny windows
                int newMinimapSize = Math.Max(150, Math.Min(1400, targetSize));
                
                // Only update if size actually changed (avoid unnecessary redraws)
                if (newMinimapSize == _currentMinimapSize)
                    return;
                    
                _currentMinimapSize = newMinimapSize;
                
                // Suspend layout while making multiple changes
                if (_minimapContainer != null && !_minimapContainer.IsDisposed)
                {
                    _minimapContainer.SuspendLayout();
                    
                    // Container width: minimap + legend (if shown) + small margin
                    _minimapContainer.Width = _currentMinimapSize + legendWidth + 20;
                    _minimapContainer.Height = _currentMinimapSize + 80; // Space for controls below
                    
                    // Update legend visibility and position
                    foreach (System.Windows.Forms.Control ctrl in _minimapContainer.Controls)
                    {
                        if (ctrl.Tag?.ToString() == "legend_panel")
                        {
                            ctrl.Visible = showLegend;
                            if (showLegend)
                            {
                                ctrl.Location = new Point(_currentMinimapSize + 15, 10);
                            }
                        }
                    }
                    
                    _minimapContainer.ResumeLayout(true);
                }
                
                if (_minimapPictureBox != null && !_minimapPictureBox.IsDisposed)
                {
                    _minimapPictureBox.Size = new Size(_currentMinimapSize, _currentMinimapSize);
                }
                
                if (_playerCountLabel != null && !_playerCountLabel.IsDisposed)
                {
                    _playerCountLabel.Location = new Point(10, _currentMinimapSize + 12);
                }
                
                if (_hoverInfoLabel != null && !_hoverInfoLabel.IsDisposed)
                {
                    _hoverInfoLabel.Location = new Point(100, _currentMinimapSize + 12);
                }
                
                // Update selection button positions
                if (_minimapContainer != null && !_minimapContainer.IsDisposed)
                {
                    foreach (System.Windows.Forms.Control ctrl in _minimapContainer.Controls)
                    {
                        if (ctrl.Tag?.ToString() == "selection_button")
                        {
                            ctrl.Location = new Point(10, _currentMinimapSize + 35);
                        }
                        else if (ctrl.Tag?.ToString() == "clear_button")
                        {
                            ctrl.Location = new Point(145, _currentMinimapSize + 35);
                        }
                    }
                }
                
                // Force parent layout to recalculate
                if (_mainFlowPanel != null && !_mainFlowPanel.IsDisposed)
                {
                    _mainFlowPanel.PerformLayout();
                }
                if (_mainPanel != null && !_mainPanel.IsDisposed)
                {
                    _mainPanel.PerformLayout();
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[AdminWindow] Error updating minimap size: {ex.Message}");
            }
        }
        
        private void CreateFeaturePanel(string key, IAdminFeature feature)
        {
            var panel = new Panel
            {
                Width = _mainPanel.Width - 40,
                Height = 60,
                BackColor = _panelColor,
                Margin = new Padding(0, 3, 0, 3),
                Padding = new Padding(10)
            };
            
            // Feature name
            var nameLabel = new Label
            {
                Text = feature.FeatureName,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    ForeColor = _textColor,
                        Location = new Point(10, 10),
                    AutoSize = true
                };
            panel.Controls.Add(nameLabel);
            
            // Status
            var statusLabel = new Label
            {
                Text = feature.IsEnabled ? "● Enabled" : "○ Disabled",
                        Font = new Font("Segoe UI", 9),
                ForeColor = feature.IsEnabled ? _enabledColor : _disabledColor,
                Location = new Point(10, 32),
                        AutoSize = true
                    };
            _featureStatusLabels[key] = statusLabel;
            panel.Controls.Add(statusLabel);
            
            // Toggle button
            var button = new Button
            {
                Text = feature.IsEnabled ? "Disable" : "Enable",
                Size = new Size(70, 30),
                Location = new Point(panel.Width - 90, 15),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = feature.IsEnabled ? Color.FromArgb(180, 60, 60) : Color.FromArgb(60, 120, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) => ToggleFeature(key, feature, button, statusLabel);
            _featureButtons[key] = button;
            panel.Controls.Add(button);
            
            _mainFlowPanel.Controls.Add(panel);
        }
        
        private void ToggleFeature(string key, IAdminFeature feature, Button button, Label statusLabel)
        {
            try
        {
            if (feature.IsEnabled)
                feature.Disable();
            else
                feature.Enable();
            
                // Update UI
                button.Text = feature.IsEnabled ? "Disable" : "Enable";
                button.BackColor = feature.IsEnabled ? Color.FromArgb(180, 60, 60) : Color.FromArgb(60, 120, 60);
                statusLabel.Text = feature.IsEnabled ? "● Enabled" : "○ Disabled";
                statusLabel.ForeColor = feature.IsEnabled ? _enabledColor : _disabledColor;
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[AdminWindow] Error toggling {key}: {ex.Message}");
            }
        }
        
        private void UpdateGroupStatus()
        {
            if (_form == null || _form.IsDisposed || _mainFlowPanel == null || !_form.Visible)
                return;
            
            // Remove all group status items (tagged with "group_status_item")
            var toRemove = new List<System.Windows.Forms.Control>();
            foreach (System.Windows.Forms.Control ctrl in _mainFlowPanel.Controls)
            {
                string tag = ctrl.Tag?.ToString() ?? "";
                if (tag == "group_status_item")
                {
                    toRemove.Add(ctrl);
                }
            }
            foreach (var ctrl in toRemove)
            {
                _mainFlowPanel.Controls.Remove(ctrl);
                ctrl.Dispose();
            }
            
            if (_features == null)
                return;
            
            // Get Rambo feature for group data
            if (!_features.TryGetValue("rambo", out IAdminFeature ramboRaw) || 
                !(ramboRaw is RamboIndicatorFeature ramboFeature))
            {
                AddGroupStatusLabel("⚠ Rambo Indicator not found", _disabledColor);
                return;
            }
            
            if (!ramboFeature.IsEnabled)
            {
                AddGroupStatusLabel("⚠ Enable Rambo Indicator to see groups", _disabledColor);
                return;
            }
            
            var groupData = ramboFeature.GetGroupDisplayData();
            
            // Filter for cavalry
            var cavalryGroups = new List<RamboIndicatorFeature.GroupDisplayData>();
            foreach (var group in groupData)
            {
                if (group.Composition?.ClassCounts == null) continue;
                foreach (var classKvp in group.Composition.ClassCounts)
                {
                    string className = classKvp.Key ?? "";
                    if (className.IndexOf("hussar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        className.IndexOf("dragoon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cavalryGroups.Add(group);
                    break;
                }
                }
            }
            
            if (cavalryGroups.Count == 0)
            {
                AddGroupStatusLabel("No cavalry groups found", Color.Gray);
                return;
            }
            
            foreach (var group in cavalryGroups)
            {
                AddGroupPanel(group);
            }
        }
        
        private void AddGroupStatusLabel(string text, Color color)
        {
            var label = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 10),
                ForeColor = color,
                AutoSize = true,
                Padding = new Padding(5),
                Tag = "group_status_item"  // Tag so it gets cleaned up properly
            };
            _mainFlowPanel.Controls.Add(label);
        }
        
        private void AddGroupPanel(RamboIndicatorFeature.GroupDisplayData group)
        {
            var panel = new Panel
            {
                Width = _mainPanel.Width - 40,
                Height = 50,
                BackColor = _panelColor,
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(8),
                Tag = "group_status_item"  // Tag so it gets cleaned up properly
            };
            
            string icon = group.IsValid ? "✓" : "✗";
            string groupType = GetGroupType(group.Composition);
            Color statusColor = group.IsValid ? _enabledColor : _disabledColor;
            
            var statusLabel = new Label
            {
                Text = $"{icon} {groupType} (Size: {group.ComponentSize})",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = statusColor,
                Location = new Point(8, 8),
                AutoSize = true
            };
            panel.Controls.Add(statusLabel);
            
            if (!group.IsValid && !string.IsNullOrEmpty(group.ValidationReason))
            {
                var reasonLabel = new Label
                {
                    Text = group.ValidationReason,
                    Font = new Font("Segoe UI", 8),
                    ForeColor = _disabledColor,
                    Location = new Point(8, 28),
                    AutoSize = true
                };
                panel.Controls.Add(reasonLabel);
            }
            
            _mainFlowPanel.Controls.Add(panel);
        }
        
        private string GetGroupType(RamboIndicatorFeature.ComponentComposition composition)
        {
            if (composition?.ClassCounts == null) return "Unknown";
            
            foreach (var kvp in composition.ClassCounts)
            {
                string key = kvp.Key?.ToLower() ?? "";
                if (key.Contains("dragoon")) return "Cavalry (Dragoon)";
                if (key.Contains("hussar")) return "Cavalry (Hussar)";
            }
            return "Mixed";
        }
        
        private void UpdateMinimap()
        {
            if (_form == null || _form.IsDisposed || _minimapPictureBox == null || !_form.Visible)
                return;
            
            if (_features == null)
                return;
            
            // Get Minimap feature
            if (!_features.TryGetValue("minimap", out IAdminFeature minimapRaw) || 
                !(minimapRaw is MinimapFeature minimapFeature))
                return;
            
            if (!minimapFeature.IsEnabled)
            {
                // Clear cached map image when disabled
                if (_mapBackgroundImage != null)
                {
                    _mapBackgroundImage.Dispose();
                    _mapBackgroundImage = null;
                }
                _mapBackgroundLoadFailed = false;
                _lastKnownHasMapCapture = false;
                _lastKnownMapCaptureVersion = -1; // Reset version so next enable reloads
                
                // Draw "disabled" message
                DrawMinimapDisabled();
                return;
            }
            
            // Detect if a new map photo was captured
            // Check version number to detect recaptures (e.g., after custom bounds selection)
            bool hasCapture = minimapFeature.HasMapCapture;
            int currentVersion = minimapFeature.MapCaptureVersion;
            
            if (currentVersion != _lastKnownMapCaptureVersion && hasCapture)
            {
                // New capture detected (different version) - clear old cached image
                if (_mapBackgroundImage != null)
                {
                    _mapBackgroundImage.Dispose();
                    _mapBackgroundImage = null;
                }
                _mapBackgroundLoadFailed = false; // Reset failure flag for new capture
                _lastKnownMapCaptureVersion = currentVersion;
                AdvancedAdminUIMod.Log.LogInfo($"[AdminWindow] New map capture detected (version {currentVersion}) - will load new background");
            }
            else if (_lastKnownHasMapCapture && !hasCapture)
            {
                // Capture was reset (new round) - clear cached image
                if (_mapBackgroundImage != null)
                {
                    _mapBackgroundImage.Dispose();
                    _mapBackgroundImage = null;
                }
                _mapBackgroundLoadFailed = false; // Reset failure flag
            }
            _lastKnownHasMapCapture = hasCapture;
            
            // Try to load map background if available (only once - don't retry on failure)
            if (_mapBackgroundImage == null && minimapFeature.HasMapCapture && !_mapBackgroundLoadFailed)
            {
                try
                {
                    byte[] mapBytes = minimapFeature.GetMapTextureBytes();
                    var (width, height) = minimapFeature.GetMapTextureDimensions();
                    
                    if (mapBytes != null && mapBytes.Length > 0 && width > 0 && height > 0)
                    {
                        // Convert raw RGB24 data to Bitmap - this is expensive, only do once!
                        _mapBackgroundImage = ConvertRawToBitmap(mapBytes, width, height);
                        AdvancedAdminUIMod.Log.LogInfo($"[AdminWindow] Loaded map background image: {width}x{height}");
                    }
                    else
                    {
                        _mapBackgroundLoadFailed = true;
                        AdvancedAdminUIMod.Log.LogWarning("[AdminWindow] Map texture data was empty or invalid");
                    }
                }
                catch (Exception ex)
                {
                    _mapBackgroundLoadFailed = true;
                    AdvancedAdminUIMod.Log.LogWarning($"[AdminWindow] Failed to load map background: {ex.Message}");
                }
            }
            
            // Get player data
            var playerData = minimapFeature.GetMinimapPlayerData(_currentMinimapSize);
            
            // Store for hover detection
            _currentMinimapPlayers = playerData;
            
            // Update player count label
            UpdatePlayerCountLabel(playerData.Count);
            
            // Draw minimap
            DrawMinimap(playerData);
        }
        
        private void UpdatePlayerCountLabel(int spawnedCount)
        {
            if (_playerCountLabel != null)
            {
                // Get total tracked players from PlayerTracker
                int totalPlayers = AdvancedAdminUI.Utils.PlayerTracker.GetPlayerCount();
                _playerCountLabel.Text = $"Spawned: {spawnedCount} / Total: {totalPlayers}";
            }
        }
        
        private void DrawMinimapDisabled()
        {
            if (_minimapPictureBox == null) return;
            
            try
            {
                Bitmap bmp = new Bitmap(_currentMinimapSize, _currentMinimapSize);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(20, 20, 20));
                    
                    using (Font font = new Font("Segoe UI", 12))
                    using (SolidBrush brush = new SolidBrush(_disabledColor))
                    {
                        string text = "Enable Minimap feature";
                        SizeF textSize = g.MeasureString(text, font);
                        g.DrawString(text, font, brush,
                            (_currentMinimapSize - textSize.Width) / 2f,
                            (_currentMinimapSize - textSize.Height) / 2f);
                    }
                }
                
                var oldImage = _minimapPictureBox.Image;
                _minimapPictureBox.Image = bmp;
                oldImage?.Dispose();
            }
            catch { }
        }
        
        private void DrawMinimap(List<MinimapFeature.MinimapPlayerData> playerData)
        {
            if (_minimapPictureBox == null) return;
            
            try
            {
                Bitmap bmp = new Bitmap(_currentMinimapSize, _currentMinimapSize);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    
                    // Draw map background if available, otherwise solid color
                    if (_mapBackgroundImage != null)
                    {
                        g.DrawImage(_mapBackgroundImage, 0, 0, _currentMinimapSize, _currentMinimapSize);
                    }
                    else
                    {
                        g.Clear(Color.FromArgb(20, 20, 20));
                    }
                    
                    // Border
                    using (Pen borderPen = new Pen(_borderColor, 2))
                    {
                        g.DrawRectangle(borderPen, 1, 1, _currentMinimapSize - 2, _currentMinimapSize - 2);
                    }
                    
                    if (playerData.Count == 0)
                    {
                        // No players message
                        using (Font font = new Font("Segoe UI", 12))
                        using (SolidBrush brush = new SolidBrush(Color.Gray))
                        {
                            string text = "No players detected";
                            SizeF textSize = g.MeasureString(text, font);
                            g.DrawString(text, font, brush,
                                (_currentMinimapSize - textSize.Width) / 2f,
                                (_currentMinimapSize - textSize.Height) / 2f);
                            }
                        }
                        else
                        {
                        // Draw player dots (scale dot size with minimap)
                        float dotSize = Math.Max(3f, _currentMinimapSize / 150f);
                        foreach (var player in playerData)
                        {
                            // Convert Unity Color to System.Drawing.Color
                            Color drawColor = Color.FromArgb(
                                (int)(player.Color.r * 255),
                                (int)(player.Color.g * 255),
                                (int)(player.Color.b * 255)
                            );
                            
                            float x = player.Position.x - dotSize / 2f;
                            float y = player.Position.y - dotSize / 2f;
                            
                            // Clamp to bounds
                            x = Math.Max(0, Math.Min(_currentMinimapSize - dotSize, x));
                            y = Math.Max(0, Math.Min(_currentMinimapSize - dotSize, y));
                            
                            // Draw black outline first (slightly larger)
                            using (SolidBrush outlineBrush = new SolidBrush(Color.Black))
                            {
                                float outlineSize = dotSize + 2f;
                                float outlineX = x - 1f;
                                float outlineY = y - 1f;
                                DrawPlayerIcon(g, outlineBrush, outlineX, outlineY, outlineSize, player.ClassName);
                            }
                            
                            // Draw colored fill on top
                            using (SolidBrush brush = new SolidBrush(drawColor))
                            {
                                DrawPlayerIcon(g, brush, x, y, dotSize, player.ClassName);
                            }
                        }
                    }
                    
                    // Draw player drag indicator if dragging
                    if (_isDraggingPlayer && _dragStartPos != PointF.Empty)
                    {
                        // Draw line from start to current position
                        using (var linePen = new Pen(Color.Yellow, 2))
                        {
                            linePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                            g.DrawLine(linePen, _dragStartPos, _dragCurrentPos);
                        }
                        
                        // Draw circle at start position (player's current location)
                        float startSize = 12f;
                        using (var startBrush = new SolidBrush(Color.FromArgb(100, _draggedPlayerColor)))
                        {
                            g.FillEllipse(startBrush, 
                                _dragStartPos.X - startSize / 2, 
                                _dragStartPos.Y - startSize / 2, 
                                startSize, startSize);
                        }
                        using (var startPen = new Pen(_draggedPlayerColor, 2))
                        {
                            g.DrawEllipse(startPen, 
                                _dragStartPos.X - startSize / 2, 
                                _dragStartPos.Y - startSize / 2, 
                                startSize, startSize);
                        }
                        
                        // Draw target crosshair at current position
                        float crossSize = 15f;
                        using (var crossPen = new Pen(Color.Lime, 2))
                        {
                            // Horizontal line
                            g.DrawLine(crossPen, 
                                _dragCurrentPos.X - crossSize, _dragCurrentPos.Y,
                                _dragCurrentPos.X + crossSize, _dragCurrentPos.Y);
                            // Vertical line
                            g.DrawLine(crossPen, 
                                _dragCurrentPos.X, _dragCurrentPos.Y - crossSize,
                                _dragCurrentPos.X, _dragCurrentPos.Y + crossSize);
                            // Circle
                            g.DrawEllipse(crossPen, 
                                _dragCurrentPos.X - crossSize / 2, 
                                _dragCurrentPos.Y - crossSize / 2, 
                                crossSize, crossSize);
                        }
                        
                        // Draw player name near cursor
                        using (var nameBrush = new SolidBrush(Color.White))
                        using (var nameFont = new Font("Segoe UI", 9, FontStyle.Bold))
                        {
                            g.DrawString(_draggedPlayerName, nameFont, nameBrush, 
                                _dragCurrentPos.X + 20, _dragCurrentPos.Y - 10);
                        }
                    }
                    
                    // Draw selection rectangle if in selection mode
                    var minimapFeature = GetMinimapFeature();
                    if (minimapFeature != null && minimapFeature.IsSelectionMode)
                    {
                        // Draw mode indicator text
                        using (var modeBrush = new SolidBrush(Color.Yellow))
                        using (var modeFont = new Font("Segoe UI", 12, FontStyle.Bold))
                        {
                            string modeText = minimapFeature.IsSelecting ? "Drag to select area..." : "Click and drag to select capture area";
                            g.DrawString(modeText, modeFont, modeBrush, 10, 10);
                        }
                        
                        // Draw selection rectangle if selecting
                        if (minimapFeature.IsSelecting)
                        {
                            var start = minimapFeature.SelectionStart;
                            var end = minimapFeature.SelectionEnd;
                            
                            float rectX = Math.Min(start.x, end.x);
                            float rectY = Math.Min(start.y, end.y);
                            float rectW = Math.Abs(end.x - start.x);
                            float rectH = Math.Abs(end.y - start.y);
                            
                            // Draw semi-transparent fill
                            using (var fillBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 0)))
                            {
                                g.FillRectangle(fillBrush, rectX, rectY, rectW, rectH);
                            }
                            
                            // Draw border
                            using (var borderPen = new Pen(Color.Yellow, 2))
                            {
                                g.DrawRectangle(borderPen, rectX, rectY, rectW, rectH);
                            }
                        }
                        
                        // Show if custom bounds exist for this map
                        if (minimapFeature.HasCustomBounds)
                        {
                            using (var infoBrush = new SolidBrush(Color.Lime))
                            using (var infoFont = new Font("Segoe UI", 9))
                            {
                                g.DrawString($"✓ Custom bounds saved for '{minimapFeature.CurrentMapName}'", infoFont, infoBrush, 10, 30);
                            }
                        }
                    }
                }
                
                var oldImage = _minimapPictureBox.Image;
                _minimapPictureBox.Image = bmp;
                oldImage?.Dispose();
                    }
                    catch { }
                }
                
        private void DrawPlayerIcon(Graphics g, SolidBrush brush, float x, float y, float size, string className)
        {
            string classLower = (className ?? "").ToLower();
            float centerX = x + size / 2f;
            float centerY = y + size / 2f;
            
            // Officers and leadership - Star
            if (classLower.Contains("officer") || classLower.Contains("general") || 
                classLower.Contains("captain") || classLower.Contains("flagbearer") ||
                classLower.Contains("standardbearer"))
            {
                DrawStar(g, brush, centerX, centerY, size / 2f);
            }
            // Cavalry - Diamond
            else if (classLower.Contains("dragoon") || classLower.Contains("hussar") ||
                     classLower.Contains("cavalry") || classLower.Contains("lancer") ||
                     classLower.Contains("cuirassier") || classLower.Contains("horse"))
            {
                PointF[] diamond = new PointF[]
                {
                    new PointF(centerX, y),
                    new PointF(x + size, centerY),
                    new PointF(centerX, y + size),
                    new PointF(x, centerY)
                };
                g.FillPolygon(brush, diamond);
            }
            // Skirmishers/Light Infantry - Triangle pointing up
            else if (classLower.Contains("rifleman") || classLower.Contains("rifle") ||
                     classLower.Contains("light") || classLower.Contains("jager") ||
                     classLower.Contains("jaeger") || classLower.Contains("skirmish") ||
                     classLower.Contains("voltigeur") || classLower.Contains("chasseur") ||
                     classLower.Contains("sharpshooter") || classLower.Contains("tirailleur"))
            {
                PointF[] triangle = new PointF[]
                {
                    new PointF(centerX, y),
                    new PointF(x + size, y + size),
                    new PointF(x, y + size)
                };
                g.FillPolygon(brush, triangle);
            }
            // Artillery - Square
            else if (classLower.Contains("artillery") || classLower.Contains("cannon") ||
                     classLower.Contains("gunner") || classLower.Contains("sapper") ||
                     classLower.Contains("engineer") || classLower.Contains("pioneer"))
            {
                g.FillRectangle(brush, x, y, size, size);
            }
            // Musicians - Pentagon (special)
            else if (classLower.Contains("musician") || classLower.Contains("drummer") ||
                     classLower.Contains("fifer") || classLower.Contains("piper") ||
                     classLower.Contains("bugler"))
            {
                DrawPentagon(g, brush, centerX, centerY, size / 2f);
            }
            // Guards/Grenadiers - Circle with border (larger)
            else if (classLower.Contains("guard") || classLower.Contains("grenadier") ||
                     classLower.Contains("elite"))
            {
                float guardSize = size * 1.2f;
                g.FillEllipse(brush, centerX - guardSize / 2f, centerY - guardSize / 2f, guardSize, guardSize);
            }
            // Line Infantry / default - Circle
            else
            {
                g.FillEllipse(brush, x, y, size, size);
            }
        }
        
        private void DrawPentagon(Graphics g, SolidBrush brush, float centerX, float centerY, float radius)
        {
            int points = 5;
            PointF[] pentPoints = new PointF[points];
            double angle = -Math.PI / 2; // Start at top
            
            for (int i = 0; i < points; i++)
            {
                pentPoints[i] = new PointF(
                    centerX + (float)(radius * Math.Cos(angle)),
                    centerY + (float)(radius * Math.Sin(angle))
                );
                angle += 2 * Math.PI / points;
            }
            g.FillPolygon(brush, pentPoints);
        }
        
        private void DrawStar(Graphics g, SolidBrush brush, float centerX, float centerY, float radius)
        {
            int points = 5;
            PointF[] starPoints = new PointF[points * 2];
            double angle = -Math.PI / 2;
            
            for (int i = 0; i < points * 2; i++)
            {
                float r = (i % 2 == 0) ? radius : radius * 0.4f;
                starPoints[i] = new PointF(
                    centerX + (float)(r * Math.Cos(angle)),
                    centerY + (float)(r * Math.Sin(angle))
                );
                angle += Math.PI / points;
            }
            g.FillPolygon(brush, starPoints);
        }
        
        /// <summary>
        /// Converts raw RGB24 texture data to a System.Drawing.Bitmap
        /// </summary>
        private Bitmap ConvertRawToBitmap(byte[] rawData, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            
            // Lock the bitmap data for fast access
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb
            );
            
            try
            {
                int stride = bmpData.Stride;
                IntPtr ptr = bmpData.Scan0;
                
                // Unity textures are bottom-up, Bitmap is top-down, so we need to flip
                // Also Unity is RGB, Bitmap is BGR
                byte[] bmpBytes = new byte[stride * height];
                
                for (int y = 0; y < height; y++)
                {
                    int srcRow = (height - 1 - y) * width * 3; // Flip Y
                    int dstRow = y * stride;
                    
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = srcRow + x * 3;
                        int dstIdx = dstRow + x * 3;
                        
                        // RGB to BGR
                        bmpBytes[dstIdx + 0] = rawData[srcIdx + 2]; // B
                        bmpBytes[dstIdx + 1] = rawData[srcIdx + 1]; // G
                        bmpBytes[dstIdx + 2] = rawData[srcIdx + 0]; // R
                    }
                }
                
                System.Runtime.InteropServices.Marshal.Copy(bmpBytes, 0, ptr, bmpBytes.Length);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
            
            return bmp;
        }
    }
}
