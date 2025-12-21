using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HoldfastModdingLauncher.Services;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher
{
    public partial class InstallerForm : Form
    {
        private CheckBox _desktopShortcutCheckBox;
        private TextBox _holdfastPathTextBox;
        private Button _browseButton;
        private Button _installButton;
        private Button _uninstallButton;
        private Label _statusLabel;
        private bool _isFirstRun;
        private string? _holdfastPath;
        private HoldfastManager _holdfastManager;
        private string? _installedLocation;

        // Dark theme colors
        private readonly Color DarkBg = Color.FromArgb(18, 18, 18);
        private readonly Color DarkPanel = Color.FromArgb(28, 28, 28);
        private readonly Color AccentCyan = Color.FromArgb(0, 255, 255);
        private readonly Color AccentMagenta = Color.FromArgb(255, 0, 255);
        private readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private readonly Color TextGray = Color.FromArgb(180, 180, 180);

        public InstallerForm(bool isFirstRun = false)
        {
            _isFirstRun = isFirstRun;
            _holdfastManager = new HoldfastManager();
            _holdfastPath = _holdfastManager.FindHoldfastInstallation();
            _installedLocation = InstallerService.FindInstalledLocation();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form properties - Dark theme
            this.Text = "Holdfast Modding Launcher - Setup";
            int formHeight = !string.IsNullOrEmpty(_installedLocation) ? 700 : 650;
            this.Size = new Size(850, formHeight);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            
            // Load custom icon
            LoadIcon();

            // Title section with gradient effect
            var titlePanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(0, 0),
                Size = new Size(850, 100),
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(titlePanel);

            // Title label - Badass styling
            var titleLabel = new Label
            {
                Text = "HOLDFAST MODDING",
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(30, 20),
                BackColor = Color.Transparent
            };
            titlePanel.Controls.Add(titleLabel);

            // Subtitle
            var subtitleLabel = new Label
            {
                Text = "Launcher Setup",
                Font = new Font("Segoe UI", 10F, FontStyle.Italic),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(35, 55),
                BackColor = Color.Transparent
            };
            titlePanel.Controls.Add(subtitleLabel);

            // Main content panel
            int contentHeight = !string.IsNullOrEmpty(_installedLocation) ? 600 : 550;
            var contentPanel = new Panel
            {
                BackColor = DarkBg,
                Location = new Point(0, 100),
                Size = new Size(850, contentHeight),
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(contentPanel);

            // Holdfast Installation Section
            var holdfastSectionLabel = new Label
            {
                Text = "HOLDFAST INSTALLATION",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(30, 30),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(holdfastSectionLabel);
            holdfastSectionLabel.SendToBack();

            // Status label
            _statusLabel = new Label
            {
                Text = !string.IsNullOrEmpty(_holdfastPath) 
                    ? "✓ Holdfast installation detected"
                    : "⚠ Holdfast installation not found",
                Font = new Font("Segoe UI", 9F),
                ForeColor = !string.IsNullOrEmpty(_holdfastPath) ? Color.FromArgb(0, 255, 127) : Color.Orange,
                AutoSize = true,
                Location = new Point(30, 60),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(_statusLabel);
            _statusLabel.SendToBack();

            // Holdfast path textbox - Dark themed
            _holdfastPathTextBox = new TextBox
            {
                Text = _holdfastPath ?? "",
                Location = new Point(30, 90),
                Size = new Size(700, 28),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                ReadOnly = false
            };
            _holdfastPathTextBox.TextChanged += HoldfastPathTextBox_TextChanged;
            contentPanel.Controls.Add(_holdfastPathTextBox);

            // Browse button - Cyan accent
            _browseButton = new Button
            {
                Text = "Browse...",
                Location = new Point(740, 88),
                Size = new Size(90, 30),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = true
            };
            _browseButton.FlatAppearance.BorderColor = AccentCyan;
            _browseButton.FlatAppearance.BorderSize = 1;
            _browseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            _browseButton.Click += BrowseButton_Click;
            contentPanel.Controls.Add(_browseButton);
            _browseButton.BringToFront();

            // Install options section
            var optionsLabel = new Label
            {
                Text = "OPTIONS",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(30, 150),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(optionsLabel);
            optionsLabel.SendToBack();

            // Desktop shortcut checkbox - use standard style for visibility
            _desktopShortcutCheckBox = new CheckBox
            {
                Text = "  Create desktop shortcut",
                Location = new Point(30, 180),
                Size = new Size(400, 30),
                ForeColor = TextLight,
                BackColor = DarkPanel,
                Checked = !InstallerService.DesktopShortcutExists(),
                Font = new Font("Segoe UI", 10F),
                FlatStyle = FlatStyle.Standard,
                Enabled = true,
                Cursor = Cursors.Hand
            };
            _desktopShortcutCheckBox.CheckedChanged += (s, ev) => 
            {
                _desktopShortcutCheckBox.Text = _desktopShortcutCheckBox.Checked 
                    ? "  ✓ Create desktop shortcut" 
                    : "  Create desktop shortcut";
            };
            // Trigger initial state
            _desktopShortcutCheckBox.Text = _desktopShortcutCheckBox.Checked 
                ? "  ✓ Create desktop shortcut" 
                : "  Create desktop shortcut";
            contentPanel.Controls.Add(_desktopShortcutCheckBox);
            _desktopShortcutCheckBox.BringToFront();

            // Info label
            var infoLabel = new Label
            {
                Text = "The launcher will be installed to: Holdfast\\HoldfastModdingLauncher",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(30, 215),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(infoLabel);
            infoLabel.SendToBack();

            // Uninstall section (only show if installed)
            if (!string.IsNullOrEmpty(_installedLocation))
            {
                var uninstallLabel = new Label
                {
                    Text = "UNINSTALL",
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    ForeColor = AccentMagenta,
                    AutoSize = true,
                    Location = new Point(30, 290),
                    BackColor = Color.Transparent
                };
                contentPanel.Controls.Add(uninstallLabel);
                uninstallLabel.SendToBack();

                var installedInfoLabel = new Label
                {
                    Text = $"Installed at: {_installedLocation}",
                    Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                    ForeColor = TextGray,
                    AutoSize = true,
                    Location = new Point(30, 315),
                    MaximumSize = new Size(750, 0),
                    BackColor = Color.Transparent
                };
                contentPanel.Controls.Add(installedInfoLabel);
                installedInfoLabel.SendToBack();

                // Uninstall button - Magenta/red accent for destructive action
                _uninstallButton = new Button
                {
                    Text = "UNINSTALL",
                    Size = new Size(160, 45),
                    Location = new Point(30, 345),
                    BackColor = DarkPanel,
                    ForeColor = AccentMagenta,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Enabled = true
                };
                _uninstallButton.FlatAppearance.BorderColor = AccentMagenta;
                _uninstallButton.FlatAppearance.BorderSize = 2;
                _uninstallButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 20, 40);
                _uninstallButton.Click += UninstallButton_Click;
                contentPanel.Controls.Add(_uninstallButton);
                _uninstallButton.BringToFront();
            }

            // Install button - Badass cyan gradient effect
            _installButton = new Button
            {
                Text = _isFirstRun ? "INSTALL" : "SAVE",
                Size = new Size(180, 50),
                Location = new Point(630, !string.IsNullOrEmpty(_installedLocation) ? 340 : 380),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = true
            };
            _installButton.FlatAppearance.BorderColor = AccentCyan;
            _installButton.FlatAppearance.BorderSize = 2;
            _installButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            _installButton.Click += InstallButton_Click;
            contentPanel.Controls.Add(_installButton);
            _installButton.BringToFront();


            // Credit label at bottom - ensure it's visible
            var creditLabel = new Label
            {
                Text = "Built and maintained by Xarkanoth - Discord.gg/csg",
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(30, contentHeight - 40),
                BackColor = Color.Transparent
            };
            contentPanel.Controls.Add(creditLabel);
            creditLabel.SendToBack();

            // Ensure all labels are sent to back and checkboxes are on top
            foreach (Control control in contentPanel.Controls)
            {
                if (control is Label)
                {
                    control.SendToBack();
                }
            }
            
            // Bring checkbox to front to ensure it's clickable
            _desktopShortcutCheckBox.BringToFront();
            
            // Ensure buttons are also on top
            _browseButton.BringToFront();
            if (_installButton != null) _installButton.BringToFront();
            if (_uninstallButton != null) _uninstallButton.BringToFront();

            this.ResumeLayout(false);
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

        private void HoldfastPathTextBox_TextChanged(object? sender, EventArgs e)
        {
            string path = _holdfastPathTextBox.Text.Trim();
            bool isValid = !string.IsNullOrEmpty(path) && _holdfastManager.IsValidHoldfastDirectory(path);
            
            _statusLabel.Text = isValid 
                ? "✓ Valid Holdfast installation"
                : string.IsNullOrEmpty(path) 
                    ? "⚠ Enter Holdfast installation path"
                    : "✗ Invalid Holdfast installation";
            _statusLabel.ForeColor = isValid ? Color.FromArgb(0, 255, 127) : Color.Orange;
            
            _holdfastPath = isValid ? path : null;
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select Holdfast: Nations At War installation folder";
                folderDialog.ShowNewFolderButton = false;
                folderDialog.UseDescriptionForTitle = true;
                
                // Set initial directory to current path in textbox
                string currentPath = _holdfastPathTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                {
                    folderDialog.InitialDirectory = currentPath;
                    folderDialog.SelectedPath = currentPath;
                }
                else if (!string.IsNullOrEmpty(_holdfastPath) && Directory.Exists(_holdfastPath))
                {
                    folderDialog.InitialDirectory = _holdfastPath;
                    folderDialog.SelectedPath = _holdfastPath;
                }

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = folderDialog.SelectedPath;
                    
                    // Validate it's a Holdfast directory
                    if (_holdfastManager.IsValidHoldfastDirectory(selectedPath))
                    {
                        _holdfastPathTextBox.Text = selectedPath;
                        _holdfastPath = selectedPath;
                    }
                    else
                    {
                        ConfirmDialog.ShowWarning(
                            "The selected folder does not appear to be a valid Holdfast installation.\n\n" +
                            "Please select the folder containing 'Holdfast NaW.exe'",
                            "Invalid Directory"
                        );
                    }
                }
            }
        }

        private void InstallButton_Click(object sender, EventArgs e)
        {
            // Validate Holdfast path
            string path = _holdfastPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path) || !_holdfastManager.IsValidHoldfastDirectory(path))
            {
                ConfirmDialog.ShowWarning(
                    "Please select a valid Holdfast installation directory.",
                    "Invalid Path"
                );
                return;
            }
            _holdfastPath = path;

            // Install to Holdfast directory
            bool installSuccess = InstallerService.InstallToHoldfastDirectory(_holdfastPath);
            if (installSuccess)
            {
                string installedExe = Path.Combine(_holdfastPath, "HoldfastModdingLauncher", "HoldfastModdingLauncher.exe");
                string installDir = Path.Combine(_holdfastPath, "HoldfastModdingLauncher");
                
                // Create desktop shortcut pointing to installed location
                if (_desktopShortcutCheckBox.Checked)
                {
                    InstallerService.CreateDesktopShortcut(installedExe);
                }
                
                // Show custom installation complete dialog
                bool launchNow = ShowInstallationCompleteDialog(installDir, installedExe);
                
                // Launch the installed version if user clicked "Launch Now"
                if (launchNow && File.Exists(installedExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = installedExe,
                            WorkingDirectory = installDir,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        ConfirmDialog.ShowError($"Failed to launch: {ex.Message}", "Launch Error");
                    }
                }
                
                // Exit this instance (we're running from wrong location)
                this.DialogResult = DialogResult.Abort; // Signal to exit without running MainForm
                this.Close();
                Application.Exit();
            }
            else
            {
                ConfirmDialog.ShowError(
                    "Failed to install launcher to Holdfast directory.\n\n" +
                    "Please check you have write permissions to the Holdfast folder.",
                    "Installation Failed"
                );
            }
        }

        private bool ShowInstallationCompleteDialog(string installPath, string installedExe)
        {
            bool launchNow = false;
            
            var dialog = new Form
            {
                Text = "Installation Complete",
                Size = new Size(500, 420),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = DarkBg,
                ShowInTaskbar = false
            };

            // Glowing border panel
            var borderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AccentCyan,
                Padding = new Padding(2)
            };
            dialog.Controls.Add(borderPanel);

            // Inner panel
            var innerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DarkBg
            };
            borderPanel.Controls.Add(innerPanel);

            // Success icon area with animated feel
            var iconPanel = new Panel
            {
                Size = new Size(80, 80),
                Location = new Point(210, 30),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(iconPanel);

            // Draw checkmark circle
            iconPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                
                // Outer glow circle
                using (var glowPen = new Pen(Color.FromArgb(80, AccentCyan), 4))
                {
                    g.DrawEllipse(glowPen, 4, 4, 72, 72);
                }
                
                // Main circle
                using (var pen = new Pen(AccentCyan, 3))
                {
                    g.DrawEllipse(pen, 8, 8, 64, 64);
                }
                
                // Checkmark
                using (var pen = new Pen(Color.FromArgb(0, 255, 127), 4))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(pen, 22, 42, 35, 55);
                    g.DrawLine(pen, 35, 55, 58, 28);
                }
            };

            // Title
            var titleLabel = new Label
            {
                Text = "INSTALLATION COMPLETE",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = false,
                Size = new Size(500, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 120),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(titleLabel);

            // Subtitle
            var subtitleLabel = new Label
            {
                Text = "Your launcher has been installed successfully!",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                AutoSize = false,
                Size = new Size(500, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 160),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(subtitleLabel);

            // Location info panel
            var locationPanel = new Panel
            {
                Size = new Size(440, 60),
                Location = new Point(30, 195),
                BackColor = DarkPanel
            };
            innerPanel.Controls.Add(locationPanel);

            var locationLabel = new Label
            {
                Text = "📁  INSTALLED TO:",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextGray,
                AutoSize = true,
                Location = new Point(15, 10),
                BackColor = Color.Transparent
            };
            locationPanel.Controls.Add(locationLabel);

            var pathLabel = new Label
            {
                Text = installPath,
                Font = new Font("Consolas", 9F),
                ForeColor = AccentCyan,
                AutoSize = false,
                Size = new Size(410, 25),
                Location = new Point(15, 30),
                BackColor = Color.Transparent
            };
            locationPanel.Controls.Add(pathLabel);

            // Launch Now Button
            var launchButton = new Button
            {
                Text = "🚀  LAUNCH NOW",
                Size = new Size(200, 50),
                Location = new Point(150, 280),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            launchButton.FlatAppearance.BorderColor = AccentCyan;
            launchButton.FlatAppearance.BorderSize = 2;
            launchButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 60, 60);
            launchButton.MouseEnter += (s, ev) => launchButton.BackColor = Color.FromArgb(30, 50, 50);
            launchButton.MouseLeave += (s, ev) => launchButton.BackColor = DarkPanel;
            launchButton.Click += (s, ev) => { launchNow = true; dialog.Close(); };
            innerPanel.Controls.Add(launchButton);
            launchButton.BringToFront();

            // Close Button
            var closeButton = new Button
            {
                Text = "CLOSE",
                Size = new Size(200, 40),
                Location = new Point(150, 340),
                BackColor = Color.Transparent,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            closeButton.FlatAppearance.BorderSize = 1;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            closeButton.Click += (s, ev) => { launchNow = false; dialog.Close(); };
            innerPanel.Controls.Add(closeButton);
            closeButton.BringToFront();

            // Credit at bottom
            var creditLabel = new Label
            {
                Text = "Built by Xarkanoth • Discord.gg/csg",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = false,
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 390),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(creditLabel);

            dialog.ShowDialog(this);
            return launchNow;
        }

        private void ShowSimpleSuccessDialog(string title, string message)
        {
            var dialog = new Form
            {
                Text = title,
                Size = new Size(400, 200),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = DarkBg,
                ShowInTaskbar = false
            };

            // Glowing border
            var borderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AccentCyan,
                Padding = new Padding(2)
            };
            dialog.Controls.Add(borderPanel);

            var innerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DarkBg
            };
            borderPanel.Controls.Add(innerPanel);

            // Checkmark
            var checkLabel = new Label
            {
                Text = "✓",
                Font = new Font("Segoe UI", 32F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 255, 127),
                AutoSize = false,
                Size = new Size(400, 50),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 20),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(checkLabel);

            var msgLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 11F),
                ForeColor = TextLight,
                AutoSize = false,
                Size = new Size(380, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 75),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(msgLabel);

            var okBtn = new Button
            {
                Text = "OK",
                Size = new Size(100, 40),
                Location = new Point(150, 130),
                BackColor = DarkPanel,
                ForeColor = AccentCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            okBtn.FlatAppearance.BorderColor = AccentCyan;
            okBtn.FlatAppearance.BorderSize = 2;
            okBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 60, 60);
            innerPanel.Controls.Add(okBtn);

            dialog.AcceptButton = okBtn;
            dialog.ShowDialog(this);
        }

        private void ShowUninstallCompleteDialog(string details)
        {
            var dialog = new Form
            {
                Text = "Uninstall Complete",
                Size = new Size(450, 300),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = DarkBg,
                ShowInTaskbar = false
            };

            // Glowing border - magenta for uninstall
            var borderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AccentMagenta,
                Padding = new Padding(2)
            };
            dialog.Controls.Add(borderPanel);

            var innerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DarkBg
            };
            borderPanel.Controls.Add(innerPanel);

            // Icon
            var iconLabel = new Label
            {
                Text = "🗑️",
                Font = new Font("Segoe UI Emoji", 36F),
                AutoSize = false,
                Size = new Size(450, 60),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 20),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(iconLabel);

            // Title
            var titleLabel = new Label
            {
                Text = "UNINSTALL COMPLETE",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = AccentMagenta,
                AutoSize = false,
                Size = new Size(450, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 85),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(titleLabel);

            // Details
            var detailsLabel = new Label
            {
                Text = details.Replace("\n", "\n"),
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextLight,
                AutoSize = false,
                Size = new Size(400, 80),
                TextAlign = ContentAlignment.TopCenter,
                Location = new Point(25, 125),
                BackColor = Color.Transparent
            };
            innerPanel.Controls.Add(detailsLabel);

            // OK Button
            var okBtn = new Button
            {
                Text = "CLOSE",
                Size = new Size(140, 45),
                Location = new Point(155, 220),
                BackColor = DarkPanel,
                ForeColor = AccentMagenta,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            okBtn.FlatAppearance.BorderColor = AccentMagenta;
            okBtn.FlatAppearance.BorderSize = 2;
            okBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 30, 50);
            innerPanel.Controls.Add(okBtn);

            dialog.AcceptButton = okBtn;
            dialog.ShowDialog(this);
        }

        private void UninstallButton_Click(object? sender, EventArgs e)
        {
            // Confirm uninstall
            bool confirmed = ConfirmDialog.ShowConfirm(
                "Are you sure you want to uninstall the launcher?\n\n" +
                "This will:\n" +
                "• Remove the launcher from Holdfast directory\n" +
                "• Remove desktop shortcut\n" +
                "• Keep your mods and settings (optional)\n\n" +
                "Continue?",
                "Confirm Uninstall",
                ConfirmDialogIcon.Warning
            );

            if (!confirmed)
            {
                return;
            }

            // Ask about logs
            bool removeLogs = ConfirmDialog.ShowConfirm(
                "Do you also want to remove launcher logs and configuration?\n\n" +
                "This will delete all log files and settings.",
                "Remove Logs?",
                ConfirmDialogIcon.Question
            );

            // Perform uninstall
            var uninstallResult = InstallerService.Uninstall(removeLogs);

            // Show results
            string message = "Uninstall ";
            if (uninstallResult.LauncherRemoved && uninstallResult.ShortcutRemoved)
            {
                message += "completed successfully!\n\n";
                if (uninstallResult.LogsRemoved)
                {
                    message += "✓ Launcher removed\n✓ Desktop shortcut removed\n✓ Logs removed";
                }
                else
                {
                    message += "✓ Launcher removed\n✓ Desktop shortcut removed";
                }
                
                ShowUninstallCompleteDialog(message);

                // Close the form after successful uninstall
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                message += "completed with warnings:\n\n";
                if (!uninstallResult.LauncherRemoved)
                {
                    message += "⚠ Could not remove launcher files\n";
                }
                if (!uninstallResult.ShortcutRemoved)
                {
                    message += "⚠ Could not remove desktop shortcut\n";
                }
                if (!string.IsNullOrEmpty(uninstallResult.Message))
                {
                    message += $"\n{uninstallResult.Message}";
                }

                ConfirmDialog.ShowWarning(message, "Uninstall Warning");
            }
        }
    }
}
