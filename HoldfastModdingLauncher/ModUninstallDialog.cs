using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher
{
    public class ModUninstallDialog : Form
    {
        private bool _confirmed = false;
        private bool _deleteSuccessful = false;
        private string _modFileName;
        private string _modFullPath;
        
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentRed = Color.FromArgb(220, 80, 80);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);

        public bool Confirmed => _confirmed;
        public bool DeleteSuccessful => _deleteSuccessful;

        public ModUninstallDialog(string modFileName, string modFullPath)
        {
            _modFileName = modFileName;
            _modFullPath = modFullPath;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            int formWidth = 450;
            int formHeight = 280;

            // Form properties
            this.Text = "Uninstall Mod";
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            // Add border effect
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentRed, 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, this.Width - 3, this.Height - 3);
                }
            };

            // Title bar panel
            var titleBar = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(2, 2),
                Size = new Size(formWidth - 4, 45)
            };
            this.Controls.Add(titleBar);

            // Icon
            var iconLabel = new Label
            {
                Text = "🗑️",
                Font = new Font("Segoe UI", 16F),
                ForeColor = AccentRed,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(iconLabel);

            // Title
            var titleLabel = new Label
            {
                Text = "UNINSTALL MOD",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = AccentRed,
                AutoSize = true,
                Location = new Point(50, 12),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(titleLabel);

            // Mod name display
            var modNamePanel = new Panel
            {
                BackColor = Color.FromArgb(35, 25, 25),
                Location = new Point(20, 60),
                Size = new Size(formWidth - 40, 50)
            };
            modNamePanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentRed, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, modNamePanel.Width - 1, modNamePanel.Height - 1);
                }
            };
            this.Controls.Add(modNamePanel);

            var modNameLabel = new Label
            {
                Text = _modFileName,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextLight,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(5, 5),
                Size = new Size(modNamePanel.Width - 10, 40),
                BackColor = Color.Transparent
            };
            modNamePanel.Controls.Add(modNameLabel);

            // Warning message
            var warningLabel = new Label
            {
                Text = "Are you sure you want to uninstall this mod?\n\nThis will permanently delete the mod file from your Mods folder.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextGray,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 120),
                Size = new Size(formWidth - 40, 60),
                BackColor = Color.Transparent
            };
            this.Controls.Add(warningLabel);

            // Info label
            var infoLabel = new Label
            {
                Text = "💡 Tip: Close Holdfast before uninstalling to avoid file lock issues.",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.FromArgb(100, 180, 180),
                AutoSize = true,
                Location = new Point(20, 185),
                BackColor = Color.Transparent
            };
            this.Controls.Add(infoLabel);

            // Buttons panel
            var buttonPanel = new Panel
            {
                BackColor = Color.Transparent,
                Location = new Point(0, formHeight - 60),
                Size = new Size(formWidth, 58)
            };
            this.Controls.Add(buttonPanel);

            // Cancel button
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 38),
                Location = new Point((formWidth / 2) - 115, 10),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            cancelBtn.FlatAppearance.BorderColor = TextGray;
            cancelBtn.Click += (s, e) =>
            {
                _confirmed = false;
                this.Close();
            };
            buttonPanel.Controls.Add(cancelBtn);

            // Uninstall button
            var uninstallBtn = new Button
            {
                Text = "🗑 Uninstall",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(120, 38),
                Location = new Point((formWidth / 2) + 5, 10),
                BackColor = DarkPanel,
                ForeColor = AccentRed,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            uninstallBtn.FlatAppearance.BorderColor = AccentRed;
            uninstallBtn.Click += (s, e) =>
            {
                _confirmed = true;
                PerformUninstall();
            };
            buttonPanel.Controls.Add(uninstallBtn);

            // Make form draggable
            bool dragging = false;
            Point dragCursor = Point.Empty;
            Point dragForm = Point.Empty;

            titleBar.MouseDown += (s, e) =>
            {
                dragging = true;
                dragCursor = Cursor.Position;
                dragForm = this.Location;
            };
            titleBar.MouseMove += (s, e) =>
            {
                if (dragging)
                {
                    Point diff = Point.Subtract(Cursor.Position, new Size(dragCursor));
                    this.Location = Point.Add(dragForm, new Size(diff));
                }
            };
            titleBar.MouseUp += (s, e) => dragging = false;

            // Handle Escape key
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _confirmed = false;
                    this.Close();
                }
            };

            this.ResumeLayout(false);
        }

        private void PerformUninstall()
        {
            try
            {
                // Delete the DLL file
                if (File.Exists(_modFullPath))
                {
                    File.Delete(_modFullPath);
                    Logger.LogInfo($"Uninstalled mod: {_modFileName}");
                }

                // Also delete the manifest JSON if it exists
                string jsonPath = Path.ChangeExtension(_modFullPath, ".json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    Logger.LogInfo($"Deleted manifest: {Path.GetFileName(jsonPath)}");
                }

                _deleteSuccessful = true;
                this.Close();
                
                // Show success
                ConfirmDialog.ShowSuccess($"Successfully uninstalled '{_modFileName}'.", "Mod Uninstalled");
            }
            catch (UnauthorizedAccessException)
            {
                Logger.LogError($"Access denied when uninstalling mod {_modFileName}");
                ConfirmDialog.ShowError(
                    $"Cannot uninstall '{_modFileName}'.\n\n" +
                    "The file is locked. Please close Holdfast and try again.",
                    "File In Use");
            }
            catch (IOException ex) when (ex.Message.Contains("being used") || ex.HResult == -2147024864)
            {
                Logger.LogError($"File in use when uninstalling mod {_modFileName}: {ex.Message}");
                ConfirmDialog.ShowError(
                    $"Cannot uninstall '{_modFileName}'.\n\n" +
                    "The file is being used by another process.\n" +
                    "Please close Holdfast and try again.",
                    "File In Use");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to uninstall mod {_modFileName}: {ex.Message}");
                ConfirmDialog.ShowError(
                    $"Failed to uninstall '{_modFileName}'.\n\n{ex.Message}",
                    "Uninstall Error");
            }
        }

        /// <summary>
        /// Shows the uninstall dialog and returns true if uninstall was successful.
        /// </summary>
        public static bool ShowUninstallDialog(string modFileName, string modFullPath)
        {
            using (var dialog = new ModUninstallDialog(modFileName, modFullPath))
            {
                dialog.ShowDialog();
                return dialog.Confirmed && dialog.DeleteSuccessful;
            }
        }
    }
}

