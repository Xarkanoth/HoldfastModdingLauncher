using System;
using System.Drawing;
using System.Windows.Forms;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    public class UpdateDialog : Form
    {
        private static readonly Color DarkBg = Theme.PageBg;
        private static readonly Color DarkPanel = Theme.Panel;
        private static readonly Color AccentCyan = Theme.Brand;
        private static readonly Color TextLight = Theme.Text;
        private static readonly Color TextGray = Theme.TextMuted;
        private static readonly Color SuccessGreen = Theme.Success;

        private readonly UpdateInfo _updateInfo;
        private readonly UpdateChecker _updateChecker;

        private ProgressBar _progressBar;
        private Label _statusLabel;
        private Button _updateButton;
        private Button _skipButton;
        private Button _laterButton;

        public UpdateDialog(UpdateInfo updateInfo, UpdateChecker updateChecker)
        {
            _updateInfo = updateInfo;
            _updateChecker = updateChecker;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "Update Available";
            Size = new Size(480, 340);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = DarkBg;
            ForeColor = TextLight;

            var titleBar = new Panel
            {
                BackColor = DarkPanel,
                Dock = DockStyle.Top,
                Height = 42
            };
            Controls.Add(titleBar);

            var titleLabel = new Label
            {
                Text = "⬆  Update Available",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(15, 10),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(titleLabel);

            var versionLabel = new Label
            {
                Text = $"v{_updateInfo.CurrentVersion}  →  v{_updateInfo.LatestVersion}",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = SuccessGreen,
                AutoSize = true,
                Location = new Point(20, 58),
                BackColor = Color.Transparent
            };
            Controls.Add(versionLabel);

            var notesBox = new RichTextBox
            {
                ReadOnly = true,
                BackColor = DarkPanel,
                ForeColor = TextGray,
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.None,
                Location = new Point(20, 95),
                Size = new Size(440, 100),
                Text = string.IsNullOrEmpty(_updateInfo.ReleaseNotes)
                    ? "A new version is available with bug fixes and improvements."
                    : _updateInfo.ReleaseNotes,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            Controls.Add(notesBox);

            _progressBar = new ProgressBar
            {
                Location = new Point(20, 205),
                Size = new Size(440, 18),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            Controls.Add(_progressBar);

            _statusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(20, 226),
                Size = new Size(440, 20),
                BackColor = Color.Transparent,
                Visible = false
            };
            Controls.Add(_statusLabel);

            _updateButton = new Button
            {
                Text = "Update Now",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(130, 36),
                Location = new Point(20, 255),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _updateButton.FlatAppearance.BorderColor = AccentCyan;
            _updateButton.FlatAppearance.BorderSize = 1;
            _updateButton.Click += UpdateButton_Click;
            Controls.Add(_updateButton);

            _skipButton = new Button
            {
                Text = "Skip Version",
                Font = new Font("Segoe UI", 9F),
                Size = new Size(110, 36),
                Location = new Point(240, 255),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _skipButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _skipButton.FlatAppearance.BorderSize = 1;
            _skipButton.Click += SkipButton_Click;
            Controls.Add(_skipButton);

            _laterButton = new Button
            {
                Text = "Later",
                Font = new Font("Segoe UI", 9F),
                Size = new Size(80, 36),
                Location = new Point(360, 255),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _laterButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _laterButton.FlatAppearance.BorderSize = 1;
            _laterButton.Click += (s, e) => Close();
            Controls.Add(_laterButton);

            Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(50, 50, 55), 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            };

            ResumeLayout(false);
        }

        private async void UpdateButton_Click(object sender, EventArgs e)
        {
            _updateButton.Enabled = false;
            _skipButton.Enabled = false;
            _laterButton.Enabled = false;
            _progressBar.Visible = true;
            _statusLabel.Visible = true;
            _statusLabel.Text = "Downloading update...";

            try
            {
                bool success = await _updateChecker.DownloadAndInstallUpdateAsync(_updateInfo, progress =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => UpdateProgress(progress)));
                    else
                        UpdateProgress(progress);
                });

                if (success)
                {
                    _statusLabel.Text = "Update downloaded! Restarting...";
                    _statusLabel.ForeColor = SuccessGreen;
                    Application.Exit();
                }
                else
                {
                    _statusLabel.Text = "Update failed. Try again or download manually.";
                    _statusLabel.ForeColor = Color.OrangeRed;
                    _updateButton.Enabled = true;
                    _skipButton.Enabled = true;
                    _laterButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error: {ex.Message}";
                _statusLabel.ForeColor = Color.OrangeRed;
                _updateButton.Enabled = true;
                _skipButton.Enabled = true;
                _laterButton.Enabled = true;
            }
        }

        private void UpdateProgress(int progress)
        {
            _progressBar.Value = Math.Min(progress, 100);

            if (progress < 60)
                _statusLabel.Text = $"Downloading... {progress}%";
            else if (progress < 80)
                _statusLabel.Text = "Extracting update...";
            else if (progress < 100)
                _statusLabel.Text = "Preparing to install...";
            else
                _statusLabel.Text = "Restarting launcher...";
        }

        private void SkipButton_Click(object sender, EventArgs e)
        {
            Core.LauncherSettings.Instance.LastSkippedVersion = _updateInfo.LatestVersion;
            Core.LauncherSettings.Instance.Save();
            Close();
        }
    }
}
