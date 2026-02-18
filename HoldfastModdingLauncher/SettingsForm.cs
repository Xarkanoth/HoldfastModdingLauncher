using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HoldfastModdingLauncher.Core;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    public partial class SettingsForm : Form
    {
        private readonly HoldfastManager _holdfastManager;
        private readonly PreferencesManager _preferencesManager;
        private TextBox _holdfastPathTextBox;
        private Button _browseButton;
        private Button _saveButton;
        private Button _cancelButton;
        private CheckBox _autoUpdateCheckBox;
        private Button _checkUpdatesButton;
        
        
        // Dark theme colors (matching MainForm)
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color AccentMagenta = Color.FromArgb(200, 0, 150);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);
        private static readonly Color SuccessGreen = Color.FromArgb(80, 200, 120);
        
        // For dragging the form
        private bool _isDragging = false;
        private Point _dragOffset;

        public SettingsForm(HoldfastManager holdfastManager, PreferencesManager preferencesManager)
        {
            _holdfastManager = holdfastManager;
            _preferencesManager = preferencesManager;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            int formWidth = 550;
            int formHeight = 330;

            // Form properties - borderless dark style
            this.Text = "Settings";
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.DoubleBuffered = true;
            
            // Load custom icon if available
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Xark.ico");
            if (File.Exists(iconPath))
            {
                try { this.Icon = new Icon(iconPath); }
                catch { this.Icon = SystemIcons.Application; }
            }

            // Custom title bar panel
            var titlePanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(0, 0),
                Size = new Size(formWidth, 50),
                BorderStyle = BorderStyle.None
            };
            titlePanel.MouseDown += TitlePanel_MouseDown;
            titlePanel.MouseMove += TitlePanel_MouseMove;
            titlePanel.MouseUp += TitlePanel_MouseUp;
            this.Controls.Add(titlePanel);

            // Title label
            var titleLabel = new Label
            {
                Text = "⚙  SETTINGS",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = TextLight,
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
                Size = new Size(formWidth, formHeight - 50),
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(contentPanel);

            // Holdfast path label
            var pathLabel = new Label
            {
                Text = "HOLDFAST INSTALLATION PATH",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentMagenta,
                Location = new Point(20, 20),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(pathLabel);

            // Holdfast path textbox
            _holdfastPathTextBox = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(400, 28),
                Text = _holdfastManager.FindHoldfastInstallation() ?? "",
                Font = new Font("Segoe UI", 10F),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            contentPanel.Controls.Add(_holdfastPathTextBox);

            // Browse button
            _browseButton = new Button
            {
                Text = "Browse...",
                Font = new Font("Segoe UI", 9F),
                Location = new Point(430, 48),
                Size = new Size(90, 32),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _browseButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _browseButton.FlatAppearance.BorderSize = 1;
            _browseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 50);
            _browseButton.Click += BrowseButton_Click;
            contentPanel.Controls.Add(_browseButton);

            // Updates section label
            var updatesLabel = new Label
            {
                Text = "UPDATES",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentMagenta,
                Location = new Point(20, 95),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(updatesLabel);

            // Auto-update checkbox
            _autoUpdateCheckBox = new CheckBox
            {
                Text = "Check for updates on startup",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(20, 120),
                AutoSize = true,
                Checked = LauncherSettings.Instance.CheckForUpdatesOnStartup,
                FlatStyle = FlatStyle.Flat
            };
            contentPanel.Controls.Add(_autoUpdateCheckBox);

            // Check for updates button
            _checkUpdatesButton = new Button
            {
                Text = "Check Now",
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 150),
                Size = new Size(100, 30),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _checkUpdatesButton.FlatAppearance.BorderColor = AccentCyan;
            _checkUpdatesButton.FlatAppearance.BorderSize = 1;
            _checkUpdatesButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 50, 50);
            _checkUpdatesButton.Click += CheckUpdatesButton_Click;
            contentPanel.Controls.Add(_checkUpdatesButton);

            // Version label
            var versionLabel = new Label
            {
                Text = $"Current Version: v{new UpdateChecker().GetCurrentVersion()}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(130, 157),
                Size = new Size(200, 20),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(versionLabel);

            // Save button
            _saveButton = new Button
            {
                Text = "✓  Save",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(formWidth - 200, 210),
                Size = new Size(85, 38),
                BackColor = DarkPanel,
                ForeColor = SuccessGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            _saveButton.FlatAppearance.BorderColor = SuccessGreen;
            _saveButton.FlatAppearance.BorderSize = 1;
            _saveButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 60, 40);
            _saveButton.Click += SaveButton_Click;
            contentPanel.Controls.Add(_saveButton);

            // Cancel button
            _cancelButton = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(formWidth - 105, 210),
                Size = new Size(85, 38),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            _cancelButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _cancelButton.FlatAppearance.BorderSize = 1;
            _cancelButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 50);
            contentPanel.Controls.Add(_cancelButton);

            // Draw border around form
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(50, 50, 55), 2))
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

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select Holdfast: Nations At War installation directory";
                folderDialog.ShowNewFolderButton = false;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    _holdfastPathTextBox.Text = folderDialog.SelectedPath;
                }
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            string path = _holdfastPathTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(path))
            {
                ShowCustomMessage("Please specify a valid Holdfast installation path.", "Invalid Path", MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            if (!Directory.Exists(path))
            {
                ShowCustomMessage("The specified path does not exist.", "Invalid Path", MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            // Verify it's a valid Holdfast directory
            string exePath = Path.Combine(path, "Holdfast NaW.exe");
            if (!File.Exists(exePath))
            {
                ShowCustomMessage("The specified path does not appear to be a valid Holdfast installation.", "Invalid Path", MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            // Save settings
            LauncherSettings.Instance.CheckForUpdatesOnStartup = _autoUpdateCheckBox.Checked;
            LauncherSettings.Instance.Save();

            // Settings saved
            ShowCustomMessage("Settings saved. The launcher will use this path on next launch.", "Settings Saved", MessageBoxIcon.Information);
        }
        
        private async void CheckUpdatesButton_Click(object sender, EventArgs e)
        {
            _checkUpdatesButton.Enabled = false;
            _checkUpdatesButton.Text = "Checking...";
            
            try
            {
                using (var updateChecker = new UpdateChecker())
                {
                    var updateInfo = await updateChecker.CheckForUpdateAsync();
                    
                    if (updateInfo.UpdateAvailable)
                    {
                        using (var updateDialog = new UpdateDialog(updateInfo, updateChecker))
                        {
                            updateDialog.ShowDialog(this);
                        }
                    }
                    else
                    {
                        ShowCustomMessage($"You're up to date!\n\nCurrent version: v{updateInfo.CurrentVersion}", "No Updates", MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowCustomMessage($"Failed to check for updates:\n{ex.Message}", "Error", MessageBoxIcon.Error);
            }
            finally
            {
                _checkUpdatesButton.Enabled = true;
                _checkUpdatesButton.Text = "Check Now";
            }
        }
        
        private void ShowCustomMessage(string message, string title, MessageBoxIcon icon)
        {
            // Create a custom styled message box
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
                    Size = new Size(420, 40)
                };
                msgForm.Controls.Add(titleBar);
                
                string iconSymbol = icon == MessageBoxIcon.Warning ? "⚠" : "ℹ";
                Color iconColor = icon == MessageBoxIcon.Warning ? Color.Orange : AccentCyan;
                
                var titleLbl = new Label
                {
                    Text = $"{iconSymbol}  {title}",
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    ForeColor = iconColor,
                    AutoSize = true,
                    Location = new Point(15, 10),
                    BackColor = Color.Transparent
                };
                titleBar.Controls.Add(titleLbl);
                
                // Message
                var msgLabel = new Label
                {
                    Text = message,
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = TextLight,
                    Location = new Point(20, 55),
                    Size = new Size(380, 70),
                    BackColor = Color.Transparent
                };
                msgForm.Controls.Add(msgLabel);
                
                // OK button
                var okBtn = new Button
                {
                    Text = "OK",
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Size = new Size(80, 32),
                    Location = new Point(320, 135),
                    BackColor = DarkPanel,
                    ForeColor = AccentCyan,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    DialogResult = DialogResult.OK
                };
                okBtn.FlatAppearance.BorderColor = AccentCyan;
                okBtn.FlatAppearance.BorderSize = 1;
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
