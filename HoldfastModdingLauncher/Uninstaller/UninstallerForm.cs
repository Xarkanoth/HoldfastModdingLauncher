using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HoldfastModdingUninstaller
{
    public partial class UninstallerForm : Form
    {
        private CheckBox _uninstallLauncherCheckBox;
        private CheckBox _uninstallModsCheckBox;
        private CheckBox _uninstallBepInExCheckBox;
        private CheckBox _removeSettingsCheckBox;
        private Button _uninstallButton;
        private Button _cancelButton;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private TextBox _logTextBox;

        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentRed = Color.FromArgb(220, 80, 80);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);

        private string _launcherPath;
        private string _holdfastPath;

        public UninstallerForm()
        {
            InitializeComponent();
            DetectPaths();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            int formWidth = 500;
            int formHeight = 550;

            // Form properties
            this.Text = "Uninstall Holdfast Modding Launcher";
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.Icon = SystemIcons.Warning;

            // Try to load custom icon
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Xark.ico");
                if (File.Exists(iconPath))
                    this.Icon = new Icon(iconPath);
            }
            catch { }

            // Title panel
            var titlePanel = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(0, 0),
                Size = new Size(formWidth, 60)
            };
            this.Controls.Add(titlePanel);

            // Warning icon and title
            var titleLabel = new Label
            {
                Text = "⚠️  UNINSTALL MODDING LAUNCHER",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentRed,
                AutoSize = true,
                Location = new Point(20, 18),
                BackColor = Color.Transparent
            };
            titlePanel.Controls.Add(titleLabel);

            // Warning message
            var warningLabel = new Label
            {
                Text = "This will remove the Holdfast Modding Launcher and selected components.\nThis action cannot be undone.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                Location = new Point(20, 75),
                Size = new Size(formWidth - 40, 45),
                BackColor = Color.Transparent
            };
            this.Controls.Add(warningLabel);

            // Options panel
            var optionsLabel = new Label
            {
                Text = "SELECT COMPONENTS TO REMOVE:",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentCyan,
                Location = new Point(20, 130),
                AutoSize = true
            };
            this.Controls.Add(optionsLabel);

            int checkY = 160;

            // Uninstall Launcher checkbox
            _uninstallLauncherCheckBox = new CheckBox
            {
                Text = "  Modding Launcher (HoldfastModdingLauncher folder)",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(20, checkY),
                AutoSize = true,
                Checked = true
            };
            this.Controls.Add(_uninstallLauncherCheckBox);
            checkY += 30;

            // Uninstall Mods checkbox
            _uninstallModsCheckBox = new CheckBox
            {
                Text = "  All Installed Mods (Mods folder)",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(20, checkY),
                AutoSize = true,
                Checked = true
            };
            this.Controls.Add(_uninstallModsCheckBox);
            checkY += 30;

            // Uninstall BepInEx checkbox
            _uninstallBepInExCheckBox = new CheckBox
            {
                Text = "  BepInEx (BepInEx folder, winhttp.dll, doorstop_config.ini)",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(20, checkY),
                AutoSize = true,
                Checked = true
            };
            this.Controls.Add(_uninstallBepInExCheckBox);
            checkY += 30;

            // Remove settings checkbox
            _removeSettingsCheckBox = new CheckBox
            {
                Text = "  User Settings & Logs (AppData folder)",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(20, checkY),
                AutoSize = true,
                Checked = false
            };
            this.Controls.Add(_removeSettingsCheckBox);
            checkY += 40;

            // Log text box
            var logLabel = new Label
            {
                Text = "UNINSTALL LOG:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextGray,
                Location = new Point(20, checkY),
                AutoSize = true
            };
            this.Controls.Add(logLabel);
            checkY += 22;

            _logTextBox = new TextBox
            {
                Location = new Point(20, checkY),
                Size = new Size(formWidth - 40, 150),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = DarkPanel,
                ForeColor = TextGray,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(_logTextBox);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Location = new Point(20, formHeight - 100),
                Size = new Size(formWidth - 40, 20),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            this.Controls.Add(_progressBar);

            // Status label
            _statusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(20, formHeight - 75),
                AutoSize = true
            };
            this.Controls.Add(_statusLabel);

            // Buttons
            _cancelButton = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 35),
                Location = new Point(formWidth - 240, formHeight - 55),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _cancelButton.FlatAppearance.BorderColor = TextGray;
            _cancelButton.Click += (s, e) => this.Close();
            this.Controls.Add(_cancelButton);

            _uninstallButton = new Button
            {
                Text = "🗑  Uninstall",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(120, 35),
                Location = new Point(formWidth - 130, formHeight - 55),
                BackColor = DarkPanel,
                ForeColor = AccentRed,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _uninstallButton.FlatAppearance.BorderColor = AccentRed;
            _uninstallButton.Click += UninstallButton_Click;
            this.Controls.Add(_uninstallButton);

            this.ResumeLayout(false);
        }

        private void DetectPaths()
        {
            // Get the launcher path (where this uninstaller is running from)
            _launcherPath = AppDomain.CurrentDomain.BaseDirectory;
            
            // Try to detect Holdfast path
            string parentDir = Directory.GetParent(_launcherPath)?.FullName ?? "";
            if (!string.IsNullOrEmpty(parentDir) && File.Exists(Path.Combine(parentDir, "Holdfast NaW.exe")))
            {
                _holdfastPath = parentDir;
            }
            else
            {
                // Try common Steam paths
                string[] commonPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam\steamapps\common\Holdfast Nations At War",
                    @"D:\SteamLibrary\steamapps\common\Holdfast Nations At War",
                    @"E:\SteamLibrary\steamapps\common\Holdfast Nations At War"
                };
                
                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Holdfast NaW.exe")))
                    {
                        _holdfastPath = path;
                        break;
                    }
                }
            }

            Log($"Launcher path: {_launcherPath}");
            if (!string.IsNullOrEmpty(_holdfastPath))
                Log($"Holdfast path: {_holdfastPath}");
            else
                Log("Warning: Could not detect Holdfast installation path");
        }

        private void Log(string message)
        {
            if (_logTextBox.InvokeRequired)
            {
                _logTextBox.Invoke(new Action(() => Log(message)));
                return;
            }
            
            _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            _logTextBox.ScrollToCaret();
        }

        private async void UninstallButton_Click(object sender, EventArgs e)
        {
            if (!_uninstallLauncherCheckBox.Checked && !_uninstallModsCheckBox.Checked && 
                !_uninstallBepInExCheckBox.Checked && !_removeSettingsCheckBox.Checked)
            {
                MessageBox.Show("Please select at least one component to uninstall.", "Nothing Selected", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "Are you sure you want to uninstall the selected components?\n\nThis action cannot be undone!",
                "Confirm Uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            _uninstallButton.Enabled = false;
            _cancelButton.Enabled = false;
            _progressBar.Visible = true;
            _progressBar.Value = 0;

            int totalSteps = 0;
            if (_uninstallModsCheckBox.Checked) totalSteps++;
            if (_uninstallBepInExCheckBox.Checked) totalSteps++;
            if (_removeSettingsCheckBox.Checked) totalSteps++;
            if (_uninstallLauncherCheckBox.Checked) totalSteps++;

            int currentStep = 0;

            try
            {
                // Step 1: Remove Mods
                if (_uninstallModsCheckBox.Checked)
                {
                    _statusLabel.Text = "Removing mods...";
                    Log("Removing mods...");
                    await Task.Run(() => RemoveMods());
                    currentStep++;
                    _progressBar.Value = (currentStep * 100) / totalSteps;
                }

                // Step 2: Remove BepInEx
                if (_uninstallBepInExCheckBox.Checked && !string.IsNullOrEmpty(_holdfastPath))
                {
                    _statusLabel.Text = "Removing BepInEx...";
                    Log("Removing BepInEx...");
                    await Task.Run(() => RemoveBepInEx());
                    currentStep++;
                    _progressBar.Value = (currentStep * 100) / totalSteps;
                }

                // Step 3: Remove Settings
                if (_removeSettingsCheckBox.Checked)
                {
                    _statusLabel.Text = "Removing settings and logs...";
                    Log("Removing settings and logs...");
                    await Task.Run(() => RemoveSettings());
                    currentStep++;
                    _progressBar.Value = (currentStep * 100) / totalSteps;
                }

                // Step 4: Remove Launcher (do this last, and schedule self-deletion)
                if (_uninstallLauncherCheckBox.Checked)
                {
                    _statusLabel.Text = "Removing launcher...";
                    Log("Removing launcher...");
                    await Task.Run(() => RemoveLauncher());
                    currentStep++;
                    _progressBar.Value = (currentStep * 100) / totalSteps;
                }

                _progressBar.Value = 100;
                _statusLabel.Text = "Uninstall complete!";
                _statusLabel.ForeColor = Color.FromArgb(80, 200, 120);
                Log("Uninstall completed successfully!");

                MessageBox.Show(
                    "Uninstallation completed successfully!\n\nThe uninstaller will now close.",
                    "Uninstall Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                // If launcher was uninstalled, schedule self-deletion and close
                if (_uninstallLauncherCheckBox.Checked)
                {
                    ScheduleSelfDeletion();
                }

                this.Close();
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                _statusLabel.Text = "Uninstall failed!";
                _statusLabel.ForeColor = AccentRed;
                
                MessageBox.Show(
                    $"An error occurred during uninstallation:\n\n{ex.Message}",
                    "Uninstall Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _uninstallButton.Enabled = true;
                _cancelButton.Enabled = true;
            }
        }

        private void RemoveMods()
        {
            string modsPath = Path.Combine(_launcherPath, "Mods");
            if (Directory.Exists(modsPath))
            {
                try
                {
                    Directory.Delete(modsPath, true);
                    Log($"  Removed: {modsPath}");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove Mods folder: {ex.Message}");
                }
            }
            else
            {
                Log("  Mods folder not found, skipping.");
            }
        }

        private void RemoveBepInEx()
        {
            if (string.IsNullOrEmpty(_holdfastPath))
            {
                Log("  Holdfast path not detected, skipping BepInEx removal.");
                return;
            }

            // Remove BepInEx folder
            string bepInExPath = Path.Combine(_holdfastPath, "BepInEx");
            if (Directory.Exists(bepInExPath))
            {
                try
                {
                    Directory.Delete(bepInExPath, true);
                    Log($"  Removed: BepInEx folder");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove BepInEx folder: {ex.Message}");
                }
            }

            // Remove winhttp.dll
            string winhttpPath = Path.Combine(_holdfastPath, "winhttp.dll");
            if (File.Exists(winhttpPath))
            {
                try
                {
                    File.Delete(winhttpPath);
                    Log($"  Removed: winhttp.dll");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove winhttp.dll: {ex.Message}");
                }
            }

            // Remove doorstop_config.ini
            string doorstopPath = Path.Combine(_holdfastPath, "doorstop_config.ini");
            if (File.Exists(doorstopPath))
            {
                try
                {
                    File.Delete(doorstopPath);
                    Log($"  Removed: doorstop_config.ini");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove doorstop_config.ini: {ex.Message}");
                }
            }

            // Remove .doorstop_version
            string doorstopVersionPath = Path.Combine(_holdfastPath, ".doorstop_version");
            if (File.Exists(doorstopVersionPath))
            {
                try
                {
                    File.Delete(doorstopVersionPath);
                    Log($"  Removed: .doorstop_version");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove .doorstop_version: {ex.Message}");
                }
            }
        }

        private void RemoveSettings()
        {
            // Remove from LocalAppData
            string localAppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HoldfastModdingLauncher");
            
            if (Directory.Exists(localAppDataPath))
            {
                try
                {
                    Directory.Delete(localAppDataPath, true);
                    Log($"  Removed: {localAppDataPath}");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove LocalAppData folder: {ex.Message}");
                }
            }

            // Remove from Roaming AppData
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoldfastModding");
            
            if (Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.Delete(appDataPath, true);
                    Log($"  Removed: {appDataPath}");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove AppData folder: {ex.Message}");
                }
            }
        }

        private void RemoveLauncher()
        {
            // Remove launcher files (except uninstaller which is running)
            string[] filesToDelete = new[]
            {
                "HoldfastModdingLauncher.exe",
                "HoldfastModdingLauncher.dll",
                "HoldfastModdingLauncher.deps.json",
                "HoldfastModdingLauncher.runtimeconfig.json",
                "HoldfastModdingLauncher.pdb",
                "ModVersions.json",
                "mods.json",
                "launcher_settings.json",
                "version.txt",
                "VERSION.txt",
                "README.txt"
            };

            foreach (var file in filesToDelete)
            {
                string filePath = Path.Combine(_launcherPath, file);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        Log($"  Removed: {file}");
                    }
                    catch (Exception ex)
                    {
                        Log($"  Warning: Could not remove {file}: {ex.Message}");
                    }
                }
            }

            // Remove Resources folder
            string resourcesPath = Path.Combine(_launcherPath, "Resources");
            if (Directory.Exists(resourcesPath))
            {
                try
                {
                    Directory.Delete(resourcesPath, true);
                    Log($"  Removed: Resources folder");
                }
                catch (Exception ex)
                {
                    Log($"  Warning: Could not remove Resources folder: {ex.Message}");
                }
            }
        }

        private void ScheduleSelfDeletion()
        {
            // Create a batch script that will delete the uninstaller after it closes
            string batchPath = Path.Combine(Path.GetTempPath(), "cleanup_holdfast_modding.bat");
            string uninstallerPath = Path.Combine(_launcherPath, "Uninstall.exe");
            
            string batchContent = $@"@echo off
:loop
del ""{uninstallerPath}"" >nul 2>&1
if exist ""{uninstallerPath}"" (
    timeout /t 1 /nobreak >nul
    goto loop
)
rmdir ""{_launcherPath}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";

            try
            {
                File.WriteAllText(batchPath, batchContent);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);
                
                Log("  Scheduled self-deletion cleanup");
            }
            catch (Exception ex)
            {
                Log($"  Warning: Could not schedule cleanup: {ex.Message}");
            }
        }
    }
}

