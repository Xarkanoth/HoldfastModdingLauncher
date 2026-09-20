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
        
        private Button _playButton;
        private Button _settingsButton;
        private Button _browseModsButton;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private CheckBox _debugModeCheckBox;
        private Panel _modsPanel;
        private Label _modsLabel;
        private readonly List<CheckBox> _modCheckBoxes = new();
        
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
        private readonly Color AccentMagenta = Theme.Umber;
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
            CheckForUpdatesAsync();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // Form properties - Dark theme, bigger size
            this.Text = "Holdfast Modding";
            this.Size = new Size(880, 820);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            
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
            int formWidth = this.ClientSize.Width;
            int formHeight = this.ClientSize.Height;
            const int pad = 16;
            int contentWidth = formWidth - (pad * 2);

            var titlePanel = new Panel
            {
                BackColor = Theme.HeaderBg,
                Location = new Point(0, 0),
                Size = new Size(formWidth, 56),
                BorderStyle = BorderStyle.None
            };
            titlePanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Theme.Border);
                e.Graphics.DrawLine(pen, 0, titlePanel.Height - 1, titlePanel.Width, titlePanel.Height - 1);
            };
            this.Controls.Add(titlePanel);

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
                Location = new Point(formWidth - 116, 10)
            };
            Theme.ApplyGhostButton(_settingsButton);
            _settingsButton.Click += SettingsButton_Click;
            titlePanel.Controls.Add(_settingsButton);

            var contentPanel = new Panel
            {
                BackColor = DarkBg,
                Location = new Point(0, 56),
                Size = new Size(formWidth, formHeight - 56),
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(contentPanel);

            var statusCard = new ThemePanel
            {
                Location = new Point(pad, 12),
                Size = new Size(contentWidth, 70)
            };
            contentPanel.Controls.Add(statusCard);

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
                AutoSize = true,
                Location = new Point(12, 28),
                BackColor = Color.Transparent
            };
            statusCard.Controls.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                Location = new Point(12, 50),
                Size = new Size(contentWidth - 24, 8),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };
            statusCard.Controls.Add(_progressBar);

            var modsCard = new ThemePanel
            {
                Location = new Point(pad, 94),
                Size = new Size(contentWidth, 210)
            };
            contentPanel.Controls.Add(modsCard);

            _modsLabel = new Label
            {
                Text = "Installed mods",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(12, 10),
                BackColor = Color.Transparent
            };
            modsCard.Controls.Add(_modsLabel);

            _browseModsButton = new Button
            {
                Text = "Browse mods",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(120, 36),
                Location = new Point(contentWidth - 256, 6)
            };
            Theme.ApplyPrimaryButton(_browseModsButton);
            _browseModsButton.Click += BrowseModsButton_Click;
            modsCard.Controls.Add(_browseModsButton);

            var openModsFolderButton = new Button
            {
                Text = "Open folder",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(120, 36),
                Location = new Point(contentWidth - 128, 6)
            };
            Theme.ApplyGhostButton(openModsFolderButton);
            openModsFolderButton.Click += (s, e) => OpenModsFolder();
            modsCard.Controls.Add(openModsFolderButton);

            _modsPanel = new Panel
            {
                Location = new Point(8, 48),
                Size = new Size(contentWidth - 16, 154),
                AutoScroll = true,
                BackColor = Theme.PanelAlt,
                BorderStyle = BorderStyle.None
            };
            modsCard.Controls.Add(_modsPanel);

            var detailsCard = new ThemePanel
            {
                Location = new Point(pad, 316),
                Size = new Size(contentWidth, 168)
            };
            contentPanel.Controls.Add(detailsCard);

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
                Location = new Point(12, 32),
                Size = new Size(contentWidth - 24, 24),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsTitleLabel);

            _detailsDescLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextLight,
                Location = new Point(12, 56),
                Size = new Size(contentWidth - 24, 60),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsDescLabel);

            _detailsReqLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Theme.Warning,
                Location = new Point(12, 118),
                Size = new Size(contentWidth - 140, 40),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsReqLabel);

            _modSettingsButton = new Button
            {
                Text = "Settings",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(110, 36),
                Location = new Point(contentWidth - 122, 122),
                Visible = false
            };
            Theme.ApplyGhostButton(_modSettingsButton);
            _modSettingsButton.Click += ModSettingsButton_Click;
            _detailsPanel.Controls.Add(_modSettingsButton);
            _modSettingsButton.BringToFront();

            var accountCard = new ThemePanel
            {
                Location = new Point(pad, 496),
                Size = new Size(contentWidth, 88)
            };
            contentPanel.Controls.Add(accountCard);

            var loginSectionLabel = new Label
            {
                Text = "Account",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Theme.BrandText,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };
            accountCard.Controls.Add(loginSectionLabel);

            _loginUsernameBox = new TextBox
            {
                Location = new Point(12, 36),
                Size = new Size(140, 28),
                Font = new Font("Segoe UI", 9F),
                Text = ""
            };
            Theme.ApplyInput(_loginUsernameBox);
            _loginUsernameBox.GotFocus += (s, e) => { if (_loginUsernameBox.ForeColor == TextGray) { _loginUsernameBox.Text = ""; _loginUsernameBox.ForeColor = TextLight; } };
            _loginUsernameBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(_loginUsernameBox.Text)) { _loginUsernameBox.ForeColor = TextGray; _loginUsernameBox.Text = "Username"; } };
            _loginUsernameBox.ForeColor = TextGray;
            _loginUsernameBox.Text = "Username";
            accountCard.Controls.Add(_loginUsernameBox);

            _loginPasswordBox = new TextBox
            {
                Location = new Point(160, 36),
                Size = new Size(140, 28),
                Font = new Font("Segoe UI", 9F),
                UseSystemPasswordChar = true
            };
            Theme.ApplyInput(_loginPasswordBox);
            accountCard.Controls.Add(_loginPasswordBox);
            _loginPasswordBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) PerformMasterLogin(); };

            _loginButton = new Button
            {
                Text = "Log in",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(80, 36),
                Location = new Point(308, 32)
            };
            Theme.ApplyPrimaryButton(_loginButton);
            _loginButton.Click += LoginButton_Click;
            accountCard.Controls.Add(_loginButton);

            _adminPanelButton = new Button
            {
                Text = "Admin",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(80, 36),
                Location = new Point(396, 32),
                Visible = false
            };
            Theme.ApplyGhostButton(_adminPanelButton);
            _adminPanelButton.Click += AdminPanelButton_Click;
            accountCard.Controls.Add(_adminPanelButton);

            _loginStatusLabel = new Label
            {
                Text = "Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(488, 40),
                BackColor = Color.Transparent
            };
            accountCard.Controls.Add(_loginStatusLabel);

            _debugModeCheckBox = new CheckBox
            {
                Text = "Show debug console",
                Location = new Point(12, 64),
                AutoSize = true,
                ForeColor = TextGray,
                BackColor = Color.Transparent,
                Checked = false,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand,
                Visible = false
            };
            accountCard.Controls.Add(_debugModeCheckBox);

            CheckExistingLogin();

            _playButton = new Button
            {
                Text = "Launch Holdfast",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(220, 44),
                Location = new Point(formWidth - pad - 220, 596),
                Enabled = false
            };
            Theme.ApplyPrimaryButton(_playButton);
            _playButton.Click += PlayButton_Click;
            contentPanel.Controls.Add(_playButton);

            var disclaimerLabel = new Label
            {
                Text = "Unofficial tool. PC only. Not affiliated with Anvil Game Studios.",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Theme.Warning,
                AutoSize = true,
                Location = new Point(pad, 600),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            disclaimerLabel.Click += (s, e) => ShowDisclaimer();
            var disclaimerTooltip = new ToolTip();
            disclaimerTooltip.SetToolTip(disclaimerLabel, "Click for full disclaimer");
            contentPanel.Controls.Add(disclaimerLabel);

            var creditLabel = new Label
            {
                Text = "Built by Xarkanoth  ·  Discord.gg/csg",
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(pad, 618),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(creditLabel);

            var donateButton = new Button
            {
                Text = "Support",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Size = new Size(80, 28),
                Location = new Point(pad + 210, 614)
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
            contentPanel.Controls.Add(donateButton);

            var versionLabel = new Label
            {
                Text = GetVersionString(),
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(formWidth - 80, 650),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(versionLabel);

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
        private bool VerifySecureToken(string token)
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
        
        private void UpdateLoginStatus(bool loggedIn)
        {
            if (loggedIn)
            {
                string displayName = !string.IsNullOrEmpty(_loggedInClientName) ? _loggedInClientName : "Unknown";
                bool isApiMaster = _apiClient?.IsMaster == true;
                string role = isApiMaster ? "Master" : "Member";
                _loginStatusLabel.Text = $"{displayName} ({role})";
                _loginStatusLabel.ForeColor = SuccessGreen;
                _loginButton.Text = "Logout";
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
                _loginButton.Text = "Login";
                _loginPasswordBox.Enabled = true;
                _loginUsernameBox.Enabled = true;
                _adminPanelButton.Visible = false;
                
                if (_debugModeCheckBox != null)
                {
                    _debugModeCheckBox.Visible = false;
                    _debugModeCheckBox.Checked = false;
                }
            }
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
                    if (rowFileName == modFileName && modFileName != null)
                    {
                        modRow.BackColor = Color.FromArgb(50, 60, 70); // Highlighted
                    }
                    else
                    {
                        modRow.BackColor = Color.Transparent; // Normal
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
            string modNameNoExt = Path.GetFileNameWithoutExtension(modFileName);
            bool hasSettings = modFileName.Equals("CustomSplashScreen.dll", StringComparison.OrdinalIgnoreCase) ||
                               modNameNoExt.Equals("CustomSplashScreen", StringComparison.OrdinalIgnoreCase) ||
                               modFileName.Equals("CustomCrosshairs.dll", StringComparison.OrdinalIgnoreCase) ||
                               modNameNoExt.Equals("CustomCrosshairs", StringComparison.OrdinalIgnoreCase);
            _modSettingsButton.Visible = hasSettings;
            
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

            // Open settings for specific mods
            string modNameNoExt = Path.GetFileNameWithoutExtension(_selectedModFileName);
            if (_selectedModFileName.Equals("CustomSplashScreen.dll", StringComparison.OrdinalIgnoreCase) ||
                modNameNoExt.Equals("CustomSplashScreen", StringComparison.OrdinalIgnoreCase))
            {
                SplashScreenSettingsForm.ShowSettings(holdfastPath, _apiClient);
            }
            else if (_selectedModFileName.Equals("CustomCrosshairs.dll", StringComparison.OrdinalIgnoreCase) ||
                     modNameNoExt.Equals("CustomCrosshairs", StringComparison.OrdinalIgnoreCase))
            {
                CrosshairSettingsForm.ShowSettings(holdfastPath, _preferencesManager);
            }
        }

        private async void LoadMods()
        {
            try
            {
                // Clear existing checkboxes
                _modsPanel.Controls.Clear();
                _modCheckBoxes.Clear();
                _modManifests.Clear();

                var mods = _modManager.DiscoverMods();

                if (mods.Count == 0)
                {
                    var noModsLabel = new Label
                    {
                        Text = "No mods found. Place .dll files in the 'Mods' folder.",
                        Font = new Font("Segoe UI", 9F),
                        ForeColor = TextGray,
                        Location = new Point(15, 15),
                        AutoSize = true,
                        BackColor = Color.Transparent
                    };
                    _modsPanel.Controls.Add(noModsLabel);
                    return;
                }

                // Display mods
                int yPos = 10;
                foreach (var mod in mods)
                {
                    // Create a panel for each mod row
                    var modRow = new Panel
                    {
                        Location = new Point(5, yPos),
                        Size = new Size(_modsPanel.Width - 30, 32),
                        BackColor = Color.Transparent,
                        Tag = mod.FileName,
                        Cursor = Cursors.Hand
                    };
                    modRow.Click += (s, e) => UpdateModDetails(mod.FileName);
                    
                    // Add right-click context menu for uninstall
                    var contextMenu = new ContextMenuStrip
                    {
                        BackColor = DarkPanel,
                        ForeColor = TextLight
                    };
                    
                    // Core mods cannot be uninstalled
                    if (!_modManager.IsCoreMod(mod.FileName))
                    {
                        var uninstallItem = new ToolStripMenuItem("Uninstall mod");
                        uninstallItem.Click += (s, e) => UninstallMod(mod.FileName, mod.FullPath);
                        contextMenu.Items.Add(uninstallItem);
                    }
                    else
                    {
                        var uninstallItem = new ToolStripMenuItem("Core mod (cannot uninstall)")
                        {
                            Enabled = false
                        };
                        contextMenu.Items.Add(uninstallItem);
                    }
                    
                    var openFolderItem = new ToolStripMenuItem("Open mods folder");
                    openFolderItem.Click += (s, e) => System.Diagnostics.Process.Start("explorer.exe", _modManager.GetModsFolderPath());
                    contextMenu.Items.Add(openFolderItem);
                    modRow.ContextMenuStrip = contextMenu;
                    
                    _modsPanel.Controls.Add(modRow);

                    // Use display name if available, otherwise fallback to filename without extension
                    string displayName = !string.IsNullOrEmpty(mod.DisplayName) 
                        ? mod.DisplayName 
                        : Path.GetFileNameWithoutExtension(mod.FileName);
                    
                    // Checkbox only (no text) - for enabling/disabling the mod
                    var checkBox = new CheckBox
                    {
                        Text = "",
                        Location = new Point(8, 6),
                        Checked = mod.Enabled,
                        Size = new Size(18, 18),
                        Tag = mod.FileName,
                        BackColor = Color.Transparent,
                        Cursor = Cursors.Hand
                    };
                    
                    // Core mods cannot be disabled
                    if (_modManager.IsCoreMod(mod.FileName))
                    {
                        checkBox.Enabled = false;
                        checkBox.Checked = true; // Always checked for core mods
                    }
                    else
                    {
                        checkBox.CheckedChanged += ModCheckBox_CheckedChanged;
                    }
                    
                    checkBox.ContextMenuStrip = contextMenu;
                    modRow.Controls.Add(checkBox);
                    _modCheckBoxes.Add(checkBox);
                    
                    // Separate label for mod name - clicking this only selects the mod (shows details)
                    var modNameLabel = new Label
                    {
                        Text = displayName,
                        Location = new Point(30, 6),
                        AutoSize = true,
                        Tag = mod.FileName,
                        ForeColor = TextLight,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 10F),
                        Cursor = Cursors.Hand
                    };
                    modNameLabel.Click += (s, e) => UpdateModDetails(mod.FileName);
                    modNameLabel.ContextMenuStrip = contextMenu;
                    modRow.Controls.Add(modNameLabel);

                    // Add version info
                    var versionLabel = new Label
                    {
                        Text = $"v{mod.Version}",
                        Font = new Font("Segoe UI", 9F),
                        ForeColor = AccentCyan,
                        Location = new Point(280, 8),
                        AutoSize = true,
                        BackColor = Color.Transparent
                    };
                    versionLabel.Click += (s, e) => UpdateModDetails(mod.FileName);
                    modRow.Controls.Add(versionLabel);

                    // Add file size info
                    var sizeLabel = new Label
                    {
                        Text = $"({FormatFileSize(mod.FileSize)})",
                        Font = new Font("Segoe UI", 9F),
                        ForeColor = TextGray,
                        Location = new Point(350, 8),
                        AutoSize = true,
                        BackColor = Color.Transparent
                    };
                    sizeLabel.Click += (s, e) => UpdateModDetails(mod.FileName);
                    modRow.Controls.Add(sizeLabel);

                    // Update indicator label
                    var updateLabel = new Label
                    {
                        Text = "",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Color.Orange,
                        Location = new Point(420, 8),
                        AutoSize = true,
                        Name = $"updateLabel_{mod.FileName}",
                        BackColor = Color.Transparent
                    };
                    modRow.Controls.Add(updateLabel);

                    // Update button (hidden by default)
                    var updateButton = new Button
                    {
                        Text = "⬆ Update",
                        Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                        Size = new Size(70, 24),
                        Location = new Point(520, 4),
                        BackColor = Color.FromArgb(40, 40, 50),
                        ForeColor = Color.Orange,
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand,
                        Visible = false,
                        Name = $"updateBtn_{mod.FileName}",
                        Tag = mod.FileName
                    };
                    updateButton.FlatAppearance.BorderColor = Color.Orange;
                    updateButton.FlatAppearance.BorderSize = 1;
                    updateButton.Click += async (s, e) => await UpdateModAsync(mod.FileName);
                    modRow.Controls.Add(updateButton);

                    yPos += 36;
                }

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
                                            updateLabel.Text = "✓ Latest";
                                            updateLabel.ForeColor = SuccessGreen;
                                        }
                                    }

                                    if (updateButton != null)
                                    {
                                        updateButton.Visible = hasUpdate;
                                    }
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

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ModCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string fileName)
            {
                // Double-check: prevent disabling core mods even if checkbox is somehow unchecked
                if (_modManager.IsCoreMod(fileName) && !checkBox.Checked)
                {
                    checkBox.Checked = true;
                    return;
                }
                
                _modManager.SetModEnabled(fileName, checkBox.Checked);
                UpdateModCountStatus();
            }
        }
        
        private void UpdateModCountStatus()
        {
            int enabledCount = _modCheckBoxes.Count(cb => cb.Checked);
            _statusLabel.Text = $"✓ Ready! {enabledCount} mod(s) enabled.";
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
        
        private async void CheckForUpdatesAsync()
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

            msgForm.ShowDialog(this);
        }
    }
}
