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
        private readonly Injector _injector;
        private readonly PreferencesManager _preferencesManager;
        
        private Button _playButton;
        private Button _settingsButton;
        private Button _browseModsButton;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private CheckBox _debugModeCheckBox;
        private Panel _modsPanel;
        private Label _modsLabel;
        private List<CheckBox> _modCheckBoxes = new List<CheckBox>();
        
        // Selected mod details
        private Panel _detailsPanel;
        private Label _detailsTitleLabel;
        private Label _detailsDescLabel;
        private Label _detailsReqLabel;
        private string _selectedModFileName = null;
        private Dictionary<string, ModManifest> _modManifests = new Dictionary<string, ModManifest>();
        
        // Master login (bypasses RC login requirement)
        private TextBox _loginPasswordBox;
        private Button _loginButton;
        private Label _loginStatusLabel;
        private bool _isMasterLoggedIn = false;
        
        // Secure password hash - SHA256 of password with salt (password is never stored)
        // This hash cannot be reversed to get the original password
        // Even if someone sees this hash, they cannot determine the password
        private const string PASSWORD_HASH = "5af29f81b2678084c9ffb40fcfeb0ee8287f4f5a16fb73229aca874503097728";
        private const string HASH_SALT = "HF_MODDING_2024_XARK";
        private const string LOGIN_TOKEN_FILE = "master_login.token";

        // Dark theme colors (matching InstallerForm)
        private readonly Color DarkBg = Color.FromArgb(18, 18, 18);
        private readonly Color DarkPanel = Color.FromArgb(28, 28, 28);
        private readonly Color AccentCyan = Color.FromArgb(0, 255, 255);
        private readonly Color AccentMagenta = Color.FromArgb(255, 0, 255);
        private readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private readonly Color TextGray = Color.FromArgb(180, 180, 180);
        private readonly Color SuccessGreen = Color.FromArgb(0, 255, 127);

        public MainForm(bool debugMode = false)
        {
            _holdfastManager = new HoldfastManager();
            _modManager = new ModManager();
            _versionChecker = new ModVersionChecker();
            _updateChecker = new UpdateChecker();
            _injector = new Injector();
            _preferencesManager = new PreferencesManager();
            
            InitializeComponent();
            InitializeUI();
            
            // Check first-run disclaimer
            CheckFirstRunDisclaimer();
            
            // Perform initial setup check
            CheckSetup();
            
            // Check for updates on startup (if enabled)
            CheckForUpdatesAsync();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // Form properties - Dark theme, bigger size
            this.Text = "Holdfast Modding Launcher";
            this.Size = new Size(850, 850);
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
            
            // Title panel
            var titlePanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(0, 0),
                Size = new Size(formWidth, 80),
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(titlePanel);

            // Title label
            var titleLabel = new Label
            {
                Text = "HOLDFAST MODDING LAUNCHER",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(25, 25),
                BackColor = Color.Transparent
            };
            titlePanel.Controls.Add(titleLabel);

            // Settings button (gear icon)
            _settingsButton = new Button
            {
                Text = "⚙",
                Font = new Font("Segoe UI", 16F),
                Size = new Size(45, 45),
                Location = new Point(formWidth - 70, 18),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _settingsButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _settingsButton.FlatAppearance.BorderSize = 1;
            _settingsButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 50);
            _settingsButton.Click += SettingsButton_Click;
            
            // Add tooltip for settings button
            var settingsTooltip = new ToolTip();
            settingsTooltip.SetToolTip(_settingsButton, "Settings");
            
            titlePanel.Controls.Add(_settingsButton);

            // Main content panel
            var contentPanel = new Panel
            {
                BackColor = DarkBg,
                Location = new Point(0, 80),
                Size = new Size(formWidth, formHeight - 80),
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(contentPanel);

            // Status section
            var statusSectionLabel = new Label
            {
                Text = "STATUS",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(25, 15),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(statusSectionLabel);

            _statusLabel = new Label
            {
                Text = "Checking installation...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(25, 38),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                Location = new Point(25, 60),
                Size = new Size(formWidth - 50, 6),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };
            contentPanel.Controls.Add(_progressBar);

            // Mods section label
            _modsLabel = new Label
            {
                Text = "INSTALLED MODS",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(25, 85),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(_modsLabel);
            
            // Browse Mods button
            _browseModsButton = new Button
            {
                Text = "📦 Browse & Download Mods",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(200, 28),
                Location = new Point(formWidth - 225, 80),
                BackColor = DarkPanel,
                ForeColor = Color.FromArgb(255, 165, 0), // Orange
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _browseModsButton.FlatAppearance.BorderColor = Color.FromArgb(255, 165, 0);
            _browseModsButton.FlatAppearance.BorderSize = 1;
            _browseModsButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 50, 30);
            _browseModsButton.Click += BrowseModsButton_Click;
            contentPanel.Controls.Add(_browseModsButton);

            // Open Mods Folder button
            var openModsFolderButton = new Button
            {
                Text = "📁 Open Mods Folder",
                Font = new Font("Segoe UI", 9F),
                Size = new Size(140, 28),
                Location = new Point(formWidth - 435, 80),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            openModsFolderButton.FlatAppearance.BorderColor = TextGray;
            openModsFolderButton.FlatAppearance.BorderSize = 1;
            openModsFolderButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 50);
            openModsFolderButton.Click += (s, e) => OpenModsFolder();
            contentPanel.Controls.Add(openModsFolderButton);

            // Mods panel (scrollable list) - Increased height
            _modsPanel = new Panel
            {
                Location = new Point(25, 110),
                Size = new Size(formWidth - 50, 180),
                AutoScroll = true,
                BackColor = DarkPanel,
                BorderStyle = BorderStyle.None
            };
            contentPanel.Controls.Add(_modsPanel);

            // Selected mod details section - Moved down
            var detailsSectionLabel = new Label
            {
                Text = "MOD DETAILS",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentMagenta,
                AutoSize = true,
                Location = new Point(25, 305),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(detailsSectionLabel);

            _detailsPanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(25, 330),
                Size = new Size(formWidth - 50, 220),
                BorderStyle = BorderStyle.None
            };
            contentPanel.Controls.Add(_detailsPanel);

            // Details title
            _detailsTitleLabel = new Label
            {
                Text = "Select a mod to view details",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextGray,
                Location = new Point(15, 12),
                Size = new Size(formWidth - 80, 28),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsTitleLabel);

            // Details description - More space
            _detailsDescLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextLight,
                Location = new Point(15, 45),
                Size = new Size(formWidth - 80, 100),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsDescLabel);

            // Details requirements - More space
            _detailsReqLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Orange,
                Location = new Point(15, 150),
                Size = new Size(formWidth - 80, 60),
                BackColor = Color.Transparent
            };
            _detailsPanel.Controls.Add(_detailsReqLabel);

            // Master Login section - Moved down
            var loginSectionLabel = new Label
            {
                Text = "MASTER LOGIN",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentMagenta,
                AutoSize = true,
                Location = new Point(25, 565),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(loginSectionLabel);
            
            _loginPasswordBox = new TextBox
            {
                Location = new Point(25, 590),
                Size = new Size(200, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true
            };
            contentPanel.Controls.Add(_loginPasswordBox);
            _loginPasswordBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) PerformMasterLogin(); };
            
            _loginButton = new Button
            {
                Text = "Login",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(80, 28),
                Location = new Point(235, 590),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _loginButton.FlatAppearance.BorderColor = AccentCyan;
            _loginButton.FlatAppearance.BorderSize = 1;
            _loginButton.Click += (s, e) => PerformMasterLogin();
            contentPanel.Controls.Add(_loginButton);
            
            _loginStatusLabel = new Label
            {
                Text = "○ Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(330, 595),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(_loginStatusLabel);

            // Debug mode checkbox - Only visible to master login users
            // MUST be created BEFORE CheckExistingLogin() so it can be shown/hidden
            _debugModeCheckBox = new CheckBox
            {
                Text = "  🔧 Show Debug Console (Master Only)",
                Location = new Point(25, 635),
                AutoSize = true,
                ForeColor = TextGray,
                BackColor = Color.Transparent,
                Checked = false,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand,
                Visible = false  // Hidden by default, shown only when master logged in
            };
            contentPanel.Controls.Add(_debugModeCheckBox);
            
            // Check if already logged in (token file exists)
            // This will show/hide the debug checkbox based on login status
            CheckExistingLogin();

            // Play button - Moved down
            _playButton = new Button
            {
                Text = "▶  LAUNCH HOLDFAST",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Size = new Size(280, 55),
                Location = new Point(formWidth - 305, 615),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _playButton.FlatAppearance.BorderColor = AccentCyan;
            _playButton.FlatAppearance.BorderSize = 2;
            _playButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 60, 60);
            _playButton.Click += PlayButton_Click;
            contentPanel.Controls.Add(_playButton);

            // Disclaimer label at bottom
            var disclaimerLabel = new Label
            {
                Text = "⚠ UNOFFICIAL TOOL - PC Only - Not affiliated with Anvil Game Studios",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(255, 180, 100),
                AutoSize = true,
                Location = new Point(25, 678),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            disclaimerLabel.Click += (s, e) => ShowDisclaimer();
            var disclaimerTooltip = new ToolTip();
            disclaimerTooltip.SetToolTip(disclaimerLabel, "Click for full disclaimer");
            contentPanel.Controls.Add(disclaimerLabel);
            
            // Credit label at bottom - Moved down
            var creditLabel = new Label
            {
                Text = "Built by Xarkanoth  •  Discord.gg/csg",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(25, 695),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(creditLabel);

            // Version label - Moved down
            var versionLabel = new Label
            {
                Text = GetVersionString(),
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(formWidth - 80, 690),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(versionLabel);

            // Load and display mods
            LoadMods();
        }

        private string GetVersionString()
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
        
        private string GetTokenFilePath()
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
        
        private void WriteTokenToAllLocations(string content)
        {
            // Write to AppData (primary location)
            string primaryPath = GetTokenFilePath();
            File.WriteAllText(primaryPath, content);
            
            // Also try to write to the game's BepInEx folder if game path is known
            try
            {
                string gamePath = _holdfastManager.FindHoldfastInstallation();
                if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                {
                    string bepInExPlugins = Path.Combine(gamePath, "BepInEx", "plugins");
                    if (Directory.Exists(bepInExPlugins))
                    {
                        File.WriteAllText(Path.Combine(bepInExPlugins, LOGIN_TOKEN_FILE), content);
                    }
                    
                    // Also write to Mods subfolder if it exists
                    string modsFolder = Path.Combine(bepInExPlugins, "Mods");
                    if (Directory.Exists(modsFolder))
                    {
                        File.WriteAllText(Path.Combine(modsFolder, LOGIN_TOKEN_FILE), content);
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
        
        private void CheckExistingLogin()
        {
            try
            {
                string tokenPath = GetTokenFilePath();
                if (File.Exists(tokenPath))
                {
                    // Token exists - verify it's valid for this machine
                    string token = File.ReadAllText(tokenPath).Trim();
                    
                    // Support old format for backwards compatibility (one-time migration)
                    if (token == "MASTER_ACCESS_GRANTED")
                    {
                        // Upgrade to new secure token
                        string secureToken = CreateSecureToken();
                        WriteTokenToAllLocations(secureToken);
                        _isMasterLoggedIn = true;
                        UpdateLoginStatus(true);
                    }
                    else if (VerifySecureToken(token))
                    {
                        _isMasterLoggedIn = true;
                        UpdateLoginStatus(true);
                    }
                    else
                    {
                        // Invalid token - delete it
                        DeleteTokenFromAllLocations();
                    }
                }
            }
            catch { }
        }
        
        /// <summary>
        /// Computes SHA256 hash of password with salt - one-way, cannot be reversed
        /// </summary>
        private string ComputePasswordHash(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                // Combine password with salt
                string saltedPassword = password + HASH_SALT;
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
                
                // Convert to hex string
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
        
        /// <summary>
        /// Creates a machine-specific encrypted token that can't be copied to other computers
        /// </summary>
        private string CreateSecureToken()
        {
            // Create a token with machine ID and timestamp
            string machineId = Environment.MachineName + Environment.UserName;
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            string tokenData = $"MASTER_ACCESS|{machineId}|{timestamp}";
            
            // Hash it so it can't be easily read or modified
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(tokenData + HASH_SALT));
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
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
            
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(yesterdayData + HASH_SALT));
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                if (token == sb.ToString()) return true;
            }
            
            return false;
        }
        
        private void PerformMasterLogin()
        {
            string password = _loginPasswordBox.Text;
            
            // Hash the input password and compare to stored hash
            string inputHash = ComputePasswordHash(password);
            
            if (inputHash == PASSWORD_HASH)
            {
                // Success - write secure token file
                try
                {
                    string secureToken = CreateSecureToken();
                    WriteTokenToAllLocations(secureToken);
                    _isMasterLoggedIn = true;
                    UpdateLoginStatus(true);
                    _loginPasswordBox.Text = "";
                    ShowCustomMessage("Master login successful!\n\nYou now have admin access on all servers without RC login.", 
                        "Login Successful", MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    ShowCustomMessage($"Failed to save login token: {ex.Message}", 
                        "Error", MessageBoxIcon.Error);
                }
            }
            else
            {
                // Failed
                _loginPasswordBox.Text = "";
                _loginStatusLabel.Text = "✗ Invalid password";
                _loginStatusLabel.ForeColor = Color.Red;
            }
        }
        
        private void UpdateLoginStatus(bool loggedIn)
        {
            if (loggedIn)
            {
                _loginStatusLabel.Text = "✓ Master access granted";
                _loginStatusLabel.ForeColor = SuccessGreen;
                _loginButton.Text = "Logout";
                _loginButton.Click -= (s, e) => PerformMasterLogin();
                _loginButton.Click += (s, e) => PerformLogout();
                _loginPasswordBox.Enabled = false;
                
                // Show debug console option for master users
                if (_debugModeCheckBox != null)
                {
                    _debugModeCheckBox.Visible = true;
                    _debugModeCheckBox.ForeColor = TextLight;
                }
            }
            else
            {
                _loginStatusLabel.Text = "○ Not logged in";
                _loginStatusLabel.ForeColor = TextGray;
                _loginButton.Text = "Login";
                _loginPasswordBox.Enabled = true;
                
                // Hide and uncheck debug console option
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
                DeleteTokenFromAllLocations();
                _isMasterLoggedIn = false;
                
                // Reset button handler
                _loginButton.Click -= (s, e) => PerformLogout();
                _loginButton.Click += (s, e) => PerformMasterLogin();
                
                UpdateLoginStatus(false);
            }
            catch (Exception ex)
            {
                ShowCustomMessage($"Failed to logout: {ex.Message}", 
                    "Error", MessageBoxIcon.Error);
            }
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new SettingsForm(_holdfastManager, _preferencesManager))
            {
                settingsForm.ShowDialog(this);
            }
        }
        
        private void BrowseModsButton_Click(object sender, EventArgs e)
        {
            using (var browserForm = new ModBrowserForm(_modManager))
            {
                browserForm.ShowDialog(this);
                
                // Refresh mods list after closing browser (in case mods were installed/uninstalled)
                LoadMods();
                CheckSetup();
            }
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
        
        private void ShowDisclaimer()
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
            
            var lockIcon = new Label
            {
                Text = "🔒",
                Font = new Font("Segoe UI", 48F),
                ForeColor = Color.FromArgb(255, 180, 100),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            
            var lockTitle = new Label
            {
                Text = "MODDING SYSTEM LOCKED",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 80, 80),
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
                Text = "📜  Review & Accept Disclaimer",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(280, 45),
                BackColor = Color.FromArgb(28, 28, 35),
                ForeColor = Color.FromArgb(0, 200, 200),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            acceptButton.FlatAppearance.BorderColor = Color.FromArgb(0, 200, 200);
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
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(28, 28, 35),
                ForeColor = Color.FromArgb(140, 140, 140),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            exitButton.FlatAppearance.BorderColor = Color.FromArgb(140, 140, 140);
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
                _detailsReqLabel.Text = "⚠ REQUIREMENTS:\n" + mod.Requirements;
            }
            else
            {
                _detailsReqLabel.Text = "";
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
                    var contextMenu = new ContextMenuStrip();
                    contextMenu.BackColor = DarkPanel;
                    contextMenu.ForeColor = TextLight;
                    var uninstallItem = new ToolStripMenuItem("🗑 Uninstall Mod");
                    uninstallItem.Click += (s, e) => UninstallMod(mod.FileName, mod.FullPath);
                    contextMenu.Items.Add(uninstallItem);
                    var openFolderItem = new ToolStripMenuItem("📁 Open Mods Folder");
                    openFolderItem.Click += (s, e) => System.Diagnostics.Process.Start("explorer.exe", _modManager.GetModsFolderPath());
                    contextMenu.Items.Add(openFolderItem);
                    modRow.ContextMenuStrip = contextMenu;
                    
                    _modsPanel.Controls.Add(modRow);

                    var checkBox = new CheckBox
                    {
                        Text = $"  {mod.FileName}",
                        Location = new Point(5, 5),
                        Checked = mod.Enabled,
                        AutoSize = true,
                        Tag = mod.FileName,
                        ForeColor = TextLight,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 10F),
                        Cursor = Cursors.Hand
                    };
                    checkBox.CheckedChanged += ModCheckBox_CheckedChanged;
                    checkBox.Click += (s, e) => UpdateModDetails(mod.FileName);
                    checkBox.ContextMenuStrip = contextMenu;
                    modRow.Controls.Add(checkBox);
                    _modCheckBoxes.Add(checkBox);

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

                    // Update indicator
                    var updateLabel = new Label
                    {
                        Text = "",
                        Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                        ForeColor = Color.Orange,
                        Location = new Point(440, 8),
                        AutoSize = true,
                        Tag = mod.FileName,
                        BackColor = Color.Transparent
                    };
                    modRow.Controls.Add(updateLabel);

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
                var updateTasks = mods.Select(async mod =>
                {
                    var manifest = _versionChecker.ReadModManifest(mod.FullPath);
                    string? updateUrl = manifest?.UpdateUrl;
                    return await _versionChecker.CheckForUpdateAsync(mod.FileName, mod.FullPath, updateUrl);
                });

                var updateResults = await Task.WhenAll(updateTasks);

                this.Invoke((MethodInvoker)delegate
                {
                    foreach (var result in updateResults)
                    {
                        var mod = mods.FirstOrDefault(m => m.FileName == result.ModName);
                        if (mod != null)
                        {
                            mod.HasUpdate = result.HasUpdate;
                            mod.LatestVersion = result.LatestVersion;

                            // Find the update label in the mod rows
                            foreach (Control panel in _modsPanel.Controls)
                            {
                                if (panel is Panel modRow && modRow.Tag?.ToString() == mod.FileName)
                                {
                                    var updateLabel = modRow.Controls.OfType<Label>()
                                        .FirstOrDefault(l => l.Tag?.ToString() == mod.FileName);
                                    
                                    if (updateLabel != null)
                                    {
                                        if (result.HasUpdate)
                                        {
                                            updateLabel.Text = $"⚠ Update: v{result.LatestVersion}";
                                            updateLabel.ForeColor = Color.Orange;
                                            updateLabel.Cursor = Cursors.Hand;
                                            updateLabel.Click += (s, e) => ShowUpdateInfo(mod, result);
                                        }
                                        else
                                        {
                                            updateLabel.Text = "✓ Latest";
                                            updateLabel.ForeColor = SuccessGreen;
                                        }
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

        private void ShowUpdateInfo(ModInfo mod, Services.ModVersionInfo versionInfo)
        {
            string message = $"Mod: {mod.FileName}\n\n" +
                           $"Current Version: {mod.Version}\n" +
                           $"Latest Version: {versionInfo.LatestVersion}\n\n" +
                           $"To update:\n" +
                           $"1. Download the latest version from the mod repository\n" +
                           $"2. Replace the DLL file in the Mods folder\n" +
                           $"3. Restart the launcher";

            if (!string.IsNullOrEmpty(versionInfo.UpdateUrl))
            {
                message += $"\n\nUpdate URL: {versionInfo.UpdateUrl}";
            }

            ShowCustomMessage(message, "Update Available", MessageBoxIcon.Information);
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ModCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string fileName)
            {
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
                    using (var updateDialog = new UpdateDialog(updateInfo, _updateChecker))
                    {
                        updateDialog.ShowDialog(this);
                    }
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
                    _statusLabel.Text = "▶ Playing...";
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
        
        private void ShowCustomMessage(string message, string title, MessageBoxIcon icon)
        {
            using (var msgForm = new Form())
            {
                msgForm.Text = title;
                msgForm.Size = new Size(420, 180);
                msgForm.FormBorderStyle = FormBorderStyle.None;
                msgForm.StartPosition = FormStartPosition.CenterParent;
                msgForm.BackColor = DarkBg;
                msgForm.ForeColor = TextLight;
                
                // Title bar
                var titleBar = new Panel
                {
                    BackColor = DarkPanel,
                    Location = new Point(0, 0),
                    Size = new Size(420, 45)
                };
                msgForm.Controls.Add(titleBar);
                
                string iconSymbol = icon switch
                {
                    MessageBoxIcon.Warning => "⚠",
                    MessageBoxIcon.Error => "✗",
                    _ => "✓"
                };
                Color iconColor = icon switch
                {
                    MessageBoxIcon.Warning => Color.Orange,
                    MessageBoxIcon.Error => Color.Red,
                    _ => SuccessGreen
                };
                
                var titleLbl = new Label
                {
                    Text = $"{iconSymbol}  {title}",
                    Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                    ForeColor = iconColor,
                    AutoSize = true,
                    Location = new Point(18, 12),
                    BackColor = Color.Transparent
                };
                titleBar.Controls.Add(titleLbl);
                
                // Message
                var msgLabel = new Label
                {
                    Text = message,
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = TextLight,
                    Location = new Point(22, 60),
                    Size = new Size(380, 65),
                    BackColor = Color.Transparent
                };
                msgForm.Controls.Add(msgLabel);
                
                // OK button
                var okBtn = new Button
                {
                    Text = "OK",
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Size = new Size(85, 35),
                    Location = new Point(315, 135),
                    BackColor = DarkPanel,
                    ForeColor = AccentCyan,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    DialogResult = DialogResult.OK
                };
                okBtn.FlatAppearance.BorderColor = AccentCyan;
                okBtn.FlatAppearance.BorderSize = 1;
                okBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 60, 60);
                msgForm.Controls.Add(okBtn);
                
                // Border
                msgForm.Paint += (s, pe) =>
                {
                    using (var pen = new Pen(Color.FromArgb(50, 50, 55), 2))
                    {
                        pe.Graphics.DrawRectangle(pen, 0, 0, msgForm.Width - 1, msgForm.Height - 1);
                    }
                };
                
                msgForm.ShowDialog(this);
            }
        }
    }
}
