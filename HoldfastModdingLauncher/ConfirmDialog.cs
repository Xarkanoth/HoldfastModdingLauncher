using System;
using System.Drawing;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    public enum ConfirmDialogType
    {
        YesNo,
        OkCancel,
        Ok
    }

    public enum ConfirmDialogIcon
    {
        Warning,
        Question,
        Info,
        Error,
        Success
    }

    public class ConfirmDialog : Form
    {
        private bool _confirmed = false;
        
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color AccentOrange = Color.FromArgb(255, 180, 100);
        private static readonly Color AccentRed = Color.FromArgb(220, 80, 80);
        private static readonly Color AccentGreen = Color.FromArgb(80, 200, 120);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);

        public bool Confirmed => _confirmed;

        public ConfirmDialog(string message, string title, ConfirmDialogType dialogType = ConfirmDialogType.YesNo, ConfirmDialogIcon icon = ConfirmDialogIcon.Question)
        {
            InitializeComponent(message, title, dialogType, icon);
        }

        private void InitializeComponent(string message, string title, ConfirmDialogType dialogType, ConfirmDialogIcon icon)
        {
            this.SuspendLayout();

            // Wider dialog for error messages which tend to be longer
            int formWidth = icon == ConfirmDialogIcon.Error ? 520 : 450;
            int formHeight = 220;

            // Calculate message height based on message length
            using (var g = this.CreateGraphics())
            {
                var messageFont = new Font("Segoe UI", 10F);
                var messageSize = g.MeasureString(message, messageFont, formWidth - 60);
                formHeight = Math.Max(220, (int)messageSize.Height + 170);
                // Cap height
                formHeight = Math.Min(formHeight, 400);
            }

            // Form properties
            this.Text = title;
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            // Get accent color based on icon type
            Color accentColor = icon switch
            {
                ConfirmDialogIcon.Warning => AccentOrange,
                ConfirmDialogIcon.Error => AccentRed,
                ConfirmDialogIcon.Success => AccentGreen,
                ConfirmDialogIcon.Info => AccentCyan,
                _ => AccentCyan
            };

            // Add border effect
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(accentColor, 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, this.Width - 3, this.Height - 3);
                }
            };

            // Title bar panel
            var titleBar = new Panel
            {
                BackColor = DarkPanel,
                Location = new Point(2, 2),
                Size = new Size(formWidth - 4, 40)
            };
            this.Controls.Add(titleBar);

            // Icon emoji
            string iconEmoji = icon switch
            {
                ConfirmDialogIcon.Warning => "⚠️",
                ConfirmDialogIcon.Error => "❌",
                ConfirmDialogIcon.Success => "✅",
                ConfirmDialogIcon.Info => "ℹ️",
                ConfirmDialogIcon.Question => "❓",
                _ => "❓"
            };

            var iconLabel = new Label
            {
                Text = iconEmoji,
                Font = new Font("Segoe UI", 14F),
                ForeColor = accentColor,
                AutoSize = true,
                Location = new Point(12, 8),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(iconLabel);

            // Title
            var titleLabel = new Label
            {
                Text = title.ToUpper(),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = accentColor,
                AutoSize = true,
                Location = new Point(45, 10),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(titleLabel);

            // Message
            var messageLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(25, 55),
                Size = new Size(formWidth - 50, formHeight - 130),
                BackColor = Color.Transparent
            };
            this.Controls.Add(messageLabel);

            // Buttons panel
            var buttonPanel = new Panel
            {
                BackColor = Color.Transparent,
                Location = new Point(0, formHeight - 60),
                Size = new Size(formWidth, 58)
            };
            this.Controls.Add(buttonPanel);

            switch (dialogType)
            {
                case ConfirmDialogType.YesNo:
                    CreateYesNoButtons(buttonPanel, formWidth, accentColor);
                    break;
                case ConfirmDialogType.OkCancel:
                    CreateOkCancelButtons(buttonPanel, formWidth, accentColor);
                    break;
                case ConfirmDialogType.Ok:
                    CreateOkButton(buttonPanel, formWidth, accentColor);
                    break;
            }

            // Make form draggable from title bar
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

            // Handle Enter key
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    _confirmed = true;
                    this.Close();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _confirmed = false;
                    this.Close();
                }
            };

            this.ResumeLayout(false);
        }

        private void CreateYesNoButtons(Panel buttonPanel, int formWidth, Color accentColor)
        {
            var noBtn = new Button
            {
                Text = "No",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 36),
                Location = new Point((formWidth / 2) - 110, 10),
                BackColor = DarkPanel,
                ForeColor = TextGray,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            noBtn.FlatAppearance.BorderColor = TextGray;
            noBtn.Click += (s, e) =>
            {
                _confirmed = false;
                this.Close();
            };
            buttonPanel.Controls.Add(noBtn);

            var yesBtn = new Button
            {
                Text = "Yes",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(100, 36),
                Location = new Point((formWidth / 2) + 10, 10),
                BackColor = DarkPanel,
                ForeColor = accentColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            yesBtn.FlatAppearance.BorderColor = accentColor;
            yesBtn.Click += (s, e) =>
            {
                _confirmed = true;
                this.Close();
            };
            buttonPanel.Controls.Add(yesBtn);
        }

        private void CreateOkCancelButtons(Panel buttonPanel, int formWidth, Color accentColor)
        {
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 36),
                Location = new Point((formWidth / 2) - 110, 10),
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

            var okBtn = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(100, 36),
                Location = new Point((formWidth / 2) + 10, 10),
                BackColor = DarkPanel,
                ForeColor = accentColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            okBtn.FlatAppearance.BorderColor = accentColor;
            okBtn.Click += (s, e) =>
            {
                _confirmed = true;
                this.Close();
            };
            buttonPanel.Controls.Add(okBtn);
        }

        private void CreateOkButton(Panel buttonPanel, int formWidth, Color accentColor)
        {
            var okBtn = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(120, 36),
                Location = new Point((formWidth - 120) / 2, 10),
                BackColor = DarkPanel,
                ForeColor = accentColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            okBtn.FlatAppearance.BorderColor = accentColor;
            okBtn.Click += (s, e) =>
            {
                _confirmed = true;
                this.Close();
            };
            buttonPanel.Controls.Add(okBtn);
        }

        // Static helper methods
        public static bool ShowConfirm(string message, string title, ConfirmDialogIcon icon = ConfirmDialogIcon.Question)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.YesNo, icon))
            {
                dialog.ShowDialog();
                return dialog.Confirmed;
            }
        }

        public static bool ShowOkCancel(string message, string title, ConfirmDialogIcon icon = ConfirmDialogIcon.Question)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.OkCancel, icon))
            {
                dialog.ShowDialog();
                return dialog.Confirmed;
            }
        }

        public static void ShowInfo(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Info))
            {
                dialog.ShowDialog();
            }
        }

        public static void ShowSuccess(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Success))
            {
                dialog.ShowDialog();
            }
        }

        public static void ShowWarning(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Warning))
            {
                dialog.ShowDialog();
            }
        }

        public static void ShowError(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Error))
            {
                dialog.ShowDialog();
            }
        }
    }
}

