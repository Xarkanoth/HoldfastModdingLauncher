using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    public class CrosshairSettingsForm : Form
    {
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentCyan = Color.FromArgb(0, 255, 255);
        private static readonly Color AccentOrange = Color.FromArgb(255, 165, 0);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);
        private static readonly Color SuccessGreen = Color.FromArgb(80, 200, 120);

        private Panel _crosshairListPanel;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private Button _saveButton;
        private CheckBox _rangefinderCheckBox;
        private string _selectedCrosshairId = "default";
        private bool _rangefinderEnabled = false;
        private string _configPath;
        private string _crosshairsPath;
        private string _holdfastPath;
        private List<CrosshairOption> _crosshairs;
        private static readonly HttpClient _httpClient;
        private readonly PreferencesManager _preferencesManager;
        
        static CrosshairSettingsForm()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
        }

        public CrosshairSettingsForm(string holdfastPath, PreferencesManager preferencesManager)
        {
            _holdfastPath = holdfastPath;
            _preferencesManager = preferencesManager;
            
            // Config will be saved to BepInEx/config folder
            string bepInExConfigPath = Path.Combine(holdfastPath, "BepInEx", "config");
            _configPath = Path.Combine(bepInExConfigPath, "com.xarkanoth.customcrosshairs.json");
            
            // Crosshairs are stored in BepInEx/CustomCrosshairs folder
            _crosshairsPath = Path.Combine(holdfastPath, "BepInEx", "CustomCrosshairs");

            InitializeCrosshairs();
            LoadConfig();
            InitializeUI();
            CheckDownloadedCrosshairs();
        }

        private void InitializeCrosshairs()
        {
            _crosshairs = new List<CrosshairOption>
            {
                new CrosshairOption
                {
                    Id = "default",
                    Name = "Default (Original)",
                    Description = "The original Holdfast crosshairs.",
                    WeaponType = "all",
                    Url = "",
                    LocalFileName = ""
                }
                // More crosshairs will be added here as they're created in GitHub
            };
        }

        private void CheckDownloadedCrosshairs()
        {
            foreach (var crosshair in _crosshairs)
            {
                if (!string.IsNullOrEmpty(crosshair.LocalFileName))
                {
                    string localPath = Path.Combine(_crosshairsPath, crosshair.LocalFileName);
                    crosshair.IsDownloaded = File.Exists(localPath);
                }
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<CrosshairConfig>(json);
                    if (config != null)
                    {
                        _selectedCrosshairId = config.SelectedCrosshairId ?? "default";
                        _rangefinderEnabled = config.RangefinderEnabled;
                    }
                }
            }
            catch
            {
                _selectedCrosshairId = "default";
                _rangefinderEnabled = false;
            }
        }

        private async Task<bool> SaveConfigAndDownloadAsync()
        {
            try
            {
                // Find selected crosshair
                var selectedCrosshair = _crosshairs.Find(c => c.Id == _selectedCrosshairId);
                
                // If it's a downloadable crosshair and not downloaded yet, download it
                if (selectedCrosshair != null && 
                    !string.IsNullOrEmpty(selectedCrosshair.Url) && 
                    !string.IsNullOrEmpty(selectedCrosshair.LocalFileName) &&
                    !selectedCrosshair.IsDownloaded)
                {
                    _saveButton.Enabled = false;
                    _statusLabel.Text = $"Downloading {selectedCrosshair.Name}...";
                    _statusLabel.Visible = true;
                    _progressBar.Visible = true;
                    _progressBar.Value = 0;

                    bool downloadSuccess = await DownloadCrosshairAsync(selectedCrosshair);
                    if (!downloadSuccess)
                    {
                        _statusLabel.Text = "Download failed!";
                        _saveButton.Enabled = true;
                        return false;
                    }
                    
                    selectedCrosshair.IsDownloaded = true;
                }

                // Save config
                string directory = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var config = new CrosshairConfig
                {
                    SelectedCrosshairId = _selectedCrosshairId,
                    RangefinderEnabled = _rangefinderEnabled,
                    Enabled = true
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
                
                _statusLabel.Text = "Settings saved!";
                return true;
            }
            catch (Exception ex)
            {
                ConfirmDialog.ShowError($"Failed to save settings: {ex.Message}", "Error");
                return false;
            }
        }

        private async Task<bool> DownloadCrosshairAsync(CrosshairOption crosshair)
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(_crosshairsPath))
                {
                    Directory.CreateDirectory(_crosshairsPath);
                }

                string localPath = Path.Combine(_crosshairsPath, crosshair.LocalFileName);

                using (var response = await _httpClient.GetAsync(crosshair.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long bytesRead = 0;
                    var startTime = DateTime.Now;
                    var lastUpdateTime = DateTime.Now;
                    long lastBytesRead = 0;
                    double currentSpeed = 0;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        int read;
                        
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            bytesRead += read;
                            
                            if (totalBytes > 0)
                            {
                                int percentage = (int)((bytesRead * 100) / totalBytes);
                                
                                var timeSinceLastUpdate = (DateTime.Now - lastUpdateTime).TotalSeconds;
                                if (timeSinceLastUpdate >= 0.5)
                                {
                                    currentSpeed = (bytesRead - lastBytesRead) / timeSinceLastUpdate;
                                    lastBytesRead = bytesRead;
                                    lastUpdateTime = DateTime.Now;
                                }
                                
                                string etaText = "";
                                if (currentSpeed > 0)
                                {
                                    double remainingSeconds = (totalBytes - bytesRead) / currentSpeed;
                                    if (remainingSeconds < 60)
                                        etaText = $"~{(int)remainingSeconds}s";
                                    else
                                        etaText = $"~{(int)(remainingSeconds / 60)}m {(int)(remainingSeconds % 60)}s";
                                }
                                
                                this.Invoke((MethodInvoker)delegate
                                {
                                    _progressBar.Value = percentage;
                                    string speedText = currentSpeed > 0 ? $" @ {FormatFileSize((long)currentSpeed)}/s" : "";
                                    _statusLabel.Text = $"Downloading: {FormatFileSize(bytesRead)} / {FormatFileSize(totalBytes)}{speedText} {etaText}";
                                });
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Core.Logger.LogError($"Failed to download crosshair: {ex.Message}");
                return false;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void InitializeUI()
        {
            this.Text = "🎯 Custom Crosshairs Settings";
            this.Size = new Size(500, 600);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;

            // Title
            var titleLabel = new Label
            {
                Text = "Select Crosshair",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentCyan,
                Location = new Point(20, 15),
                AutoSize = true
            };
            this.Controls.Add(titleLabel);

            // Description
            var descLabel = new Label
            {
                Text = "Choose which crosshair to use:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(20, 45),
                AutoSize = true
            };
            this.Controls.Add(descLabel);

            // Crosshair list panel
            _crosshairListPanel = new Panel
            {
                Location = new Point(20, 75),
                Size = new Size(445, 300),
                AutoScroll = true,
                BackColor = DarkPanel
            };
            this.Controls.Add(_crosshairListPanel);

            // Populate crosshair list
            int yPos = 10;
            foreach (var crosshair in _crosshairs)
            {
                var crosshairPanel = CreateCrosshairPanel(crosshair, yPos);
                _crosshairListPanel.Controls.Add(crosshairPanel);
                yPos += 70;
            }

            // Rangefinder checkbox (master login only)
            bool isMasterLoggedIn = _preferencesManager.IsMasterLoggedIn();
            if (isMasterLoggedIn)
            {
                _rangefinderCheckBox = new CheckBox
                {
                    Text = "Enable Rangefinder",
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = TextLight,
                    Location = new Point(20, 385),
                    Size = new Size(200, 25),
                    Checked = _rangefinderEnabled,
                    BackColor = DarkBg
                };
                this.Controls.Add(_rangefinderCheckBox);
            }

            // Progress bar (hidden by default)
            _progressBar = new ProgressBar
            {
                Location = new Point(20, 420),
                Size = new Size(445, 20),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            this.Controls.Add(_progressBar);

            // Status label (hidden by default)
            _statusLabel = new Label
            {
                Location = new Point(20, 443),
                Size = new Size(445, 20),
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccentCyan,
                Visible = false
            };
            this.Controls.Add(_statusLabel);

            // Buttons
            var cancelButton = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 35),
                Location = new Point(245, 470),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderColor = TextGray;
            cancelButton.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            this.Controls.Add(cancelButton);

            _saveButton = new Button
            {
                Text = "💾 Save",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(100, 35),
                Location = new Point(355, 470),
                BackColor = DarkPanel,
                ForeColor = SuccessGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _saveButton.FlatAppearance.BorderColor = SuccessGreen;
            _saveButton.Click += async (s, e) =>
            {
                if (_rangefinderCheckBox != null)
                {
                    _rangefinderEnabled = _rangefinderCheckBox.Checked;
                }
                bool success = await SaveConfigAndDownloadAsync();
                if (success)
                {
                    this.DialogResult = DialogResult.OK;
                }
            };
            this.Controls.Add(_saveButton);
        }

        private Panel CreateCrosshairPanel(CrosshairOption crosshair, int yPos)
        {
            bool isSelected = crosshair.Id == _selectedCrosshairId;
            bool needsDownload = !string.IsNullOrEmpty(crosshair.Url) && !crosshair.IsDownloaded;

            var panel = new Panel
            {
                Location = new Point(5, yPos),
                Size = new Size(420, 60),
                BackColor = isSelected ? Color.FromArgb(0, 60, 60) : Color.FromArgb(35, 35, 45),
                Cursor = Cursors.Hand,
                Tag = crosshair.Id
            };

            // Selection indicator
            var selectIndicator = new Label
            {
                Text = isSelected ? "●" : "○",
                Font = new Font("Segoe UI", 16F),
                ForeColor = isSelected ? AccentCyan : TextGray,
                Location = new Point(10, 15),
                Size = new Size(30, 30),
                Tag = crosshair.Id
            };
            panel.Controls.Add(selectIndicator);

            // Crosshair name with download status
            string displayName = crosshair.Name;
            if (crosshair.Id != "default")
            {
                displayName += crosshair.IsDownloaded ? " ✓" : " ⬇";
            }
            
            var nameLabel = new Label
            {
                Text = displayName,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = isSelected ? AccentCyan : TextLight,
                Location = new Point(45, 8),
                AutoSize = true,
                Tag = crosshair.Id
            };
            panel.Controls.Add(nameLabel);

            // Crosshair description
            string description = crosshair.Description;
            if (needsDownload)
            {
                description += " (will download on save)";
            }
            
            var descLabel = new Label
            {
                Text = description,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(45, 32),
                Size = new Size(360, 20),
                Tag = crosshair.Id
            };
            panel.Controls.Add(descLabel);

            // Click handler for entire panel
            Action selectAction = () => SelectCrosshair(crosshair.Id);
            panel.Click += (s, e) => selectAction();
            selectIndicator.Click += (s, e) => selectAction();
            nameLabel.Click += (s, e) => selectAction();
            descLabel.Click += (s, e) => selectAction();

            return panel;
        }

        private void SelectCrosshair(string crosshairId)
        {
            _selectedCrosshairId = crosshairId;
            RefreshCrosshairList();
        }

        private void RefreshCrosshairList()
        {
            _crosshairListPanel.Controls.Clear();
            int yPos = 10;
            foreach (var crosshair in _crosshairs)
            {
                var crosshairPanel = CreateCrosshairPanel(crosshair, yPos);
                _crosshairListPanel.Controls.Add(crosshairPanel);
                yPos += 70;
            }
        }

        public static void ShowSettings(string holdfastPath, PreferencesManager preferencesManager)
        {
            using (var form = new CrosshairSettingsForm(holdfastPath, preferencesManager))
            {
                form.ShowDialog();
            }
        }
    }

    public class CrosshairOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string WeaponType { get; set; } // "all", "musket", "pistol", etc.
        public string Url { get; set; }
        public string LocalFileName { get; set; }
        public bool IsDownloaded { get; set; }
    }

    public class CrosshairConfig
    {
        public string SelectedCrosshairId { get; set; } = "default";
        public bool RangefinderEnabled { get; set; } = false;
        public bool Enabled { get; set; } = true;
    }
}
