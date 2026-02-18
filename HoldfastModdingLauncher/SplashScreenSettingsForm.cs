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
    public class SplashScreenSettingsForm : Form
    {
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentCyan = Color.FromArgb(0, 255, 255);
        private static readonly Color AccentOrange = Color.FromArgb(255, 165, 0);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);
        private static readonly Color SuccessGreen = Color.FromArgb(80, 200, 120);

        private Panel _videoListPanel;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private Button _saveButton;
        private string _selectedVideoId = "default";
        private string _configPath;
        private string _splashVideosPath;
        private string _holdfastPath;
        private List<SplashVideoOption> _videos;
        private static readonly HttpClient _httpClient;
        
        static SplashScreenSettingsForm()
        {
            // Configure HttpClient to follow redirects (GitHub uses redirects for large files)
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
        }

        public SplashScreenSettingsForm(string holdfastPath)
        {
            _holdfastPath = holdfastPath;
            
            // Config will be saved to BepInEx/config folder
            string bepInExConfigPath = Path.Combine(holdfastPath, "BepInEx", "config");
            _configPath = Path.Combine(bepInExConfigPath, "com.xarkanoth.customsplashscreen.json");
            
            // Videos are stored in BepInEx/SplashVideos folder
            _splashVideosPath = Path.Combine(holdfastPath, "BepInEx", "SplashVideos");

            InitializeVideos();
            LoadConfig();
            InitializeUI();
            CheckDownloadedVideos();
            
            // Fetch available videos from GitHub (must be after InitializeUI so _statusLabel exists)
            _ = LoadVideosFromGitHubAsync();
        }

        private void InitializeVideos()
        {
            // Start with default option (GitHub videos are loaded after UI is initialized)
            _videos = new List<SplashVideoOption>
            {
                new SplashVideoOption
                {
                    Id = "default",
                    Name = "Default (Original)",
                    Description = "The original Holdfast splash screen video.",
                    Url = "",
                    LocalFileName = ""
                }
            };
        }
        
        private async Task LoadVideosFromGitHubAsync()
        {
            try
            {
                if (_statusLabel != null)
                {
                    _statusLabel.Text = "Loading videos from GitHub...";
                    _statusLabel.Visible = true;
                }
                
                // Use raw.githubusercontent.com to avoid GitHub API rate limits (60/hr unauthenticated)
                string manifestUrl = "https://raw.githubusercontent.com/Xarkanoth/HoldfastMods/main/SplashVideos/videos.json";
                string response = await _httpClient.GetStringAsync(manifestUrl);
                
                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            string fileName = item.TryGetProperty("name", out var nameElement) 
                                ? nameElement.GetString() ?? "" 
                                : "";
                            
                            if (string.IsNullOrEmpty(fileName))
                                continue;
                            
                            string displayName = item.TryGetProperty("displayName", out var displayElement) 
                                ? displayElement.GetString() ?? Path.GetFileNameWithoutExtension(fileName)
                                : Path.GetFileNameWithoutExtension(fileName);
                            
                            string description = item.TryGetProperty("description", out var descElement) 
                                ? descElement.GetString() ?? ""
                                : $"Custom splash screen video: {displayName}";
                            
                            string downloadUrl = item.TryGetProperty("downloadUrl", out var urlElement) 
                                ? urlElement.GetString() ?? "" 
                                : "";
                            
                            string videoId = Path.GetFileNameWithoutExtension(fileName)
                                .ToLowerInvariant()
                                .Replace(" ", "_")
                                .Replace("-", "_");
                            
                            var video = new SplashVideoOption
                            {
                                Id = videoId,
                                Name = displayName,
                                Description = description,
                                Url = downloadUrl,
                                LocalFileName = fileName
                            };
                            
                            string localPath = Path.Combine(_splashVideosPath, fileName);
                            video.IsDownloaded = File.Exists(localPath);
                            
                            _videos.Add(video);
                        }
                        
                        if (!this.IsDisposed && this.IsHandleCreated)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                RefreshVideoList();
                                CheckDownloadedVideos();
                                if (_statusLabel != null)
                                    _statusLabel.Visible = false;
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Logger.LogError($"Failed to load videos from GitHub: {ex.Message}");
                
                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            RefreshVideoList();
                            if (_statusLabel != null)
                            {
                                _statusLabel.Text = $"Failed to load videos: {ex.Message}";
                                _statusLabel.ForeColor = Color.Orange;
                                _statusLabel.Visible = true;
                            }
                        });
                    }
                }
                catch { }
            }
        }

        private void CheckDownloadedVideos()
        {
            foreach (var video in _videos)
            {
                if (!string.IsNullOrEmpty(video.LocalFileName))
                {
                    string localPath = Path.Combine(_splashVideosPath, video.LocalFileName);
                    video.IsDownloaded = File.Exists(localPath);
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
                    var config = JsonSerializer.Deserialize<SplashScreenConfig>(json);
                    if (config != null)
                    {
                        _selectedVideoId = config.SelectedVideoId ?? "default";
                    }
                }
            }
            catch
            {
                _selectedVideoId = "default";
            }
        }

        private async Task<bool> SaveConfigAndDownloadAsync()
        {
            try
            {
                // Find selected video
                var selectedVideo = _videos.Find(v => v.Id == _selectedVideoId);
                
                // If it's a downloadable video and not downloaded yet, download it
                if (selectedVideo != null && 
                    !string.IsNullOrEmpty(selectedVideo.Url) && 
                    !string.IsNullOrEmpty(selectedVideo.LocalFileName) &&
                    !selectedVideo.IsDownloaded)
                {
                    _saveButton.Enabled = false;
                    _statusLabel.Text = $"Downloading {selectedVideo.Name}...";
                    _statusLabel.Visible = true;
                    _progressBar.Visible = true;
                    _progressBar.Value = 0;

                    bool downloadSuccess = await DownloadVideoAsync(selectedVideo);
                    if (!downloadSuccess)
                    {
                        _statusLabel.Text = "Download failed!";
                        _saveButton.Enabled = true;
                        return false;
                    }
                    
                    selectedVideo.IsDownloaded = true;
                }

                // Save config
                string directory = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var config = new SplashScreenConfig
                {
                    SelectedVideoId = _selectedVideoId,
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

        private async Task<bool> DownloadVideoAsync(SplashVideoOption video)
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(_splashVideosPath))
                {
                    Directory.CreateDirectory(_splashVideosPath);
                }

                string localPath = Path.Combine(_splashVideosPath, video.LocalFileName);

                using (var response = await _httpClient.GetAsync(video.Url, HttpCompletionOption.ResponseHeadersRead))
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
                                
                                // Calculate speed every 500ms
                                var timeSinceLastUpdate = (DateTime.Now - lastUpdateTime).TotalSeconds;
                                if (timeSinceLastUpdate >= 0.5)
                                {
                                    currentSpeed = (bytesRead - lastBytesRead) / timeSinceLastUpdate;
                                    lastBytesRead = bytesRead;
                                    lastUpdateTime = DateTime.Now;
                                }
                                
                                // Calculate ETA
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
                Core.Logger.LogError($"Failed to download video: {ex.Message}");
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
            this.Text = "🎬 Custom Splash Screen Settings";
            this.Size = new Size(500, 560);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;

            // Title
            var titleLabel = new Label
            {
                Text = "Select Splash Screen Video",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentCyan,
                Location = new Point(20, 15),
                AutoSize = true
            };
            this.Controls.Add(titleLabel);

            // Description
            var descLabel = new Label
            {
                Text = "Choose which video plays when Holdfast starts:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(20, 45),
                AutoSize = true
            };
            this.Controls.Add(descLabel);

            // Video list panel
            _videoListPanel = new Panel
            {
                Location = new Point(20, 75),
                Size = new Size(445, 350),
                AutoScroll = true,
                BackColor = DarkPanel
            };
            this.Controls.Add(_videoListPanel);

            // Populate video list
            int yPos = 10;
            foreach (var video in _videos)
            {
                var videoPanel = CreateVideoPanel(video, yPos);
                _videoListPanel.Controls.Add(videoPanel);
                yPos += 70;
            }

            // Progress bar (hidden by default)
            _progressBar = new ProgressBar
            {
                Location = new Point(20, 435),
                Size = new Size(445, 20),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            this.Controls.Add(_progressBar);

            // Status label (hidden by default)
            _statusLabel = new Label
            {
                Location = new Point(20, 458),
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
                Location = new Point(245, 480),
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
                Location = new Point(355, 480),
                BackColor = DarkPanel,
                ForeColor = SuccessGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _saveButton.FlatAppearance.BorderColor = SuccessGreen;
            _saveButton.Click += async (s, e) =>
            {
                bool success = await SaveConfigAndDownloadAsync();
                if (success)
                {
                    this.DialogResult = DialogResult.OK;
                }
            };
            this.Controls.Add(_saveButton);
        }

        private Panel CreateVideoPanel(SplashVideoOption video, int yPos)
        {
            bool isSelected = video.Id == _selectedVideoId;
            bool needsDownload = !string.IsNullOrEmpty(video.Url) && !video.IsDownloaded;

            var panel = new Panel
            {
                Location = new Point(5, yPos),
                Size = new Size(420, 60),
                BackColor = isSelected ? Color.FromArgb(0, 60, 60) : Color.FromArgb(35, 35, 45),
                Cursor = Cursors.Hand,
                Tag = video.Id
            };

            // Selection indicator
            var selectIndicator = new Label
            {
                Text = isSelected ? "●" : "○",
                Font = new Font("Segoe UI", 16F),
                ForeColor = isSelected ? AccentCyan : TextGray,
                Location = new Point(10, 15),
                Size = new Size(30, 30),
                Tag = video.Id
            };
            panel.Controls.Add(selectIndicator);

            // Video name with download status
            string displayName = video.Name;
            if (video.Id != "default" && video.Id != "custom")
            {
                displayName += video.IsDownloaded ? " ✓" : " ⬇";
            }
            
            var nameLabel = new Label
            {
                Text = displayName,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = isSelected ? AccentCyan : TextLight,
                Location = new Point(45, 8),
                AutoSize = true,
                Tag = video.Id
            };
            panel.Controls.Add(nameLabel);

            // Video description - add download note if needed
            string description = video.Description;
            if (needsDownload && video.Id != "custom")
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
                Tag = video.Id
            };
            panel.Controls.Add(descLabel);

            // Click handler for entire panel
            Action selectAction = () => SelectVideo(video.Id);
            panel.Click += (s, e) => selectAction();
            selectIndicator.Click += (s, e) => selectAction();
            nameLabel.Click += (s, e) => selectAction();
            descLabel.Click += (s, e) => selectAction();

            return panel;
        }

        private void SelectVideo(string videoId)
        {
            _selectedVideoId = videoId;
            RefreshVideoList();
        }

        private void RefreshVideoList()
        {
            _videoListPanel.Controls.Clear();
            int yPos = 10;
            foreach (var video in _videos)
            {
                var videoPanel = CreateVideoPanel(video, yPos);
                _videoListPanel.Controls.Add(videoPanel);
                yPos += 70;
            }
        }

        public static void ShowSettings(string holdfastPath)
        {
            using (var form = new SplashScreenSettingsForm(holdfastPath))
            {
                form.ShowDialog();
            }
        }
    }

    public class SplashVideoOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string LocalFileName { get; set; }
        public bool IsDownloaded { get; set; }
    }

    public class SplashScreenConfig
    {
        public string SelectedVideoId { get; set; } = "default";
        public bool Enabled { get; set; } = true;
    }
}

