using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HoldfastModdingLauncher.Core;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    public partial class ModBrowserForm : Form
    {
        private readonly ModDownloader _modDownloader;
        private readonly ModManager _modManager;
        
        private Panel _modListPanel;
        private Panel _detailsPanel;
        private Label _titleLabel;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private ComboBox _categoryFilter;
        private TextBox _searchBox;
        private Button _refreshButton;
        
        // Details panel controls
        private Label _modNameLabel;
        private Label _modAuthorLabel;
        private Label _modVersionLabel;
        private Label _modDescriptionLabel;
        private Label _modRequirementsLabel;
        private Button _installButton;
        private Button _uninstallButton;
        private Button _updateButton;
        
        private List<RemoteModInfo> _allMods = new();
        private RemoteModInfo? _selectedMod = null;
        
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color DarkListItem = Color.FromArgb(35, 35, 42);
        private static readonly Color DarkListItemHover = Color.FromArgb(45, 45, 55);
        private static readonly Color DarkListItemSelected = Color.FromArgb(50, 60, 70);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color AccentMagenta = Color.FromArgb(200, 0, 150);
        private static readonly Color AccentGreen = Color.FromArgb(80, 200, 120);
        private static readonly Color AccentOrange = Color.FromArgb(255, 165, 0);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);
        private static readonly Color BorderColor = Color.FromArgb(60, 60, 70);

        public ModBrowserForm(ModManager modManager)
        {
            _modManager = modManager;
            _modDownloader = new ModDownloader(modManager);
            
            InitializeComponent();
            LoadModsAsync();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            int formWidth = 950;
            int formHeight = 650;

            // Form properties
            this.Text = "Mod Browser";
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.DoubleBuffered = true;

            // Load icon
            LoadIcon();

            // Title bar
            var titlePanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(0, 0),
                Size = new Size(formWidth, 50)
            };
            titlePanel.MouseDown += TitlePanel_MouseDown;
            titlePanel.MouseMove += TitlePanel_MouseMove;
            titlePanel.MouseUp += TitlePanel_MouseUp;
            this.Controls.Add(titlePanel);

            // Title label
            _titleLabel = new Label
            {
                Text = "📦  MOD BROWSER",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(20, 12),
                BackColor = Color.Transparent
            };
            _titleLabel.MouseDown += TitlePanel_MouseDown;
            _titleLabel.MouseMove += TitlePanel_MouseMove;
            _titleLabel.MouseUp += TitlePanel_MouseUp;
            titlePanel.Controls.Add(_titleLabel);

            // Refresh button
            _refreshButton = new Button
            {
                Text = "⟳",
                Font = new Font("Segoe UI", 14F),
                Size = new Size(40, 40),
                Location = new Point(formWidth - 90, 5),
                BackColor = Color.Transparent,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _refreshButton.FlatAppearance.BorderSize = 0;
            _refreshButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 60);
            _refreshButton.Click += async (s, e) => await RefreshModsAsync();
            var refreshTooltip = new ToolTip();
            refreshTooltip.SetToolTip(_refreshButton, "Refresh mod list");
            titlePanel.Controls.Add(_refreshButton);

            // Close button
            var closeButton = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(40, 40),
                Location = new Point(formWidth - 45, 5),
                BackColor = Color.Transparent,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 50, 50);
            closeButton.Click += (s, e) => this.Close();
            titlePanel.Controls.Add(closeButton);

            // Search and filter bar
            var searchPanel = new Panel
            {
                BackColor = DarkBg,
                Location = new Point(0, 50),
                Size = new Size(formWidth, 45)
            };
            this.Controls.Add(searchPanel);

            // Search box
            _searchBox = new TextBox
            {
                Location = new Point(20, 10),
                Size = new Size(250, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _searchBox.TextChanged += (s, e) => FilterMods();
            var searchPlaceholder = new Label
            {
                Text = "🔍 Search mods...",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(25, 13),
                AutoSize = true,
                BackColor = DarkPanel
            };
            searchPlaceholder.Click += (s, e) => _searchBox.Focus();
            _searchBox.Enter += (s, e) => searchPlaceholder.Visible = false;
            _searchBox.Leave += (s, e) => searchPlaceholder.Visible = string.IsNullOrEmpty(_searchBox.Text);
            searchPanel.Controls.Add(_searchBox);
            searchPanel.Controls.Add(searchPlaceholder);
            searchPlaceholder.BringToFront();

            // Category filter
            _categoryFilter = new ComboBox
            {
                Location = new Point(290, 10),
                Size = new Size(150, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _categoryFilter.Items.Add("All Categories");
            _categoryFilter.SelectedIndex = 0;
            _categoryFilter.SelectedIndexChanged += (s, e) => FilterMods();
            searchPanel.Controls.Add(_categoryFilter);

            // Status label
            _statusLabel = new Label
            {
                Text = "Loading mods...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(formWidth - 220, 13),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            searchPanel.Controls.Add(_statusLabel);

            // Main content area (split view)
            int contentY = 95;
            int contentHeight = formHeight - contentY - 10;
            int listWidth = 450;
            int detailsWidth = formWidth - listWidth - 30;

            // Mod list panel (left side)
            _modListPanel = new Panel
            {
                Location = new Point(10, contentY),
                Size = new Size(listWidth, contentHeight),
                AutoScroll = true,
                BackColor = DarkPanel,
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(_modListPanel);

            // Details panel (right side)
            _detailsPanel = new Panel
            {
                Location = new Point(listWidth + 20, contentY),
                Size = new Size(detailsWidth, contentHeight),
                BackColor = DarkPanel,
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(_detailsPanel);

            InitializeDetailsPanel();

            // Progress bar (for downloads)
            _progressBar = new ProgressBar
            {
                Location = new Point(10, formHeight - 35),
                Size = new Size(formWidth - 20, 20),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            this.Controls.Add(_progressBar);

            // Border
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(BorderColor, 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            this.ResumeLayout(false);
        }

        private void LoadIcon()
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Xark.ico");
            if (File.Exists(iconPath))
            {
                try { this.Icon = new Icon(iconPath); }
                catch { this.Icon = SystemIcons.Application; }
            }
        }

        private void InitializeDetailsPanel()
        {
            int padding = 20;
            int y = padding;

            // Mod name
            _modNameLabel = new Label
            {
                Text = "Select a mod",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = TextGray,
                Location = new Point(padding, y),
                Size = new Size(_detailsPanel.Width - padding * 2, 35),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_modNameLabel);
            y += 40;

            // Author
            _modAuthorLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(padding, y),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_modAuthorLabel);
            y += 28;

            // Version info
            _modVersionLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10F),
                ForeColor = AccentCyan,
                Location = new Point(padding, y),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_modVersionLabel);
            y += 35;

            // Description
            _modDescriptionLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(padding, y),
                Size = new Size(_detailsPanel.Width - padding * 2, 150),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_modDescriptionLabel);
            y += 160;

            // Requirements
            _modRequirementsLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccentOrange,
                Location = new Point(padding, y),
                Size = new Size(_detailsPanel.Width - padding * 2, 60),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_modRequirementsLabel);

            // Buttons panel at bottom
            int buttonY = _detailsPanel.Height - 60;
            int buttonWidth = 130;
            int buttonHeight = 40;

            // Install button
            _installButton = new Button
            {
                Text = "⬇  Install",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(padding, buttonY),
                Size = new Size(buttonWidth, buttonHeight),
                BackColor = DarkListItem,
                ForeColor = AccentGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Visible = false
            };
            _installButton.FlatAppearance.BorderColor = AccentGreen;
            _installButton.FlatAppearance.BorderSize = 1;
            _installButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 80, 60);
            _installButton.Click += async (s, e) => await InstallSelectedModAsync();
            _detailsPanel.Controls.Add(_installButton);

            // Update button
            _updateButton = new Button
            {
                Text = "⟳  Update",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(padding, buttonY),
                Size = new Size(buttonWidth, buttonHeight),
                BackColor = DarkListItem,
                ForeColor = AccentOrange,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Visible = false
            };
            _updateButton.FlatAppearance.BorderColor = AccentOrange;
            _updateButton.FlatAppearance.BorderSize = 1;
            _updateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 60, 30);
            _updateButton.Click += async (s, e) => await InstallSelectedModAsync();
            _detailsPanel.Controls.Add(_updateButton);

            // Uninstall button
            _uninstallButton = new Button
            {
                Text = "🗑  Uninstall",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(padding + buttonWidth + 15, buttonY),
                Size = new Size(buttonWidth, buttonHeight),
                BackColor = DarkListItem,
                ForeColor = Color.FromArgb(255, 100, 100),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Visible = false
            };
            _uninstallButton.FlatAppearance.BorderColor = Color.FromArgb(255, 100, 100);
            _uninstallButton.FlatAppearance.BorderSize = 1;
            _uninstallButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 40, 40);
            _uninstallButton.Click += UninstallSelectedMod;
            _detailsPanel.Controls.Add(_uninstallButton);
        }

        private async void LoadModsAsync()
        {
            await RefreshModsAsync();
        }

        private async Task RefreshModsAsync()
        {
            _statusLabel.Text = "Fetching mods...";
            _statusLabel.ForeColor = TextGray;
            _refreshButton.Enabled = false;

            try
            {
                var registry = await _modDownloader.FetchRegistryAsync(true);
                
                if (registry != null)
                {
                    bool isMaster = Services.ModDownloader.IsMasterLoggedIn();
                    _allMods = registry.Mods.Where(m => m.IsEnabled && 
                        (!m.RequiresMasterLogin || isMaster)).ToList();
                    
                    // Update categories
                    _categoryFilter.Items.Clear();
                    _categoryFilter.Items.Add("All Categories");
                    foreach (var category in registry.Categories)
                    {
                        _categoryFilter.Items.Add(category);
                    }
                    _categoryFilter.SelectedIndex = 0;
                    
                    DisplayMods(_allMods);
                    
                    int updateCount = _allMods.Count(m => m.HasUpdate);
                    if (updateCount > 0)
                    {
                        _statusLabel.Text = $"Found {_allMods.Count} mod(s), {updateCount} update(s) available";
                        _statusLabel.ForeColor = AccentOrange;
                    }
                    else
                    {
                        _statusLabel.Text = $"Found {_allMods.Count} mod(s)";
                        _statusLabel.ForeColor = AccentGreen;
                    }
                }
                else
                {
                    _statusLabel.Text = "Failed to load mods. Check your internet connection.";
                    _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error: {ex.Message}";
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                Logger.LogError($"Failed to load mods: {ex.Message}");
            }
            finally
            {
                _refreshButton.Enabled = true;
            }
        }

        private void FilterMods()
        {
            var filtered = _allMods.AsEnumerable();
            
            // Filter by search text
            string searchText = _searchBox.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(m => 
                    m.Name.ToLower().Contains(searchText) ||
                    m.Description.ToLower().Contains(searchText) ||
                    m.Author.ToLower().Contains(searchText) ||
                    m.Tags.Any(t => t.ToLower().Contains(searchText)));
            }
            
            // Filter by category
            if (_categoryFilter.SelectedIndex > 0)
            {
                string category = _categoryFilter.SelectedItem?.ToString() ?? "";
                filtered = filtered.Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }
            
            DisplayMods(filtered.ToList());
        }

        private void DisplayMods(List<RemoteModInfo> mods)
        {
            _modListPanel.SuspendLayout();
            _modListPanel.Controls.Clear();

            int y = 5;
            int itemHeight = 80;
            int itemWidth = _modListPanel.Width - 25;

            foreach (var mod in mods)
            {
                var modPanel = CreateModListItem(mod, itemWidth, itemHeight);
                modPanel.Location = new Point(5, y);
                _modListPanel.Controls.Add(modPanel);
                y += itemHeight + 5;
            }

            if (mods.Count == 0)
            {
                var noModsLabel = new Label
                {
                    Text = "No mods found.",
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = TextGray,
                    Location = new Point(20, 20),
                    AutoSize = true
                };
                _modListPanel.Controls.Add(noModsLabel);
            }

            _modListPanel.ResumeLayout();
        }

        private Panel CreateModListItem(RemoteModInfo mod, int width, int height)
        {
            var panel = new Panel
            {
                Size = new Size(width, height),
                BackColor = DarkListItem,
                Cursor = Cursors.Hand,
                Tag = mod
            };

            // Hover effects
            panel.MouseEnter += (s, e) => { if (_selectedMod != mod) panel.BackColor = DarkListItemHover; };
            panel.MouseLeave += (s, e) => { if (_selectedMod != mod) panel.BackColor = DarkListItem; };
            panel.Click += (s, e) => SelectMod(mod, panel);

            // Mod name
            var nameLabel = new Label
            {
                Text = mod.Name,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextLight,
                Location = new Point(12, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            nameLabel.Click += (s, e) => SelectMod(mod, panel);
            panel.Controls.Add(nameLabel);

            // Status indicator
            string statusText;
            Color statusColor;
            if (mod.HasUpdate)
            {
                statusText = $"⚠ v{mod.InstalledVersion} → v{mod.Version}";
                statusColor = AccentOrange;
            }
            else if (mod.IsInstalled)
            {
                statusText = $"✓ v{mod.InstalledVersion}";
                statusColor = AccentGreen;
            }
            else
            {
                statusText = string.IsNullOrEmpty(mod.Version) ? "Available" : $"v{mod.Version}";
                statusColor = TextGray;
            }

            var statusLabel = new Label
            {
                Text = statusText,
                Font = new Font("Segoe UI", 9F),
                ForeColor = statusColor,
                Location = new Point(width - 130, 10),
                Size = new Size(120, 20),
                TextAlign = ContentAlignment.TopRight,
                BackColor = Color.Transparent
            };
            statusLabel.Click += (s, e) => SelectMod(mod, panel);
            panel.Controls.Add(statusLabel);

            // Author
            var authorLabel = new Label
            {
                Text = $"by {mod.Author}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(12, 30),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            authorLabel.Click += (s, e) => SelectMod(mod, panel);
            panel.Controls.Add(authorLabel);

            // Category
            var categoryLabel = new Label
            {
                Text = mod.Category,
                Font = new Font("Segoe UI", 8F),
                ForeColor = AccentMagenta,
                Location = new Point(12, 50),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            categoryLabel.Click += (s, e) => SelectMod(mod, panel);
            panel.Controls.Add(categoryLabel);

            // Short description
            string shortDesc = mod.Description.Length > 60 
                ? mod.Description.Substring(0, 60) + "..." 
                : mod.Description;
            var descLabel = new Label
            {
                Text = shortDesc,
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextGray,
                Location = new Point(100, 50),
                Size = new Size(width - 115, 20),
                BackColor = Color.Transparent
            };
            descLabel.Click += (s, e) => SelectMod(mod, panel);
            panel.Controls.Add(descLabel);

            return panel;
        }

        private void SelectMod(RemoteModInfo mod, Panel panel)
        {
            // Reset previous selection
            foreach (Control ctrl in _modListPanel.Controls)
            {
                if (ctrl is Panel p)
                    p.BackColor = DarkListItem;
            }

            // Highlight selected
            panel.BackColor = DarkListItemSelected;
            _selectedMod = mod;

            // Update details panel
            UpdateDetailsPanel(mod);
        }

        private void UpdateDetailsPanel(RemoteModInfo mod)
        {
            _modNameLabel.Text = mod.Name;
            _modNameLabel.ForeColor = AccentCyan;
            
            _modAuthorLabel.Text = $"by {mod.Author}  •  {mod.Category}";
            
            if (mod.IsInstalled)
            {
                if (mod.HasUpdate)
                {
                    _modVersionLabel.Text = $"Installed: v{mod.InstalledVersion}  →  Latest: v{mod.Version}";
                    _modVersionLabel.ForeColor = AccentOrange;
                }
                else
                {
                    _modVersionLabel.Text = $"Installed: v{mod.InstalledVersion} (Latest)";
                    _modVersionLabel.ForeColor = AccentGreen;
                }
            }
            else
            {
                _modVersionLabel.Text = string.IsNullOrEmpty(mod.Version) 
                    ? "Version: Resolving..." 
                    : $"Version: v{mod.Version}";
                _modVersionLabel.ForeColor = AccentCyan;
            }

            _modDescriptionLabel.Text = mod.Description;

            if (!string.IsNullOrEmpty(mod.Requirements))
            {
                _modRequirementsLabel.Text = $"⚠ {mod.Requirements}";
                _modRequirementsLabel.Visible = true;
            }
            else
            {
                _modRequirementsLabel.Visible = false;
            }

            // Show/hide buttons based on installed state
            if (mod.IsInstalled)
            {
                _installButton.Visible = false;
                _updateButton.Visible = mod.HasUpdate;
                _uninstallButton.Visible = true;
            }
            else
            {
                _installButton.Visible = true;
                _updateButton.Visible = false;
                _uninstallButton.Visible = false;
            }
        }

        private async Task InstallSelectedModAsync()
        {
            if (_selectedMod == null) return;

            _installButton.Enabled = false;
            _updateButton.Enabled = false;
            _uninstallButton.Enabled = false;
            _progressBar.Value = 0;
            _progressBar.Visible = true;
            _statusLabel.Text = $"Installing {_selectedMod.Name}...";
            _statusLabel.ForeColor = AccentCyan;

            try
            {
                var result = await _modDownloader.DownloadAndInstallModAsync(
                    _selectedMod,
                    detailedProgress =>
                    {
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() => 
                            {
                                _progressBar.Value = detailedProgress.PercentComplete;
                                if (detailedProgress.TotalBytes > 0)
                                {
                                    _statusLabel.Text = $"Downloading: {detailedProgress.FormattedProgress}";
                                }
                                else
                                {
                                    _statusLabel.Text = $"Installing... {detailedProgress.Status}";
                                }
                            }));
                        }
                        else
                        {
                            _progressBar.Value = detailedProgress.PercentComplete;
                        }
                    },
                    progress =>
                    {
                        if (this.InvokeRequired)
                            this.Invoke(new Action(() => _progressBar.Value = progress));
                        else
                            _progressBar.Value = progress;
                    });

                if (result.Success)
                {
                    _statusLabel.Text = result.Message;
                    _statusLabel.ForeColor = AccentGreen;
                    
                    // Refresh the mod list to update installed status
                    await RefreshModsAsync();
                }
                else
                {
                    _statusLabel.Text = result.Message;
                    _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Installation failed: {ex.Message}";
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
            }
            finally
            {
                _progressBar.Visible = false;
                _installButton.Enabled = true;
                _updateButton.Enabled = true;
                _uninstallButton.Enabled = true;
            }
        }

        private void UninstallSelectedMod(object sender, EventArgs e)
        {
            if (_selectedMod == null) return;

            var confirmResult = MessageBox.Show(
                $"Are you sure you want to uninstall {_selectedMod.Name}?",
                "Confirm Uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmResult != DialogResult.Yes) return;

            var result = _modDownloader.UninstallMod(_selectedMod);
            
            if (result.Success)
            {
                _statusLabel.Text = result.Message;
                _statusLabel.ForeColor = AccentGreen;
                
                // Refresh the mod list
                _ = RefreshModsAsync();
            }
            else
            {
                _statusLabel.Text = result.Message;
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
            }
        }

        #region Window Dragging

        private bool _isDragging = false;
        private Point _dragOffset;

        private void TitlePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragOffset = e.Location;
            }
        }

        private void TitlePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentScreenPos = PointToScreen(e.Location);
                this.Location = new Point(
                    currentScreenPos.X - _dragOffset.X,
                    currentScreenPos.Y - _dragOffset.Y
                );
            }
        }

        private void TitlePanel_MouseUp(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _modDownloader?.Dispose();
            base.OnFormClosing(e);
        }
    }
}

