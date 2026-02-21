using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    public class SplashScreenSettingsForm : Form
    {
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
        private readonly ApiClient _apiClient;

        public SplashScreenSettingsForm(string holdfastPath, ApiClient apiClient)
        {
            _holdfastPath = holdfastPath;
            _apiClient = apiClient;

            string bepInExConfigPath = Path.Combine(holdfastPath, "BepInEx", "config");
            _configPath = Path.Combine(bepInExConfigPath, "com.xarkanoth.customsplashscreen.json");
            _splashVideosPath = Path.Combine(holdfastPath, "BepInEx", "SplashVideos");

            InitializeVideos();
            LoadConfig();
            InitializeUI();
            CheckDownloadedVideos();

            _ = LoadVideosFromServerAsync();
        }

        private void InitializeVideos()
        {
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

        private async Task LoadVideosFromServerAsync()
        {
            try
            {
                if (_statusLabel != null)
                {
                    _statusLabel.Text = "Loading videos from server...";
                    _statusLabel.Visible = true;
                }

                if (_apiClient == null || !_apiClient.IsAuthenticated)
                {
                    throw new Exception("Not authenticated. Please log in first.");
                }

                var serverVideos = await _apiClient.GetSplashVideosAsync();
                if (serverVideos == null || serverVideos.Count == 0)
                {
                    Core.Logger.LogInfo("Server returned no splash videos");
                    UpdateUIAfterLoad(true);
                    return;
                }

                foreach (var sv in serverVideos)
                {
                    string videoId = string.IsNullOrEmpty(sv.Id)
                        ? Path.GetFileNameWithoutExtension(sv.FileName)
                            .ToLowerInvariant().Replace(" ", "_").Replace("-", "_")
                        : sv.Id;

                    var video = new SplashVideoOption
                    {
                        Id = videoId,
                        Name = sv.Name,
                        Description = sv.Description,
                        Url = videoId,
                        LocalFileName = sv.FileName
                    };

                    string localPath = Path.Combine(_splashVideosPath, sv.FileName);
                    video.IsDownloaded = File.Exists(localPath);

                    _videos.Add(video);
                }

                Core.Logger.LogInfo($"Loaded {serverVideos.Count} splash video(s) from server");
                UpdateUIAfterLoad(true);
            }
            catch (Exception ex)
            {
                Core.Logger.LogError($"Failed to load videos from server: {ex.Message}");
                UpdateUIAfterLoad(false, ex.Message);
            }
        }

        private void UpdateUIAfterLoad(bool success, string errorMessage = null)
        {
            try
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;

                this.Invoke((MethodInvoker)delegate
                {
                    RefreshVideoList();
                    CheckDownloadedVideos();
                    if (_statusLabel != null)
                    {
                        if (success)
                        {
                            _statusLabel.Visible = false;
                        }
                        else
                        {
                            _statusLabel.Text = $"Failed to load videos: {errorMessage}";
                            _statusLabel.ForeColor = Color.Orange;
                            _statusLabel.Visible = true;
                        }
                    }
                });
            }
            catch { }
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
                var selectedVideo = _videos.Find(v => v.Id == _selectedVideoId);

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

                string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, configJson);

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
                if (!Directory.Exists(_splashVideosPath))
                {
                    Directory.CreateDirectory(_splashVideosPath);
                }

                string localPath = Path.Combine(_splashVideosPath, video.LocalFileName);

                // video.Url holds the server video ID for the API endpoint
                using (var response = await _apiClient.DownloadSplashVideoAsync(video.Url))
                {
                    if (response == null || !response.IsSuccessStatusCode)
                    {
                        string status = response?.StatusCode.ToString() ?? "No response";
                        Core.Logger.LogError($"Server returned {status} for splash video download");
                        return false;
                    }

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
                Core.Logger.LogError($"Failed to download video from server: {ex.Message}");
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

            var titleLabel = new Label
            {
                Text = "Select Splash Screen Video",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentCyan,
                Location = new Point(20, 15),
                AutoSize = true
            };
            this.Controls.Add(titleLabel);

            var descLabel = new Label
            {
                Text = "Choose which video plays when Holdfast starts:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(20, 45),
                AutoSize = true
            };
            this.Controls.Add(descLabel);

            _videoListPanel = new Panel
            {
                Location = new Point(20, 75),
                Size = new Size(445, 350),
                AutoScroll = true,
                BackColor = DarkPanel
            };
            this.Controls.Add(_videoListPanel);

            int yPos = 10;
            foreach (var video in _videos)
            {
                var videoPanel = CreateVideoPanel(video, yPos);
                _videoListPanel.Controls.Add(videoPanel);
                yPos += 70;
            }

            _progressBar = new ProgressBar
            {
                Location = new Point(20, 435),
                Size = new Size(445, 20),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            this.Controls.Add(_progressBar);

            _statusLabel = new Label
            {
                Location = new Point(20, 458),
                Size = new Size(445, 20),
                Font = new Font("Segoe UI", 9F),
                ForeColor = AccentCyan,
                Visible = false
            };
            this.Controls.Add(_statusLabel);

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

        public static void ShowSettings(string holdfastPath, ApiClient apiClient)
        {
            using (var form = new SplashScreenSettingsForm(holdfastPath, apiClient))
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
