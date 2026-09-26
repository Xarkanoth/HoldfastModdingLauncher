using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using HoldfastModdingLauncher.Core;
using HoldfastModdingLauncher.Services;
using System.Threading.Tasks;

namespace HoldfastModdingLauncher
{
    public partial class MainForm : Form
    {
        private readonly HoldfastManager _holdfastManager;
        private readonly ModManager _modManager;
        private readonly ModVersionChecker _versionChecker;
        private readonly UpdateChecker _updateChecker;
        private readonly ModDownloader _modDownloader;
        private readonly Injector _injector;
        private readonly PreferencesManager _preferencesManager;
        private readonly ApiClient _apiClient;
        
        // Store remote mod info for updates
        private readonly Dictionary<string, RemoteModInfo> _remoteModInfo = new();
        private readonly System.Windows.Forms.Timer _liveUpdateTimer = new();
        private bool _liveUpdateBusy;
        private string _promptedLauncherVersion;
        
        private Button _playButton;
        private Button _settingsButton;
        private Button _browseModsButton;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private CheckBox _debugModeCheckBox;
        private Panel _modsPanel;
        private ThemeScrollPanel _modsScroll;
        private Label _modsLabel;
        private readonly List<ThemeToggle> _modToggles = new();
        private Panel _sidebarPanel;
        private ThemePanel _accountCard;
        
        // Selected mod details
        private Panel _detailsPanel;
        private Label _detailsTitleLabel;
        private Label _detailsDescLabel;
        private Label _detailsReqLabel;
        private Button _modSettingsButton;
        private string _selectedModFileName = null;
        private readonly Dictionary<string, ModManifest> _modManifests = new();
        
        // Login
        private TextBox _loginUsernameBox;
        private TextBox _loginPasswordBox;
        private Button _loginButton;
        private Button _adminPanelButton;
        private Label _loginStatusLabel;
        private bool _isMasterLoggedIn = false;
        private string _loggedInClientName = null;
        
        private const string HASH_SALT = "HF_MODDING_2024_XARK";
        private const string LOGIN_TOKEN_FILE = "master_login.token";
        
        // LauncherCoreMod integrity protection
        private const string LAUNCHER_CORE_MOD_NAME = "LauncherCoreMod.dll";
        private bool _coreModMissing = false;
        private Panel _coreModLockPanel;

        private readonly Color DarkBg = Theme.PageBg;
        private readonly Color DarkPanel = Theme.Panel;
        private readonly Color AccentCyan = Theme.Brand;
        private readonly Color TextLight = Theme.Text;
        private readonly Color TextGray = Theme.TextMuted;
        private readonly Color SuccessGreen = Theme.Success;

        public MainForm(bool debugMode = false)
        {
            _holdfastManager = new HoldfastManager();
            _modManager = new ModManager();
            _versionChecker = new ModVersionChecker();
            _apiClient = new ApiClient();
            _updateChecker = new UpdateChecker();
            _updateChecker.SetApiClient(_apiClient);
            _modDownloader = new ModDownloader(_modManager, _apiClient);
            _injector = new Injector();
            _preferencesManager = new PreferencesManager();
            
            InitializeComponent();
            InitializeUI();

            if (debugMode && _debugModeCheckBox != null)
                _debugModeCheckBox.Checked = true;
            
            // Check LauncherCoreMod - if missing, launcher will be locked after login
            _coreModMissing = !VerifyLauncherCoreMod();
            
            // Check first-run disclaimer
            CheckFirstRunDisclaimer();
            
            // Require login before showing dashboard
            if (_disclaimerAccepted)
                ShowLoginGate();
            
            // Perform initial setup check
            CheckSetup();
            
            // Check for updates on startup (if enabled)
            _ = CheckForUpdatesAsync();
            _liveUpdateTimer.Interval = 60_000;
            _liveUpdateTimer.Tick += LiveUpdateTimer_Tick;
            _liveUpdateTimer.Start();
            FormClosed += (_, _) => _liveUpdateTimer.Stop();
        }

        private async void LiveUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_liveUpdateBusy || IsDisposed) return;
            _liveUpdateBusy = true;
            try
            {
                var mods = _modManager.DiscoverMods();
                if (mods != null && mods.Count > 0 && _modsPanel != null && _modsPanel.Controls.Count > 0)
                    await CheckForModUpdatesAsync(mods);
                await CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Live update check failed: {ex.Message}");
            }
            finally
            {
                _liveUpdateBusy = false;
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // Form properties - Dark theme, bigger size
            this.Text = "Holdfast Modding";
            this.Size = new Size(1040, 720);
            this.MinimumSize = new Size(900, 600);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            
            // Load custom icon
            LoadIcon();
            
            // Subscribe to form closing to ensure vanilla by default
            this.FormClosing += MainForm_FormClosing;
            
            this.ResumeLayout(false);
        }
        
