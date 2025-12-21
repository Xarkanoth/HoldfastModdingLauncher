using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HoldfastModdingLauncher.Core;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    public class UpdateDialog : Form
    {
        private readonly UpdateInfo _updateInfo;
        private readonly UpdateChecker _updateChecker;
        
        private ProgressBar _progressBar;
        private Label _statusLabel;
        private Button _updateButton;
        private Button _laterButton;
        private Button _skipButton;
        private CheckBox _autoUpdateCheckBox;
        
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color AccentMagenta = Color.FromArgb(200, 0, 150);
        private static readonly Color AccentGreen = Color.FromArgb(80, 200, 120);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);

        // For dragging
        private bool _isDragging = false;
        private Point _dragOffset;

        public UpdateDialog(UpdateInfo updateInfo, UpdateChecker updateChecker)
        {
            _updateInfo = updateInfo;
            _updateChecker = updateChecker;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            int formWidth = 480;
            int formHeight = 320;

            // Form properties
            this.Text = "Update Available";
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.DoubleBuffered = true;
            this.TopMost = true;

            // Load icon
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Xark.ico");
            if (File.Exists(iconPath))
            {
                try { this.Icon = new Icon(iconPath); }
                catch { this.Icon = SystemIcons.Application; }
            }

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

            // Title label with icon
            var titleLabel = new Label
            {
                Text = "🚀  UPDATE AVAILABLE",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(20, 12),
                BackColor = Color.Transparent
            };
            titleLabel.MouseDown += TitlePanel_MouseDown;
            titleLabel.MouseMove += TitlePanel_MouseMove;
            titleLabel.MouseUp += TitlePanel_MouseUp;
            titlePanel.Controls.Add(titleLabel);

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
            closeButton.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            titlePanel.Controls.Add(closeButton);

            // Content panel
            var contentPanel = new Panel
            {
                BackColor = DarkBg,
                Location = new Point(0, 50),
                Size = new Size(formWidth, formHeight - 50)
            };
            this.Controls.Add(contentPanel);

            // Version info
            var versionLabel = new Label
            {
                Text = $"A new version of Holdfast Modding Launcher is available!",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(20, 15),
                AutoSize = true
            };
            contentPanel.Controls.Add(versionLabel);

            // Version details panel
            var versionPanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(20, 45),
                Size = new Size(formWidth - 40, 60)
            };
            contentPanel.Controls.Add(versionPanel);

            var currentVersionLabel = new Label
            {
                Text = $"Current version:  v{_updateInfo.CurrentVersion}",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(15, 10),
                AutoSize = true
            };
            versionPanel.Controls.Add(currentVersionLabel);

            var newVersionLabel = new Label
            {
                Text = $"New version:      v{_updateInfo.LatestVersion}",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentGreen,
                Location = new Point(15, 32),
                AutoSize = true
            };
            versionPanel.Controls.Add(newVersionLabel);

            // Auto-update checkbox
            _autoUpdateCheckBox = new CheckBox
            {
                Text = "Automatically check for updates on startup",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(20, 115),
                AutoSize = true,
                Checked = LauncherSettings.Instance.CheckForUpdatesOnStartup,
                FlatStyle = FlatStyle.Flat
            };
            _autoUpdateCheckBox.CheckedChanged += (s, e) =>
            {
                LauncherSettings.Instance.CheckForUpdatesOnStartup = _autoUpdateCheckBox.Checked;
                LauncherSettings.Instance.Save();
            };
            contentPanel.Controls.Add(_autoUpdateCheckBox);

            // Status label
            _statusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(20, 145),
                Size = new Size(formWidth - 40, 20)
            };
            contentPanel.Controls.Add(_statusLabel);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Location = new Point(20, 165),
                Size = new Size(formWidth - 40, 20),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            contentPanel.Controls.Add(_progressBar);

            // Buttons
            int buttonY = 200;
            int buttonWidth = 120;
            int buttonHeight = 38;

            // Update Now button
            _updateButton = new Button
            {
                Text = "⬇  Update Now",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, buttonY),
                Size = new Size(buttonWidth + 20, buttonHeight),
                BackColor = DarkPanel,
                ForeColor = AccentGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _updateButton.FlatAppearance.BorderColor = AccentGreen;
            _updateButton.FlatAppearance.BorderSize = 1;
            _updateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 60, 40);
            _updateButton.Click += UpdateButton_Click;
            contentPanel.Controls.Add(_updateButton);

            // Remind Me Later button
            _laterButton = new Button
            {
                Text = "Later",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(160, buttonY),
                Size = new Size(90, buttonHeight),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _laterButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _laterButton.FlatAppearance.BorderSize = 1;
            _laterButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 45);
            _laterButton.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            contentPanel.Controls.Add(_laterButton);

            // Skip This Version button
            _skipButton = new Button
            {
                Text = "Skip Version",
                Font = new Font("Segoe UI", 9F),
                Location = new Point(formWidth - 130, buttonY),
                Size = new Size(100, buttonHeight),
                BackColor = Color.Transparent,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _skipButton.FlatAppearance.BorderSize = 0;
            _skipButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 45);
            _skipButton.Click += SkipButton_Click;
            contentPanel.Controls.Add(_skipButton);

            // Border
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentCyan, 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            this.ResumeLayout(false);
        }

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

        private async void UpdateButton_Click(object sender, EventArgs e)
        {
            _updateButton.Enabled = false;
            _laterButton.Enabled = false;
            _skipButton.Enabled = false;
            _progressBar.Visible = true;
            _statusLabel.Text = "Downloading update...";
            _statusLabel.ForeColor = AccentCyan;

            try
            {
                bool success = await _updateChecker.DownloadAndInstallUpdateAsync(_updateInfo, progress =>
                {
                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                        {
                            _progressBar.Value = progress;
                            if (progress >= 90)
                                _statusLabel.Text = "Installing update...";
                            else if (progress >= 60)
                                _statusLabel.Text = "Extracting files...";
                        }));
                    }
                    else
                    {
                        _progressBar.Value = progress;
                    }
                });

                if (success)
                {
                    _statusLabel.Text = "Update installed! Restarting...";
                    _statusLabel.ForeColor = Color.FromArgb(80, 200, 120);
                    
                    // Close the application to allow update script to run
                    await System.Threading.Tasks.Task.Delay(1000);
                    Application.Exit();
                }
                else
                {
                    _statusLabel.Text = "Update failed. Please try again or download manually.";
                    _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                    _updateButton.Enabled = true;
                    _laterButton.Enabled = true;
                    _skipButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Update failed: {ex.Message}");
                _statusLabel.Text = "Update failed. Please download manually.";
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                _updateButton.Enabled = true;
                _laterButton.Enabled = true;
                _skipButton.Enabled = true;
            }
        }

        private void SkipButton_Click(object sender, EventArgs e)
        {
            LauncherSettings.Instance.LastSkippedVersion = _updateInfo.LatestVersion;
            LauncherSettings.Instance.Save();
            this.DialogResult = DialogResult.Ignore;
            this.Close();
        }
    }
}