        /// <summary>
        /// When the launcher closes, disable BepInEx doorstop so that
        /// launching Holdfast.exe directly runs vanilla (no mods).
        /// </summary>
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                string? holdfastPath = _holdfastManager.FindHoldfastInstallation();
                if (!string.IsNullOrEmpty(holdfastPath))
                {
                    _injector.EnsureVanillaByDefault(holdfastPath);
                    Logger.LogInfo("Disabled BepInEx doorstop - Holdfast.exe will run vanilla");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not disable doorstop on close: {ex.Message}");
            }
        }

        private void LoadIcon()
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Xark.ico");
            if (File.Exists(iconPath))
            {
                try
                {
                    this.Icon = new Icon(iconPath);
                }
                catch
                {
                    this.Icon = SystemIcons.Application;
                }
            }
            else
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var iconStream = assembly.GetManifestResourceStream("HoldfastModdingLauncher.Resources.Xark.ico");
                    if (iconStream != null)
                    {
                        this.Icon = new Icon(iconStream);
                    }
                    else
                    {
                        this.Icon = SystemIcons.Application;
                    }
                }
                catch
                {
                    this.Icon = SystemIcons.Application;
                }
            }
        }

        private void InitializeUI()
        {
            const int pad = 16;

            var titlePanel = new Panel
            {
                BackColor = Theme.HeaderBg,
                Dock = DockStyle.Fill,
                Height = 56,
                BorderStyle = BorderStyle.None
            };
            titlePanel.Resize += (s, e) => titlePanel.Invalidate();
            titlePanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Theme.Border);
                e.Graphics.DrawLine(pen, 0, titlePanel.Height - 1, titlePanel.Width, titlePanel.Height - 1);
            };
            var titleLabel = new Label
            {
                Text = "Holdfast Modding",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(pad, 14),
                BackColor = Color.Transparent
            };
            titlePanel.Controls.Add(titleLabel);

            _settingsButton = new Button
            {
                Text = "Settings",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(100, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(titlePanel.Width - 116, 10)
            };
            Theme.ApplyGhostButton(_settingsButton);
            _settingsButton.Click += SettingsButton_Click;
            titlePanel.Controls.Add(_settingsButton);
            titlePanel.Resize += (s, e) =>
            {
                _settingsButton.Location = new Point(titlePanel.Width - 116, 10);
            };

            _sidebarPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Width = 300,
                BackColor = Theme.HeaderBg,
                BorderStyle = BorderStyle.None
            };
            _sidebarPanel.Resize += (s, e) => _sidebarPanel.Invalidate();
            _sidebarPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Theme.Border);
                e.Graphics.DrawLine(pen, _sidebarPanel.Width - 1, 0, _sidebarPanel.Width - 1, _sidebarPanel.Height);
            };
            var sidebarHeader = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 96,
                BackColor = Theme.HeaderBg
            };

            _modsLabel = new Label
            {
                Text = "Mods",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(12, 10),
                BackColor = Color.Transparent
            };
            sidebarHeader.Controls.Add(_modsLabel);

            _browseModsButton = new Button
            {
                Text = "Browse mods",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(132, 36),
                Location = new Point(12, 48)
            };
            Theme.ApplyPrimaryButton(_browseModsButton);
            _browseModsButton.Click += BrowseModsButton_Click;
            sidebarHeader.Controls.Add(_browseModsButton);

            var openModsFolderButton = new Button
            {
                Text = "Open folder",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(132, 36),
                Location = new Point(152, 48)
            };
            Theme.ApplyGhostButton(openModsFolderButton);
            openModsFolderButton.Click += (s, e) => OpenModsFolder();
            sidebarHeader.Controls.Add(openModsFolderButton);

            _modsScroll = new ThemeScrollPanel
            {
                Dock = DockStyle.Fill
            };
            _modsPanel = _modsScroll.Content;
            _modsPanel.Resize += (s, e) => StretchModRows();

            var sidebarGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            sidebarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            sidebarGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            sidebarGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            sidebarGrid.Controls.Add(sidebarHeader, 0, 0);
            sidebarGrid.Controls.Add(_modsScroll, 0, 1);
            _sidebarPanel.Controls.Add(sidebarGrid);

            var contentPanel = new Panel
            {
                BackColor = DarkBg,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None
            };
            contentPanel.Resize += (s, e) => contentPanel.Invalidate();

            var statusCard = new ThemePanel
            {
                Dock = DockStyle.Fill
            };

            var statusSectionLabel = new Label
            {
                Text = "Status",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };
            statusCard.Controls.Add(statusSectionLabel);

            _statusLabel = new Label
            {
                Text = "Checking installation...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = false,
                Location = new Point(12, 28),
                Size = new Size(400, 20),
                BackColor = Color.Transparent
            };
            statusCard.Controls.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                Location = new Point(12, 50),
                Size = new Size(400, 8),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };
            statusCard.Controls.Add(_progressBar);
            statusCard.Resize += (s, e) =>
            {
                int inner = Math.Max(40, statusCard.ClientSize.Width - 24);
                _progressBar.Width = inner;
                _statusLabel.Width = inner;
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DarkBg
            };

            var detailsCard = new ThemePanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var detailsSectionLabel = new Label
            {
                Text = "Mod details",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };
            detailsCard.Controls.Add(detailsSectionLabel);

            _detailsPanel = detailsCard;

            _detailsTitleLabel = new Label
            {
                Text = "Select a mod to view details",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextGray,
                AutoSize = false,
                Location = new Point(12, 32),
                Size = new Size(400, 28),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsTitleLabel);

            _detailsDescLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextLight,
                AutoSize = false,
                Location = new Point(12, 64),
                Size = new Size(400, 80),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsDescLabel);

            _detailsReqLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Theme.Warning,
                AutoSize = false,
                Location = new Point(12, 152),
                Size = new Size(400, 40),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsReqLabel);

            _modSettingsButton = new Button
            {
                Text = "Settings",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(110, 36),
                Visible = false
            };
            Theme.ApplyGhostButton(_modSettingsButton);
            _modSettingsButton.Click += ModSettingsButton_Click;
            _detailsPanel.Controls.Add(_modSettingsButton);
            _modSettingsButton.BringToFront();
            detailsCard.Resize += (s, e) => LayoutDetailsPanel();

            _accountCard = new ThemePanel
            {
                Dock = DockStyle.Fill
            };

            var contentGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = Padding.Empty,
                Padding = new Padding(pad),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            contentGrid.Controls.Add(statusCard, 0, 0);
            contentGrid.Controls.Add(detailsCard, 0, 1);
            contentGrid.Controls.Add(_accountCard, 0, 2);
            contentGrid.Controls.Add(footerPanel, 0, 3);
            contentPanel.Controls.Add(contentGrid);

            var bodyGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bodyGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            bodyGrid.Controls.Add(_sidebarPanel, 0, 0);
            bodyGrid.Controls.Add(contentPanel, 1, 0);

            var rootGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rootGrid.Controls.Add(titlePanel, 0, 0);
            rootGrid.Controls.Add(bodyGrid, 0, 1);
            this.Controls.Add(rootGrid);

            var loginSectionLabel = new Label
            {
                Text = "Account",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };
            _accountCard.Controls.Add(loginSectionLabel);

            _loginUsernameBox = new TextBox
            {
                Size = new Size(140, 28),
                Font = new Font("Segoe UI", 9F),
                Text = ""
            };
            Theme.ApplyInput(_loginUsernameBox);
            _loginUsernameBox.GotFocus += (s, e) => { if (_loginUsernameBox.ForeColor == TextGray) { _loginUsernameBox.Text = ""; _loginUsernameBox.ForeColor = TextLight; } };
            _loginUsernameBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(_loginUsernameBox.Text)) { _loginUsernameBox.ForeColor = TextGray; _loginUsernameBox.Text = "Username"; } };
            _loginUsernameBox.ForeColor = TextGray;
            _loginUsernameBox.Text = "Username";
            _accountCard.Controls.Add(_loginUsernameBox);

            _loginPasswordBox = new TextBox
            {
                Size = new Size(140, 28),
                Font = new Font("Segoe UI", 9F),
                UseSystemPasswordChar = true
            };
            Theme.ApplyInput(_loginPasswordBox);
            _accountCard.Controls.Add(_loginPasswordBox);
            _loginPasswordBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) PerformMasterLogin(); };

            _loginButton = new Button
            {
                Text = "Log in",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(100, 36)
            };
            Theme.ApplyPrimaryButton(_loginButton);
            _loginButton.Click += LoginButton_Click;
            _accountCard.Controls.Add(_loginButton);

            _adminPanelButton = new Button
            {
                Text = "Admin",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(88, 36),
                Visible = false
            };
            Theme.ApplyGhostButton(_adminPanelButton);
            _adminPanelButton.Click += AdminPanelButton_Click;
            _accountCard.Controls.Add(_adminPanelButton);

            _loginStatusLabel = new Label
            {
                Text = "Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = false,
                Size = new Size(200, 24),
                BackColor = Color.Transparent
            };
            _accountCard.Controls.Add(_loginStatusLabel);

            _debugModeCheckBox = new CheckBox
            {
                Text = "Show debug console",
                AutoSize = true,
                ForeColor = TextGray,
                BackColor = Color.Transparent,
                Checked = false,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _accountCard.Controls.Add(_debugModeCheckBox);
            _accountCard.Resize += (s, e) => LayoutAccountBar();
            LayoutAccountBar();

            CheckExistingLogin();

            var disclaimerLabel = new Label
            {
                Text = "Unofficial tool. PC only. Not affiliated with Anvil Game Studios.",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Theme.Warning,
                AutoSize = false,
                Location = new Point(0, 4),
                Size = new Size(300, 32),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            disclaimerLabel.Click += (s, e) => ShowDisclaimer();
            var disclaimerTooltip = new ToolTip();
            disclaimerTooltip.SetToolTip(disclaimerLabel, "Click for full disclaimer");
            footerPanel.Controls.Add(disclaimerLabel);

            var creditLabel = new Label
            {
                Text = "Built by Xarkanoth  ·  Discord.gg/csg",
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextGray,
                AutoSize = false,
                Location = new Point(0, 36),
                Size = new Size(300, 18),
                BackColor = Color.Transparent
            };
            footerPanel.Controls.Add(creditLabel);

            var donateButton = new Button
            {
                Text = "Support",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Size = new Size(88, 32),
                Location = new Point(0, 58)
            };
            Theme.ApplyGhostButton(donateButton);
            donateButton.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://buymeacoffee.com/xarkanoth",
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            var donateTooltip = new ToolTip();
            donateTooltip.SetToolTip(donateButton, "Support development");
            footerPanel.Controls.Add(donateButton);

            _playButton = new Button
            {
                Text = "Launch Holdfast",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(200, 44),
                Enabled = false
            };
            Theme.ApplyPrimaryButton(_playButton);
            _playButton.Click += PlayButton_Click;
            footerPanel.Controls.Add(_playButton);

            var versionLabel = new Label
            {
                Text = GetVersionString(),
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = false,
                Size = new Size(80, 20),
                TextAlign = ContentAlignment.TopRight,
                BackColor = Color.Transparent
            };
            footerPanel.Controls.Add(versionLabel);

            void LayoutFooter()
            {
                int rightCol = 212;
                int leftW = Math.Max(80, footerPanel.ClientSize.Width - rightCol - 8);
                disclaimerLabel.SetBounds(0, 4, leftW, 32);
                creditLabel.SetBounds(0, 36, leftW, 18);
                donateButton.Location = new Point(0, 60);
                _playButton.Location = new Point(footerPanel.ClientSize.Width - 200, 28);
                versionLabel.SetBounds(footerPanel.ClientSize.Width - 80, 4, 80, 20);
            }

            footerPanel.Resize += (s, e) => LayoutFooter();
            LayoutFooter();
            LayoutDetailsPanel();

            LoadMods();
        }

        private static string GetVersionString()
        {
            try
            {
                // Try multiple locations for ModVersions.json
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possiblePaths = new[]
                {
                    Path.Combine(baseDir, "ModVersions.json"),
                    Path.Combine(baseDir, "..", "ModVersions.json"),
                    Path.Combine(baseDir, "..", "..", "ModVersions.json")
                };
                
                foreach (string versionsFile in possiblePaths)
                {
                    if (File.Exists(versionsFile))
                    {
                        string json = File.ReadAllText(versionsFile);
                        var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("Launcher", out var launcher) &&
                            launcher.TryGetProperty("Version", out var version))
                        {
                            return $"v{version.GetString()}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error reading version from JSON: {ex.Message}");
            }
            
            // Fallback: read from assembly version
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                if (version != null)
                {
                    return $"v{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch { }
            
            return "v1.0.0";
        }
        
        private static string GetTokenFilePath()
        {
            // Store token file in AppData for cross-location access
            string appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoldfastModding");
            
            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }
            
            return Path.Combine(appDataFolder, LOGIN_TOKEN_FILE);
        }
        
        private void WriteTokenToAllLocations(string content, string clientName = null)
        {
            // Write to AppData: {hash}|{clientName}|{role}
            string primaryPath = GetTokenFilePath();
            string role = _isMasterLoggedIn ? "MASTER" : "MEMBER";
            string primaryContent = !string.IsNullOrEmpty(clientName) ? $"{content}|{clientName}|{role}" : content;
            File.WriteAllText(primaryPath, primaryContent);
            
            // Write master token to BepInEx locations ONLY for master users.
            // Non-master users get any existing token files deleted so the core mod
            // correctly identifies them as non-master.
            try
            {
                string gamePath = _holdfastManager.FindHoldfastInstallation();
                if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                {
                    string[] tokenLocations = new[]
                    {
                        Path.Combine(gamePath, "BepInEx", "plugins", LOGIN_TOKEN_FILE),
                        Path.Combine(gamePath, "BepInEx", "plugins", "Mods", LOGIN_TOKEN_FILE),
                        Path.Combine(gamePath, "BepInEx", LOGIN_TOKEN_FILE)
                    };
                    
                    if (_isMasterLoggedIn)
                    {
                        const string MOD_TOKEN = "MASTER_ACCESS_GRANTED";
                        foreach (string path in tokenLocations)
                        {
                            string dir = Path.GetDirectoryName(path);
                            if (Directory.Exists(dir))
                                File.WriteAllText(path, MOD_TOKEN);
                        }
                    }
                    else
                    {
                        foreach (string path in tokenLocations)
                        {
                            if (File.Exists(path))
                                File.Delete(path);
                        }
                    }
                }
            }
            catch { }
        }
        
        private void DeleteTokenFromAllLocations()
        {
            // Delete from AppData
            string primaryPath = GetTokenFilePath();
            if (File.Exists(primaryPath))
            {
                File.Delete(primaryPath);
            }
            
            // Also delete from game's BepInEx folder
            try
            {
                string gamePath = _holdfastManager.FindHoldfastInstallation();
                if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                {
                    string[] paths = new string[]
                    {
                        Path.Combine(gamePath, "BepInEx", LOGIN_TOKEN_FILE),
                        Path.Combine(gamePath, "BepInEx", "plugins", LOGIN_TOKEN_FILE),
                        Path.Combine(gamePath, "BepInEx", "plugins", "Mods", LOGIN_TOKEN_FILE)
                    };
                    
                    foreach (string path in paths)
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                }
            }
            catch { }
        }
        
        private Panel _loginGatePanel;

        private void ShowLoginGate()
        {
            // Try restoring session in the background; if it succeeds we skip the gate
            _ = TryAutoLoginAndGateAsync();
        }

        private async Task TryAutoLoginAndGateAsync()
        {
            bool sessionRestored = false;

            if (_apiClient.IsConfigured)
            {
                try
                {
                    sessionRestored = await _apiClient.TryRestoreSessionAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"API session restore failed: {ex.Message}");
                }
            }

            if (sessionRestored)
            {
                string displayName = _apiClient.CurrentUser?.DisplayName ?? _apiClient.CurrentUser?.Username ?? "User";
                _isMasterLoggedIn = true;
                _loggedInClientName = displayName;
                UpdateLoginStatus(true);
                return;
            }

            // No stored session -- show the login gate overlay
            _loginGatePanel = new Panel
            {
                Name = "loginGatePanel",
                Dock = DockStyle.Fill,
                BackColor = Theme.PageBg
            };

            var brandBar = new Panel
            {
                Size = new Size(48, 4),
                BackColor = Theme.Brand
            };
            _loginGatePanel.Controls.Add(brandBar);

            var gateTitle = new Label
            {
                Text = "Holdfast Modding",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _loginGatePanel.Controls.Add(gateTitle);

            var gateSubtitle = new Label
            {
                Text = "Log in or create an account to continue",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _loginGatePanel.Controls.Add(gateSubtitle);

            var usernameLabel = new Label
            {
                Text = "Username",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _loginGatePanel.Controls.Add(usernameLabel);

            var gateUsernameBox = new TextBox
            {
                Size = new Size(280, 30),
                Font = new Font("Segoe UI", 11F)
            };
            Theme.ApplyInput(gateUsernameBox);
            _loginGatePanel.Controls.Add(gateUsernameBox);

            var passwordLabel = new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _loginGatePanel.Controls.Add(passwordLabel);

            var gatePasswordBox = new TextBox
            {
                Size = new Size(280, 30),
                Font = new Font("Segoe UI", 11F),
                UseSystemPasswordChar = true
            };
            Theme.ApplyInput(gatePasswordBox);
            _loginGatePanel.Controls.Add(gatePasswordBox);

            var gateStatusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Red,
                Size = new Size(280, 20),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _loginGatePanel.Controls.Add(gateStatusLabel);

            var loginBtn = new Button
            {
                Text = "Log in",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Size = new Size(280, 44)
            };
            Theme.ApplyPrimaryButton(loginBtn);
            _loginGatePanel.Controls.Add(loginBtn);

            var registerBtn = new Button
            {
                Text = "Create account",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(280, 40)
            };
            Theme.ApplyGhostButton(registerBtn);
            _loginGatePanel.Controls.Add(registerBtn);

            var keepLoggedInCheck = new CheckBox
            {
                Text = "  Keep me logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                Checked = true,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _loginGatePanel.Controls.Add(keepLoggedInCheck);

            // Layout positioning
            void LayoutGate()
            {
                int cx = _loginGatePanel.Width / 2;
                int cy = _loginGatePanel.Height / 2;
                int w = 280;

                brandBar.Location = new Point(cx - brandBar.Width / 2, cy - 190);
                gateTitle.Location = new Point(cx - gateTitle.Width / 2, cy - 170);
                gateSubtitle.Location = new Point(cx - gateSubtitle.Width / 2, cy - 140);

                usernameLabel.Location = new Point(cx - w / 2, cy - 105);
                gateUsernameBox.Location = new Point(cx - w / 2, cy - 85);
                passwordLabel.Location = new Point(cx - w / 2, cy - 52);
                gatePasswordBox.Location = new Point(cx - w / 2, cy - 32);
                gateStatusLabel.Location = new Point(cx - w / 2, cy + 5);
                loginBtn.Location = new Point(cx - w / 2, cy + 30);
                keepLoggedInCheck.Location = new Point(cx - w / 2, cy + 78);
                registerBtn.Location = new Point(cx - w / 2, cy + 105);
            }

            _loginGatePanel.Resize += (s, e) => LayoutGate();

            this.Controls.Add(_loginGatePanel);
            _loginGatePanel.BringToFront();
            LayoutGate();

            // Async login handler
            async Task DoLogin()
            {
                string user = gateUsernameBox.Text.Trim();
                string pass = gatePasswordBox.Text;

                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
                {
                    gateStatusLabel.Text = "Enter username and password";
                    gateStatusLabel.ForeColor = Color.Orange;
                    return;
                }

                loginBtn.Enabled = false;
                registerBtn.Enabled = false;
                gateStatusLabel.Text = "Connecting...";
                gateStatusLabel.ForeColor = AccentCyan;

                _apiClient.PersistSession = keepLoggedInCheck.Checked;
                var (success, error) = await _apiClient.LoginAsync(user, pass);

                if (success)
                {
                    OnLoginGateSuccess();
                }
                else
                {
                    gateStatusLabel.Text = error;
                    gateStatusLabel.ForeColor = Color.Red;
                    loginBtn.Enabled = true;
                    registerBtn.Enabled = true;
                }
            }

            loginBtn.Click += async (s, e) => await DoLogin();
            gatePasswordBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await DoLogin(); };

            // Register handler
            registerBtn.Click += async (s, e) =>
            {
                string user = gateUsernameBox.Text.Trim();
                string pass = gatePasswordBox.Text;

                if (string.IsNullOrEmpty(user) || user.Length < 3)
                {
                    gateStatusLabel.Text = "Username must be at least 3 characters";
                    gateStatusLabel.ForeColor = Color.Orange;
                    return;
                }
                if (string.IsNullOrEmpty(pass) || pass.Length < 4)
                {
                    gateStatusLabel.Text = "Password must be at least 4 characters";
                    gateStatusLabel.ForeColor = Color.Orange;
                    return;
                }

                loginBtn.Enabled = false;
                registerBtn.Enabled = false;
                gateStatusLabel.Text = "Creating account...";
                gateStatusLabel.ForeColor = AccentCyan;

                _apiClient.PersistSession = keepLoggedInCheck.Checked;

                var (success, error) = await _apiClient.RegisterAsync(user, pass, user);

                if (success)
                {
                    OnLoginGateSuccess();
                }
                else
                {
                    gateStatusLabel.Text = error;
                    gateStatusLabel.ForeColor = Color.Red;
                    loginBtn.Enabled = true;
                    registerBtn.Enabled = true;
                }
            };
        }

        private void OnLoginGateSuccess()
        {
            string displayName = _apiClient.CurrentUser?.DisplayName ?? _apiClient.CurrentUser?.Username ?? "User";
            bool isMaster = _apiClient.IsMaster;

            _isMasterLoggedIn = isMaster;
            _loggedInClientName = displayName;

            string secureToken = CreateSecureToken();
            WriteTokenToAllLocations(secureToken, displayName);

            UpdateLoginStatus(true);

            // Remove the login gate
            if (_loginGatePanel != null)
            {
                this.Controls.Remove(_loginGatePanel);
                _loginGatePanel.Dispose();
                _loginGatePanel = null;
                this.PerformLayout();
            }

            // If core mod is missing, lock the launcher
            if (_coreModMissing)
            {
                ShowCoreModLockout();
            }
        }

        private async void CheckExistingLogin()
        {
            if (_apiClient.IsAuthenticated)
            {
                UpdateLoginStatus(true);
                return;
            }

            try
            {
                string tokenPath = GetTokenFilePath();
                if (File.Exists(tokenPath))
                {
                    string rawContent = File.ReadAllText(tokenPath).Trim();
                    
                    // Token format: {hash}|{clientName}|{role}
                    // Legacy format: {hash}|{clientName} (treated as MEMBER)
                    string[] parts = rawContent.Split('|');
                    string token = parts[0];
                    string clientName = parts.Length > 1 ? parts[1] : string.Empty;
                    string role = parts.Length > 2 ? parts[2] : "MEMBER";
                    
                    if (VerifySecureToken(token) || token == "MASTER_ACCESS_GRANTED")
                    {
                        bool isMaster = role.Equals("MASTER", StringComparison.OrdinalIgnoreCase);
                        _isMasterLoggedIn = isMaster;
                        _loggedInClientName = clientName;
                        WriteTokenToAllLocations(CreateSecureToken(), clientName);
                        UpdateLoginStatus(true);
                    }
                    else
                    {
                        DeleteTokenFromAllLocations();
                    }
                }
            }
            catch { }
        }
        
        /// <summary>
        /// Creates a machine-specific encrypted token that can't be copied to other computers
        /// </summary>
        private static string CreateSecureToken()
        {
            // Create a token with machine ID and timestamp
            string machineId = Environment.MachineName + Environment.UserName;
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            string tokenData = $"MASTER_ACCESS|{machineId}|{timestamp}";
            
            // Hash it so it can't be easily read or modified
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(tokenData + HASH_SALT));
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Verifies a token is valid for this machine
        /// </summary>
        private static bool VerifySecureToken(string token)
        {
            // Generate what the token should be for today
            string expectedToken = CreateSecureToken();
            if (token == expectedToken) return true;
            
            // Also check yesterday's token (in case of timezone issues)
            string machineId = Environment.MachineName + Environment.UserName;
            string yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyyMMdd");
            string yesterdayData = $"MASTER_ACCESS|{machineId}|{yesterday}";
            
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(yesterdayData + HASH_SALT));
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            if (token == sb.ToString()) return true;
            
            return false;
        }
        
        private async void PerformMasterLogin()
        {
            string username = _loginUsernameBox.Text;
            string password = _loginPasswordBox.Text;

            if (_loginUsernameBox.ForeColor == TextGray)
                username = string.Empty;

            if (_apiClient.IsConfigured && !string.IsNullOrEmpty(username))
            {
                _loginStatusLabel.Text = "Connecting...";
                _loginStatusLabel.ForeColor = AccentCyan;
                _loginButton.Enabled = false;

                var (success, error) = await _apiClient.LoginAsync(username, password);
                _loginButton.Enabled = true;

                if (success)
                {
                    string displayName = _apiClient.CurrentUser?.DisplayName ?? _apiClient.CurrentUser?.Username ?? username;
                    bool isMaster = _apiClient.IsMaster;
                    string secureToken = CreateSecureToken();
                    WriteTokenToAllLocations(secureToken, displayName);
                    _isMasterLoggedIn = isMaster;
                    _loggedInClientName = displayName;
                    UpdateLoginStatus(true);
                    _loginPasswordBox.Text = "";
                    _loginUsernameBox.Text = "";
                    return;
                }
                else
                {
                    _loginPasswordBox.Text = "";
                    _loginStatusLabel.Text = $"✗ {error}";
                    _loginStatusLabel.ForeColor = Color.Red;
                }
            }
            else
            {
                _loginStatusLabel.Text = "✗ Enter username and password";
                _loginStatusLabel.ForeColor = Color.Red;
            }
        }
        
        /// <summary>
        /// Single click handler for login/logout button - checks state and performs appropriate action
        /// </summary>
        private void LoginButton_Click(object sender, EventArgs e)
        {
            if (_isMasterLoggedIn)
            {
                PerformLogout();
            }
            else
            {
                PerformMasterLogin();
            }
        }
        
        private static int MeasureWrapped(Label label, int width)
        {
            if (string.IsNullOrEmpty(label.Text))
                return 0;
            return TextRenderer.MeasureText(
                label.Text,
                label.Font,
                new Size(Math.Max(20, width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        }

        private void LayoutDetailsPanel()
        {
            if (_detailsPanel == null || _detailsTitleLabel == null)
                return;

            int width = Math.Max(80, _detailsPanel.ClientSize.Width - 24);
            _detailsTitleLabel.SetBounds(12, 32, width, Math.Max(24, MeasureWrapped(_detailsTitleLabel, width)));

            int y = _detailsTitleLabel.Bottom + 8;
            int descH = Math.Max(48, MeasureWrapped(_detailsDescLabel, width));
            if (string.IsNullOrEmpty(_detailsDescLabel.Text))
                descH = 24;
            _detailsDescLabel.SetBounds(12, y, width, descH);
            y = _detailsDescLabel.Bottom + 8;

            int reqWidth = _modSettingsButton.Visible ? Math.Max(80, width - 120) : width;
            int reqH = MeasureWrapped(_detailsReqLabel, reqWidth);
            _detailsReqLabel.SetBounds(12, y, reqWidth, Math.Max(reqH, 0));
            if (_modSettingsButton.Visible)
                _modSettingsButton.Location = new Point(Math.Max(12, _detailsPanel.ClientSize.Width - 122), y);
        }

        private void LayoutAccountBar()
        {
            if (_accountCard == null || _loginButton == null)
                return;

            int width = _accountCard.ClientSize.Width;
            int pad = 12;
            int btnW = 100;
            int adminW = 88;
            int gap = 8;
            int y = 36;
            bool loggedIn = !string.IsNullOrEmpty(_loggedInClientName);

            _loginButton.Size = new Size(btnW, 36);
            _adminPanelButton.Size = new Size(adminW, 36);

            if (loggedIn)
            {
                _loginUsernameBox.Visible = false;
                _loginPasswordBox.Visible = false;
                _loginButton.Location = new Point(Math.Max(pad, width - pad - btnW), y);
                if (_adminPanelButton.Visible)
                    _adminPanelButton.Location = new Point(Math.Max(pad, _loginButton.Left - gap - adminW), y);

                int statusRight = _adminPanelButton.Visible ? _adminPanelButton.Left : _loginButton.Left;
                _loginStatusLabel.Location = new Point(pad, 42);
                _loginStatusLabel.Size = new Size(Math.Max(80, statusRight - pad - gap), 24);
                if (_debugModeCheckBox.Visible)
                    _debugModeCheckBox.Location = new Point(pad, 70);
                return;
            }

            _loginUsernameBox.Visible = true;
            _loginPasswordBox.Visible = true;
            int fieldsBudget = width - pad - btnW - gap - pad;
            int fieldW = Math.Max(96, (fieldsBudget - gap) / 2);
            _loginUsernameBox.Location = new Point(pad, y + 4);
            _loginUsernameBox.Size = new Size(fieldW, 28);
            _loginPasswordBox.Location = new Point(pad + fieldW + gap, y + 4);
            _loginPasswordBox.Size = new Size(fieldW, 28);
            _loginButton.Location = new Point(pad + fieldW * 2 + gap * 2, y);
            _loginStatusLabel.Location = new Point(pad, 76);
            _loginStatusLabel.Size = new Size(Math.Max(80, width - pad * 2), 20);
        }

        private void UpdateLoginStatus(bool loggedIn)
        {
            if (loggedIn)
            {
                string displayName = !string.IsNullOrEmpty(_loggedInClientName) ? _loggedInClientName : "Unknown";
                bool isApiMaster = _apiClient?.IsMaster == true;
                string role = isApiMaster ? "Master" : "Member";
                _loginStatusLabel.Text = $"{displayName} ({role})";
                _loginStatusLabel.ForeColor = SuccessGreen;
                _loginButton.Text = "Log out";
                _loginPasswordBox.Enabled = false;
                _loginUsernameBox.Enabled = false;
                _adminPanelButton.Visible = isApiMaster;

                if (_debugModeCheckBox != null)
                {
                    _debugModeCheckBox.Visible = isApiMaster;
                    _debugModeCheckBox.ForeColor = TextLight;
                }
            }
            else
            {
                _loggedInClientName = null;
                _loginStatusLabel.Text = "Not logged in";
                _loginStatusLabel.ForeColor = TextGray;
                _loginButton.Text = "Log in";
                _loginPasswordBox.Enabled = true;
                _loginUsernameBox.Enabled = true;
                _adminPanelButton.Visible = false;
                
                if (_debugModeCheckBox != null)
                {
                    _debugModeCheckBox.Visible = false;
                    _debugModeCheckBox.Checked = false;
                }
            }

            LayoutAccountBar();
        }
        
        private void PerformLogout()
        {
            try
            {
                _apiClient?.Logout();
                DeleteTokenFromAllLocations();
                _isMasterLoggedIn = false;
                _loggedInClientName = null;
                UpdateLoginStatus(false);

                // Re-show the login gate since login is required
                ShowLoginGate();
            }
            catch (Exception ex)
            {
                ShowCustomMessage($"Failed to logout: {ex.Message}",
                    "Error", MessageBoxIcon.Error);
            }
        }

        private void AdminPanelButton_Click(object sender, EventArgs e)
        {
            if (!_apiClient.IsAuthenticated || !_apiClient.IsMaster)
            {
                ShowCustomMessage("Admin access requires a master account connected to the mod server.",
                    "Access Denied", MessageBoxIcon.Warning);
                return;
            }

            using var adminForm = new AdminPanelForm(_apiClient);
            adminForm.ShowDialog(this);
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            using var settingsForm = new SettingsForm(_holdfastManager, _preferencesManager);
            settingsForm.ShowDialog(this);
        }
        
        private void BrowseModsButton_Click(object sender, EventArgs e)
        {
            using var browserForm = new ModBrowserForm(_modManager, _apiClient);
            browserForm.ShowDialog(this);
            
            // Refresh mods list after closing browser (in case mods were installed/uninstalled)
            LoadMods();
            CheckSetup();
        }
        
        private void OpenModsFolder()
        {
            try
            {
                string modsFolder = _modManager.GetModsFolderPath();
                
                // Ensure folder exists
                if (!Directory.Exists(modsFolder))
                {
                    Directory.CreateDirectory(modsFolder);
                }
                
                // Open in Windows Explorer
                System.Diagnostics.Process.Start("explorer.exe", modsFolder);
                Logger.LogInfo($"Opened Mods folder: {modsFolder}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to open Mods folder: {ex.Message}");
                ConfirmDialog.ShowError($"Could not open Mods folder:\n{ex.Message}", "Error");
            }
        }
        
        private void UninstallMod(string fileName, string fullPath)
        {
            // Prevent uninstalling core mods
            if (_modManager.IsCoreMod(fileName))
            {
                ShowCustomMessage(
                    "Cannot uninstall core mod.\n\n" +
                    "LauncherCoreMod.dll is a core component required for the launcher to function.\n" +
                    "It cannot be disabled or uninstalled.",
                    "Core Mod Protection",
                    MessageBoxIcon.Warning
                );
                return;
            }
            
            // Use custom uninstall dialog
            bool success = ModUninstallDialog.ShowUninstallDialog(fileName, fullPath);
            
            if (success)
            {
                // Refresh the mod list
                LoadMods();
                CheckSetup();
                
                // Clear selection
                _selectedModFileName = null;
                _detailsTitleLabel.Text = "Select a mod to view details";
                _detailsTitleLabel.ForeColor = TextGray;
                _detailsDescLabel.Text = "";
                _detailsReqLabel.Text = "";
                LayoutDetailsPanel();
            }
        }
        
        private static void ShowDisclaimer()
        {
            DisclaimerForm.ShowDisclaimerInfo();
        }
        
        private bool _disclaimerAccepted = false;
        
        private void CheckFirstRunDisclaimer()
        {
            string disclaimerAcceptedFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoldfastModding", "disclaimer_accepted.txt");
            
            if (File.Exists(disclaimerAcceptedFile))
            {
                _disclaimerAccepted = true;
                return;
            }
            
            bool accepted = DisclaimerForm.ShowFirstRunDisclaimer();
            
            if (accepted)
            {
                // Create the folder and file to mark disclaimer as accepted
                string folder = Path.GetDirectoryName(disclaimerAcceptedFile);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(disclaimerAcceptedFile, DateTime.Now.ToString());
                _disclaimerAccepted = true;
            }
            else
            {
                // User declined - lock out the interface
                _disclaimerAccepted = false;
                ShowLockedOutState();
            }
        }
        
        private void ShowLockedOutState()
        {
            // Disable all modding controls
            if (_playButton != null) _playButton.Enabled = false;
            if (_modsScroll != null) _modsScroll.Enabled = false;
            if (_modsPanel != null) _modsPanel.Enabled = false;
            
            // Create lockout overlay
            var lockoutPanel = new Panel
            {
                Name = "lockoutPanel",
                Location = new Point(0, 0),
                Size = this.ClientSize,
                BackColor = Color.FromArgb(240, 18, 18, 22),
                Dock = DockStyle.Fill
            };
            
            var lockIcon = new Panel
            {
                Size = new Size(48, 4),
                BackColor = Theme.Danger
            };
            
            var lockTitle = new Label
            {
                Text = "Disclaimer required",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Theme.Danger,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            
            var lockMessage = new Label
            {
                Text = "You must accept the disclaimer to use the modding launcher.\n\n" +
                       "This tool is for legitimate modding purposes only.\n" +
                       "Misuse for cheating or harassment is prohibited.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(180, 180, 180),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(450, 100),
                BackColor = Color.Transparent
            };
            
            var acceptButton = new Button
            {
                Text = "Review and accept disclaimer",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Size = new Size(280, 44)
            };
            Theme.ApplyPrimaryButton(acceptButton);
            acceptButton.Click += (s, e) =>
            {
                bool accepted = DisclaimerForm.ShowFirstRunDisclaimer();
                if (accepted)
                {
                    string disclaimerAcceptedFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "HoldfastModding", "disclaimer_accepted.txt");
                    string folder = Path.GetDirectoryName(disclaimerAcceptedFile);
                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);
                    File.WriteAllText(disclaimerAcceptedFile, DateTime.Now.ToString());
                    _disclaimerAccepted = true;
                    
                    // Remove lockout and restore UI
                    this.Controls.Remove(lockoutPanel);
                    lockoutPanel.Dispose();
                    if (_playButton != null) _playButton.Enabled = true;
                    if (_modsScroll != null) _modsScroll.Enabled = true;
                    if (_modsPanel != null) _modsPanel.Enabled = true;
                }
            };
            
            var exitButton = new Button
            {
                Text = "Exit",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(100, 36)
            };
            Theme.ApplyGhostButton(exitButton);
            exitButton.Click += (s, e) => Application.Exit();
            
            // Center controls
            lockoutPanel.Controls.Add(lockIcon);
            lockoutPanel.Controls.Add(lockTitle);
            lockoutPanel.Controls.Add(lockMessage);
            lockoutPanel.Controls.Add(acceptButton);
            lockoutPanel.Controls.Add(exitButton);
            
            // Position on resize
            lockoutPanel.Resize += (s, e) =>
            {
                int centerX = lockoutPanel.Width / 2;
                int centerY = lockoutPanel.Height / 2;
                
                lockIcon.Location = new Point(centerX - lockIcon.Width / 2, centerY - 150);
                lockTitle.Location = new Point(centerX - lockTitle.Width / 2, centerY - 70);
                lockMessage.Location = new Point(centerX - lockMessage.Width / 2, centerY - 20);
                acceptButton.Location = new Point(centerX - acceptButton.Width / 2, centerY + 90);
                exitButton.Location = new Point(centerX - exitButton.Width / 2, centerY + 145);
            };
            
            this.Controls.Add(lockoutPanel);
            lockoutPanel.BringToFront();
            
            // Trigger initial layout by manually positioning
            int centerX = lockoutPanel.Width / 2;
            int centerY = lockoutPanel.Height / 2;
            lockIcon.Location = new Point(centerX - lockIcon.Width / 2, centerY - 150);
            lockTitle.Location = new Point(centerX - lockTitle.Width / 2, centerY - 70);
            lockMessage.Location = new Point(centerX - lockMessage.Width / 2, centerY - 20);
            acceptButton.Location = new Point(centerX - acceptButton.Width / 2, centerY + 90);
            exitButton.Location = new Point(centerX - exitButton.Width / 2, centerY + 145);
        }

        private void UpdateModDetails(string modFileName)
        {
            // Toggle selection - if clicking same mod, deselect it
            if (_selectedModFileName == modFileName)
            {
                _selectedModFileName = null;
                modFileName = null;
            }
            else
            {
                _selectedModFileName = modFileName;
            }
            
            // Update visual selection in mods panel
            foreach (Control ctrl in _modsPanel.Controls)
            {
                if (ctrl is Panel modRow)
                {
                    string rowFileName = modRow.Tag as string;
                    bool selected = rowFileName == modFileName && modFileName != null;
                    modRow.BackColor = selected ? Color.FromArgb(54, 46, 32) : Theme.PageBg;
                    foreach (Control child in modRow.Controls)
                    {
                        if (child is ThemeToggle toggle)
                            toggle.BackColor = modRow.BackColor;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(modFileName))
            {
                _detailsTitleLabel.Text = "Select a mod to view details";
                _detailsTitleLabel.ForeColor = TextGray;
                _detailsDescLabel.Text = "";
                _detailsReqLabel.Text = "";
                _modSettingsButton.Visible = false;
                LayoutDetailsPanel();
                return;
            }

            // Find the mod info
            var mods = _modManager.DiscoverMods();
            var mod = mods.FirstOrDefault(m => m.FileName == modFileName);
            
            if (mod == null)
            {
                _detailsTitleLabel.Text = modFileName;
                _detailsTitleLabel.ForeColor = AccentCyan;
                _detailsDescLabel.Text = "No details available.";
                _detailsReqLabel.Text = "";
                LayoutDetailsPanel();
                return;
            }

            // Get display name - prefer ModVersions.json, fallback to manifest, then filename
            string displayName = !string.IsNullOrEmpty(mod.DisplayName) 
                ? mod.DisplayName 
                : Path.GetFileNameWithoutExtension(modFileName);
            
            // Try to get manifest for additional info if ModVersions.json doesn't have it
            ModManifest manifest = null;
            if (_modManifests.ContainsKey(modFileName))
            {
                manifest = _modManifests[modFileName];
            }
            else
            {
                manifest = _versionChecker.ReadModManifest(mod.FullPath);
                if (manifest != null)
                    _modManifests[modFileName] = manifest;
            }
            
            // Use manifest name if ModVersions.json didn't have a display name
            if (string.IsNullOrEmpty(mod.DisplayName) && manifest != null && !string.IsNullOrEmpty(manifest.Name))
            {
                displayName = manifest.Name;
            }

            // Update title
            _detailsTitleLabel.Text = $"{displayName}  (v{mod.Version})";
            _detailsTitleLabel.ForeColor = AccentCyan;

            // Update description - prefer ModVersions.json, fallback to manifest
            string description = null;
            
            // First try ModVersions.json
            if (!string.IsNullOrEmpty(mod.Description))
            {
                description = mod.Description;
            }
            
            // Fallback to manifest (.json file next to DLL)
            if (string.IsNullOrEmpty(description) && manifest != null && !string.IsNullOrEmpty(manifest.Description))
            {
                description = manifest.Description;
            }
            
            // If still no description, actively try to read the manifest
            if (string.IsNullOrEmpty(description))
            {
                var freshManifest = _versionChecker.ReadModManifest(mod.FullPath);
                if (freshManifest != null && !string.IsNullOrEmpty(freshManifest.Description))
                {
                    description = freshManifest.Description;
                    _modManifests[modFileName] = freshManifest;
                }
            }
            
            _detailsDescLabel.Text = !string.IsNullOrEmpty(description) ? description : "No description available.";

            // Update requirements - prefer ModVersions.json
            if (!string.IsNullOrEmpty(mod.Requirements))
            {
                _detailsReqLabel.Text = "Requirements:\n" + mod.Requirements;
            }
            else
            {
                _detailsReqLabel.Text = "";
            }

            // Show settings button for mods with configurable settings
            // Check both filename and mod name (without extension) for flexibility
            bool hasSettings = ModHasSettings(modFileName);
            _modSettingsButton.Visible = hasSettings;
            LayoutDetailsPanel();
            
            Logger.LogInfo($"Mod selected: {modFileName}, hasSettings: {hasSettings}");
        }

        private void ModSettingsButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedModFileName))
                return;

            string holdfastPath = _holdfastManager.FindHoldfastInstallation();
            if (string.IsNullOrEmpty(holdfastPath))
            {
                ConfirmDialog.ShowError("Holdfast installation not found.", "Error");
                return;
            }

            string modName = Path.GetFileNameWithoutExtension(_selectedModFileName);
            if (modName.Equals("CustomSplashScreen", StringComparison.OrdinalIgnoreCase))
            {
                SplashScreenSettingsForm.ShowSettings(holdfastPath, _apiClient);
            }
            else if (modName.Equals("CustomCrosshairs", StringComparison.OrdinalIgnoreCase))
            {
                CrosshairSettingsForm.ShowSettings(holdfastPath, _preferencesManager);
            }
            else if (modName.Equals("AutoBlock", StringComparison.OrdinalIgnoreCase))
            {
                AutoBlockSettingsForm.ShowSettings(holdfastPath);
            }
        }

        private static bool ModHasSettings(string modFileName)
        {
            string name = Path.GetFileNameWithoutExtension(modFileName);
            return name.Equals("CustomSplashScreen", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CustomCrosshairs", StringComparison.OrdinalIgnoreCase)
                || name.Equals("AutoBlock", StringComparison.OrdinalIgnoreCase);
        }

        private async void LoadMods()
        {
            try
            {
                // Clear existing checkboxes
                _modsPanel.Controls.Clear();
                _modToggles.Clear();
                _modManifests.Clear();

                var mods = _modManager.DiscoverMods()
                    .OrderByDescending(m => m.Enabled)
                    .ThenBy(m => ModSortName(m), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mods.Count == 0)
                {
                    var noModsLabel = new Label
                    {
                        Text = "No mods found. Put .dll files in the Mods folder.",
                        Font = new Font("Segoe UI", 9F),
                        ForeColor = TextGray,
                        Location = new Point(15, 15),
                        AutoSize = true,
                        BackColor = Color.Transparent
                    };
                    _modsPanel.Controls.Add(noModsLabel);
                    return;
                }

                int rowWidth = Math.Max(240, _modsPanel.ClientSize.Width);
                int yPos = 0;
                foreach (var mod in mods)
                {
                    var modRow = new Panel
                    {
                        Location = new Point(0, yPos),
                        Size = new Size(rowWidth, 56),
                        BackColor = Theme.PageBg,
                        Tag = mod.FileName,
                        Cursor = Cursors.Hand
                    };
                    modRow.Resize += (s, e) => modRow.Invalidate();
                    modRow.Paint += (s, pe) =>
                    {
                        using var pen = new Pen(Theme.Border);
                        pe.Graphics.DrawLine(pen, 12, modRow.Height - 1, modRow.Width - 12, modRow.Height - 1);
                    };
                    modRow.Click += (s, e) => UpdateModDetails(mod.FileName);

                    var contextMenu = new ContextMenuStrip
                    {
                        BackColor = DarkPanel,
                        ForeColor = TextLight
                    };

                    if (!_modManager.IsCoreMod(mod.FileName))
                    {
                        var uninstallItem = new ToolStripMenuItem("Uninstall mod");
                        uninstallItem.Click += (s, e) => UninstallMod(mod.FileName, mod.FullPath);
                        contextMenu.Items.Add(uninstallItem);
                    }
                    else
                    {
                        contextMenu.Items.Add(new ToolStripMenuItem("Core mod (cannot uninstall)") { Enabled = false });
                    }

                    var openFolderItem = new ToolStripMenuItem("Open mods folder");
                    openFolderItem.Click += (s, e) => System.Diagnostics.Process.Start("explorer.exe", _modManager.GetModsFolderPath());
                    contextMenu.Items.Add(openFolderItem);
                    modRow.ContextMenuStrip = contextMenu;
                    _modsPanel.Controls.Add(modRow);

                    string displayName = !string.IsNullOrEmpty(mod.DisplayName)
                        ? mod.DisplayName
                        : Path.GetFileNameWithoutExtension(mod.FileName);

                    var toggle = new ThemeToggle
                    {
                        Location = new Point(rowWidth - 56, 16),
                        Tag = mod.FileName,
                        BackColor = Theme.PageBg,
                        Checked = _modManager.IsCoreMod(mod.FileName) || mod.Enabled
                    };
                    if (_modManager.IsCoreMod(mod.FileName))
                        toggle.Enabled = false;
                    else
                        toggle.CheckedChanged += ModToggle_CheckedChanged;
                    toggle.ContextMenuStrip = contextMenu;
                    modRow.Controls.Add(toggle);
                    _modToggles.Add(toggle);

                    var modNameLabel = new Label
                    {
                        Text = displayName,
                        Location = new Point(12, 8),
                        Size = new Size(rowWidth - 76, 20),
                        Tag = mod.FileName,
                        ForeColor = TextLight,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                        Cursor = Cursors.Hand
                    };
                    modNameLabel.Click += (s, e) => UpdateModDetails(mod.FileName);
                    modNameLabel.ContextMenuStrip = contextMenu;
                    modRow.Controls.Add(modNameLabel);

                    var versionLabel = new Label
                    {
                        Text = $"v{mod.Version}",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Theme.BrandText,
                        Location = new Point(12, 30),
                        AutoSize = true,
                        BackColor = Color.Transparent,
                        Cursor = Cursors.Hand
                    };
                    versionLabel.Click += (s, e) => UpdateModDetails(mod.FileName);
                    modRow.Controls.Add(versionLabel);

                    var updateLabel = new Label
                    {
                        Text = "",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Theme.Warning,
                        Location = new Point(70, 30),
                        AutoSize = true,
                        Name = $"updateLabel_{mod.FileName}",
                        BackColor = Color.Transparent
                    };
                    modRow.Controls.Add(updateLabel);

                    var updateButton = new Button
                    {
                        Text = "Update",
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        Size = new Size(Math.Max(88, rowWidth - 24), 44),
                        Location = new Point(12, 56),
                        Visible = false,
                        Name = $"updateBtn_{mod.FileName}",
                        Tag = mod.FileName
                    };
                    Theme.ApplyGhostButton(updateButton);
                    updateButton.Click += async (s, e) => await UpdateModAsync(mod.FileName);
                    modRow.Controls.Add(updateButton);

                    yPos += 56;
                }

                StretchModRows();

                // No mod selected by default - user clicks to select
                _selectedModFileName = null;

                // Check for updates asynchronously
                _ = CheckForModUpdatesAsync(mods);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load mods: {ex}");
            }
        }

        private async Task CheckForModUpdatesAsync(List<ModInfo> mods)
        {
            try
            {
                // Fetch the remote registry to check for updates
                var registry = await _modDownloader.FetchRegistryAsync(true);
                if (registry == null)
                {
                    Logger.LogWarning("Could not fetch mod registry for update check");
                    return;
                }

                _remoteModInfo.Clear();

                this.Invoke((MethodInvoker)delegate
                {
                    foreach (var mod in mods)
                    {
                        // Find matching remote mod by DLL name
                        var remoteMod = registry.Mods.FirstOrDefault(r => 
                            r.DllName.Equals(mod.FileName, StringComparison.OrdinalIgnoreCase));

                        if (remoteMod != null)
                        {
                            _remoteModInfo[mod.FileName] = remoteMod;
                            bool hasUpdate = remoteMod.HasUpdate;
                            string latestVersion = remoteMod.Version;

                            mod.HasUpdate = hasUpdate;
                            mod.LatestVersion = latestVersion;

                            // Find the controls in the mod rows
                            foreach (Control panel in _modsPanel.Controls)
                            {
                                if (panel is Panel modRow && modRow.Tag?.ToString() == mod.FileName)
                                {
                                    var updateLabel = modRow.Controls.OfType<Label>()
                                        .FirstOrDefault(l => l.Name == $"updateLabel_{mod.FileName}");
                                    var updateButton = modRow.Controls.OfType<Button>()
                                        .FirstOrDefault(b => b.Name == $"updateBtn_{mod.FileName}");
                                    
                                    if (updateLabel != null)
                                    {
                                        if (hasUpdate)
                                        {
                                            updateLabel.Text = $"v{latestVersion} available";
                                            updateLabel.ForeColor = Color.Orange;
                                        }
                                        else
                                        {
                                            updateLabel.Text = "Latest";
                                            updateLabel.ForeColor = SuccessGreen;
                                        }
                                    }

                                    if (updateButton != null)
                                        updateButton.Visible = hasUpdate;
                                }
                            }
                        }
                        else
                        {
                            // Mod not in registry - can't check for updates
                            foreach (Control panel in _modsPanel.Controls)
                            {
                                if (panel is Panel modRow && modRow.Tag?.ToString() == mod.FileName)
                                {
                                    var updateLabel = modRow.Controls.OfType<Label>()
                                        .FirstOrDefault(l => l.Name == $"updateLabel_{mod.FileName}");
                                    
                                    if (updateLabel != null)
                                    {
                                        updateLabel.Text = "";
                                        updateLabel.ForeColor = TextGray;
                                    }
                                }
                            }
                        }
                    }
                    SortModRows();
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to check for mod updates: {ex.Message}");
            }
        }

        private async Task UpdateModAsync(string modFileName)
        {
            if (!_remoteModInfo.TryGetValue(modFileName, out var remoteMod))
            {
                ShowCustomMessage("Could not find update information for this mod.", "Update Error", MessageBoxIcon.Error);
                return;
            }

            // Confirm update
            bool confirm = ConfirmDialog.ShowConfirm(
                $"Update {remoteMod.Name}?\n\nCurrent: v{remoteMod.InstalledVersion}\nLatest: v{remoteMod.Version}",
                "Confirm Update");

            if (!confirm) return;

            // Show progress
            _statusLabel.Text = $"Updating {remoteMod.Name}...";
            _statusLabel.ForeColor = Color.Orange;
            _progressBar.Visible = true;
            _progressBar.Value = 0;

            try
            {
                var result = await _modDownloader.DownloadAndInstallModAsync(
                    remoteMod, 
                    detailedProgress =>
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            _progressBar.Value = detailedProgress.PercentComplete;
                            // Show detailed progress in status label
                            if (!string.IsNullOrEmpty(detailedProgress.FormattedProgress) && detailedProgress.TotalBytes > 0)
                            {
                                _statusLabel.Text = $"Downloading {remoteMod.Name}: {detailedProgress.FormattedProgress}";
                            }
                            else
                            {
                                _statusLabel.Text = $"Updating {remoteMod.Name}... {detailedProgress.Status}";
                            }
                        });
                    },
                    progress =>
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            _progressBar.Value = progress;
                        });
                    });

                if (result.Success)
                {
                    _statusLabel.Text = $"✓ Updated {remoteMod.Name} to v{remoteMod.Version}";
                    _statusLabel.ForeColor = SuccessGreen;
                    
                    // ALSO copy to BepInEx/plugins so it takes effect immediately
                    string holdfastPath = _holdfastManager.FindHoldfastInstallation();
                    if (!string.IsNullOrEmpty(holdfastPath))
                    {
                        string pluginsDir = Path.Combine(holdfastPath, "BepInEx", "plugins");
                        if (Directory.Exists(pluginsDir))
                        {
                            string sourcePath = result.DownloadedPath;
                            string destPath = Path.Combine(pluginsDir, remoteMod.DllName);
                            try
                            {
                                File.Copy(sourcePath, destPath, true);
                                Logger.LogInfo($"Copied updated mod to BepInEx/plugins: {destPath}");
                            }
                            catch (Exception copyEx)
                            {
                                Logger.LogWarning($"Could not copy to BepInEx/plugins (game may be running): {copyEx.Message}");
                            }
                        }
                    }
                    
                    // Refresh the mods list
                    LoadMods();
                    
                    ConfirmDialog.ShowInfo($"{remoteMod.Name} has been updated to v{remoteMod.Version}!", "Update Complete");
                }
                else
                {
                    _statusLabel.Text = $"✗ Update failed";
                    _statusLabel.ForeColor = Color.Red;
                    ConfirmDialog.ShowError($"Failed to update mod: {result.Message}", "Update Failed");
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"✗ Update failed";
                _statusLabel.ForeColor = Color.Red;
                ConfirmDialog.ShowError($"Error updating mod: {ex.Message}", "Update Error");
            }
            finally
            {
                _progressBar.Visible = false;
            }
        }

        private void StretchModRows()
        {
            LayoutModRows();
        }

        private static string ModSortName(ModInfo mod)
        {
            if (!string.IsNullOrEmpty(mod.DisplayName))
                return mod.DisplayName;
            return Path.GetFileNameWithoutExtension(mod.FileName);
        }

        private void SortModRows()
        {
            if (_modsPanel == null) return;

            var rows = new List<Panel>();
            foreach (Control ctrl in _modsPanel.Controls)
            {
                if (ctrl is Panel row && row.Tag is string)
                    rows.Add(row);
            }

            rows.Sort(CompareModRows);
            for (int i = 0; i < rows.Count; i++)
                _modsPanel.Controls.SetChildIndex(rows[i], i);

            LayoutModRows();
        }

        private static int CompareModRows(Panel a, Panel b)
        {
            int rank = ModRowRank(a).CompareTo(ModRowRank(b));
            if (rank != 0)
                return rank;

            int name = string.Compare(ModRowTitle(a), ModRowTitle(b), StringComparison.OrdinalIgnoreCase);
            if (name != 0)
                return name;

            return string.Compare(a.Tag as string, b.Tag as string, StringComparison.OrdinalIgnoreCase);
        }

        private static int ModRowRank(Panel row)
        {
            var updateButton = row.Controls.OfType<Button>()
                .FirstOrDefault(b => b.Name != null && b.Name.StartsWith("updateBtn_"));
            if (updateButton != null && updateButton.Visible)
                return 0;

            var toggle = row.Controls.OfType<ThemeToggle>().FirstOrDefault();
            if (toggle != null && toggle.Checked)
                return 1;

            return 2;
        }

        private static string ModRowTitle(Panel row)
        {
            foreach (Control child in row.Controls)
            {
                if (child is Label name && name.Font.Bold && name.Tag is string)
                    return name.Text ?? "";
            }

            return row.Tag as string ?? "";
        }

        private void LayoutModRows()
        {
            if (_modsPanel == null) return;
            int width = Math.Max(240, _modsPanel.ClientSize.Width);
            int y = 0;
            foreach (Control ctrl in _modsPanel.Controls)
            {
                if (ctrl is not Panel row || row.Tag is not string)
                    continue;

                var updateButton = row.Controls.OfType<Button>()
                    .FirstOrDefault(b => b.Name != null && b.Name.StartsWith("updateBtn_"));
                bool hasUpdate = updateButton != null && updateButton.Visible;
                int height = hasUpdate ? 108 : 56;
                row.SetBounds(0, y, width, height);

                var toggle = row.Controls.OfType<ThemeToggle>().FirstOrDefault();
                if (toggle != null)
                    toggle.Location = new Point(width - 56, 16);

                foreach (Control child in row.Controls)
                {
                    if (child is Label name && name.Font.Bold && name.Tag is string)
                        name.Size = new Size(Math.Max(80, width - 76), 20);
                }

                updateButton?.SetBounds(12, 56, Math.Max(88, width - 24), 44);

                y += height;
            }
            _modsScroll?.Recalc();
        }

        private void ModToggle_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is not ThemeToggle toggle || toggle.Tag is not string fileName)
                return;

            if (_modManager.IsCoreMod(fileName) && !toggle.Checked)
            {
                toggle.Checked = true;
                return;
            }

            _modManager.SetModEnabled(fileName, toggle.Checked);
            UpdateModCountStatus();
            SortModRows();
        }
        
        private void UpdateModCountStatus()
        {
            int enabledCount = _modToggles.Count(t => t.Checked);
            _statusLabel.Text = $"Ready. {enabledCount} mod(s) enabled.";
            _statusLabel.ForeColor = SuccessGreen;
        }

        private void CheckSetup()
        {
            try
            {
                _statusLabel.Text = "Detecting Holdfast installation...";
                _statusLabel.ForeColor = TextGray;
                
                string holdfastPath = _holdfastManager.FindHoldfastInstallation();
                if (string.IsNullOrEmpty(holdfastPath))
                {
                    _statusLabel.Text = "✗ Holdfast not found. Please install Holdfast: Nations At War.";
                    _statusLabel.ForeColor = Color.Red;
                    _progressBar.Visible = false;
                    return;
                }

                _statusLabel.Text = "Loading mods...";
                
                var mods = _modManager.DiscoverMods();
                int enabledCount = mods.Count(m => m.Enabled);
                
                Logger.LogInfo($"Found {mods.Count} mod(s), {enabledCount} enabled");

                _statusLabel.Text = $"✓ Ready! {enabledCount} mod(s) enabled.";
                _statusLabel.ForeColor = SuccessGreen;
                _progressBar.Visible = false;
                _playButton.Enabled = true;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"✗ Error: {ex.Message}";
                _statusLabel.ForeColor = Color.Red;
                _progressBar.Visible = false;
                
                Logger.LogError($"Setup check failed: {ex}");
            }
        }
        
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                // Check if update checking is enabled
                if (!LauncherSettings.Instance.CheckForUpdatesOnStartup)
                {
                    Logger.LogInfo("Update check disabled by user preference");
                    return;
                }
                
                Logger.LogInfo("Checking for updates...");
                
                var updateInfo = await _updateChecker.CheckForUpdateAsync();
                
                if (updateInfo.UpdateAvailable)
                {
                    // Check if user has skipped this version
                    if (updateInfo.LatestVersion == LauncherSettings.Instance.LastSkippedVersion)
                    {
                        Logger.LogInfo($"Skipping update v{updateInfo.LatestVersion} (user skipped)");
                        return;
                    }

                    if (updateInfo.LatestVersion == _promptedLauncherVersion)
                        return;
                    _promptedLauncherVersion = updateInfo.LatestVersion;
                    
                    Logger.LogInfo($"Update available: v{updateInfo.CurrentVersion} -> v{updateInfo.LatestVersion}");
                    
                    // Show update dialog
                    using var updateDialog = new UpdateDialog(updateInfo, _updateChecker);
                    updateDialog.ShowDialog(this);
                }
                else
                {
                    Logger.LogInfo($"No update available. Current version: v{updateInfo.CurrentVersion}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Update check failed: {ex.Message}");
                // Don't show error to user - update check is non-critical
            }
        }

        private async void PlayButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!VerifyLauncherCoreMod())
                {
                    _coreModMissing = true;
                    ShowCoreModLockout();
                    return;
                }

                string holdfastPath = _holdfastManager.FindHoldfastInstallation();
                if (string.IsNullOrEmpty(holdfastPath))
                {
                    ShowCustomMessage("Holdfast installation not found.", "Error", MessageBoxIcon.Error);
                    return;
                }

                _statusLabel.Text = "Preparing to launch...";
                _statusLabel.ForeColor = TextGray;
                _playButton.Enabled = false;
                _progressBar.Visible = true;

                var enabledMods = _injector.GetEnabledModPaths(_modManager);
                
                Logger.LogInfo($"Launching with {enabledMods.Count} mod(s)");

                // Debug mode only allowed for master login users
                bool debugMode = _isMasterLoggedIn && _debugModeCheckBox.Checked;
                
                var gameProcess = await _injector.LaunchWithModsAsync(
                    holdfastPath, 
                    enabledMods, 
                    debugMode,
                    status => 
                    {
                        if (this.InvokeRequired)
                            this.Invoke((MethodInvoker)delegate { _statusLabel.Text = status; });
                        else
                            _statusLabel.Text = status;
                    }
                );
                
                _progressBar.Visible = false;
                
                if (gameProcess != null)
                {
                    _statusLabel.Text = "Playing...";
                    _statusLabel.ForeColor = SuccessGreen;
                    
                    // Monitor the game process in the background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Wait for the game process to exit
                            await gameProcess.WaitForExitAsync();
                            
                            // Disable BepInEx doorstop so direct Holdfast.exe launches run vanilla
                            _injector.EnsureVanillaByDefault(holdfastPath);
                            
                            // Update UI on the main thread
                            if (this.InvokeRequired)
                            {
                                this.Invoke((MethodInvoker)delegate 
                                { 
                                    _statusLabel.Text = "Ready to play";
                                    _statusLabel.ForeColor = TextGray;
                                    _playButton.Enabled = true;
                                });
                            }
                            else
                            {
                                _statusLabel.Text = "Ready to play";
                                _statusLabel.ForeColor = TextGray;
                                _playButton.Enabled = true;
                            }
                            
                            Logger.LogInfo("Holdfast has closed - BepInEx disabled for vanilla play");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Error monitoring game process: {ex.Message}");
                            // Still disable doorstop and re-enable the button on error
                            try { _injector.EnsureVanillaByDefault(holdfastPath); } catch { }
                            if (this.InvokeRequired)
                            {
                                this.Invoke((MethodInvoker)delegate 
                                { 
                                    _playButton.Enabled = true;
                                    _statusLabel.Text = "Ready to play";
                                    _statusLabel.ForeColor = TextGray;
                                });
                            }
                        }
                    });
                }
                else
                {
                    _statusLabel.Text = "✗ Failed to launch. Check logs.";
                    _statusLabel.ForeColor = Color.Red;
                    _playButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _progressBar.Visible = false;
                ShowCustomMessage($"Failed to launch Holdfast: {ex.Message}", "Error", MessageBoxIcon.Error);
                Logger.LogError($"Launch failed: {ex}");
                _statusLabel.Text = "✗ Launch failed!";
                _statusLabel.ForeColor = Color.Red;
                _playButton.Enabled = true;
            }
        }
        
        /// <summary>
        /// Verifies that LauncherCoreMod.dll exists in the Mods folder.
        /// </summary>
        private bool VerifyLauncherCoreMod()
        {
            try
            {
                string modsFolder = _modManager.GetModsFolderPath();
                string coreModPath = Path.Combine(modsFolder, LAUNCHER_CORE_MOD_NAME);
                
                if (!File.Exists(coreModPath))
                {
                    Logger.LogError($"LauncherCoreMod.dll not found at: {coreModPath}");
                    return false;
                }
                
                Logger.LogInfo("LauncherCoreMod.dll verified successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to verify LauncherCoreMod: {ex.Message}");
                return false;
            }
        }
        
        private void ShowCoreModLockout()
        {
            if (_playButton != null) _playButton.Enabled = false;
            if (_modsScroll != null) _modsScroll.Enabled = false;
            if (_modsPanel != null) _modsPanel.Enabled = false;

            _coreModLockPanel = new Panel
            {
                Name = "coreModLockPanel",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 18, 18, 22)
            };

            var lockIcon = new Panel
            {
                Size = new Size(48, 4),
                BackColor = Theme.Danger
            };

            var lockTitle = new Label
            {
                Text = "Core mod missing",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Theme.Danger,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            var lockMessage = new Label
            {
                Text = "LauncherCoreMod.dll is missing or has been modified.\n\n" +
                       "The launcher cannot function without this core mod.\n" +
                       "Use the Mod Browser to install it, then restart the launcher.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(180, 180, 180),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(500, 100),
                BackColor = Color.Transparent
            };

            var browseButton = new Button
            {
                Text = "Open mod browser",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(240, 44)
            };
            Theme.ApplyPrimaryButton(browseButton);
            browseButton.Click += (s, e) =>
            {
                using var browser = new ModBrowserForm(_modManager, _apiClient);
                browser.ShowDialog(this);

                // Re-check after mod browser closes
                _coreModMissing = !VerifyLauncherCoreMod();
                if (!_coreModMissing)
                {
                    this.Controls.Remove(_coreModLockPanel);
                    _coreModLockPanel.Dispose();
                    _coreModLockPanel = null;
                    if (_playButton != null) _playButton.Enabled = true;
                    if (_modsScroll != null) _modsScroll.Enabled = true;
                    if (_modsPanel != null) _modsPanel.Enabled = true;
                    LoadMods();
                }
            };

            var exitButton = new Button
            {
                Text = "Exit",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(100, 36)
            };
            Theme.ApplyGhostButton(exitButton);
            exitButton.Click += (s, e) => Application.Exit();

            _coreModLockPanel.Controls.AddRange(new Control[] { lockIcon, lockTitle, lockMessage, browseButton, exitButton });

            void LayoutLock()
            {
                int cx = _coreModLockPanel.Width / 2;
                int cy = _coreModLockPanel.Height / 2;
                lockIcon.Location = new Point(cx - lockIcon.Width / 2, cy - 160);
                lockTitle.Location = new Point(cx - lockTitle.Width / 2, cy - 80);
                lockMessage.Location = new Point(cx - lockMessage.Width / 2, cy - 40);
                browseButton.Location = new Point(cx - browseButton.Width / 2, cy + 75);
                exitButton.Location = new Point(cx - exitButton.Width / 2, cy + 130);
            }

            _coreModLockPanel.Resize += (s, e) => LayoutLock();

            this.Controls.Add(_coreModLockPanel);
            _coreModLockPanel.BringToFront();
            LayoutLock();
        }

        private void ShowCustomMessage(string message, string title, MessageBoxIcon icon)
        {
            using var msgForm = new Form();
            msgForm.Text = title;
            msgForm.AutoScaleMode = AutoScaleMode.None;
            msgForm.FormBorderStyle = FormBorderStyle.None;
            msgForm.ClientSize = new Size(420, 180);
            msgForm.StartPosition = FormStartPosition.CenterParent;
            msgForm.BackColor = DarkBg;
            msgForm.ForeColor = TextLight;

            var titleBar = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(0, 0)
            };
            msgForm.Controls.Add(titleBar);

            Color iconColor = icon switch
            {
                MessageBoxIcon.Warning => Theme.Warning,
                MessageBoxIcon.Error => Theme.Danger,
                _ => SuccessGreen
            };

            var titleLbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = iconColor,
                AutoSize = true,
                Location = new Point(18, 12),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(titleLbl);

            var msgLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                BackColor = Color.Transparent
            };
            msgForm.Controls.Add(msgLabel);

            var okBtn = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(85, 36),
                DialogResult = DialogResult.OK
            };
            Theme.ApplyPrimaryButton(okBtn);
            msgForm.Controls.Add(okBtn);

            msgForm.Layout += (s, le) =>
            {
                int w = msgForm.ClientSize.Width;
                int h = msgForm.ClientSize.Height;
                titleBar.Size = new Size(w, 45);
                msgLabel.Location = new Point(22, 60);
                msgLabel.Size = new Size(w - 44, h - 110);
                okBtn.Location = new Point(w - 85 - 20, h - 35 - 10);
            };

            msgForm.Paint += (s, pe) =>
            {
                using var pen = new Pen(Color.FromArgb(50, 50, 55), 2);
                pe.Graphics.DrawRectangle(pen, 0, 0, msgForm.Width - 1, msgForm.Height - 1);
            };

            DialogOwner.ShowCentered(msgForm);
        }
    }
}
