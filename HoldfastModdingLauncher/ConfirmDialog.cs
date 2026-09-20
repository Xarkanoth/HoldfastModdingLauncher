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
        
        private static readonly Color DarkBg = Theme.PageBg;
        private static readonly Color DarkPanel = Theme.Panel;
        private static readonly Color AccentCyan = Theme.Brand;
        private static readonly Color AccentOrange = Theme.Warning;
        private static readonly Color AccentRed = Theme.Danger;
        private static readonly Color AccentGreen = Theme.Success;
        private static readonly Color TextLight = Theme.Text;
        private static readonly Color TextGray = Theme.TextMuted;

        public bool Confirmed => _confirmed;

        public ConfirmDialog(string message, string title, ConfirmDialogType dialogType = ConfirmDialogType.YesNo, ConfirmDialogIcon icon = ConfirmDialogIcon.Question)
        {
            InitializeComponent(message, title, dialogType, icon);
        }

        private void InitializeComponent(string message, string title, ConfirmDialogType dialogType, ConfirmDialogIcon icon)
        {
            this.SuspendLayout();

            int formWidth = icon == ConfirmDialogIcon.Error ? 520 : 450;
            
            int baseHeight = 40 + 15 + 15 + 80;
            int messageHeight = 60;

            using (var g = this.CreateGraphics())
            {
                var messageFont = new Font("Segoe UI", 10F);
                var messageSize = g.MeasureString(message, messageFont, formWidth - 60);
                
                int newlineCount = message.Split('\n').Length - 1;
                float additionalHeight = newlineCount * 25;
                
                messageHeight = Math.Max(60, (int)(messageSize.Height + additionalHeight));
            }
            
            int formHeight = baseHeight + messageHeight;
            formHeight = Math.Max(260, Math.Min(formHeight, 500));

            this.Text = title;
            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ClientSize = new Size(formWidth, formHeight);
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
                Size = new Size(this.ClientSize.Width - 4, 40)
            };
            this.Controls.Add(titleBar);

            var iconLabel = new Panel
            {
                Size = new Size(8, 22),
                Location = new Point(14, 9),
                BackColor = accentColor
            };
            titleBar.Controls.Add(iconLabel);

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = accentColor,
                AutoSize = true,
                Location = new Point(45, 10),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(titleLabel);

            var messageLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                BackColor = Color.Transparent
            };
            this.Controls.Add(messageLabel);

            var buttonPanel = new Panel
            {
                BackColor = Color.Transparent
            };
            this.Controls.Add(buttonPanel);

            // Dynamic layout based on actual client size
            this.Layout += (s, e) =>
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;
                titleBar.Size = new Size(w - 4, 40);
                messageLabel.Location = new Point(25, 55);
                messageLabel.Size = new Size(w - 50, h - 140);
                buttonPanel.Location = new Point(0, h - 80);
                buttonPanel.Size = new Size(w, 78);
            };

            switch (dialogType)
            {
                case ConfirmDialogType.YesNo:
                    CreateYesNoButtons(buttonPanel, accentColor);
                    break;
                case ConfirmDialogType.OkCancel:
                    CreateOkCancelButtons(buttonPanel, accentColor);
                    break;
                case ConfirmDialogType.Ok:
                    CreateOkButton(buttonPanel, accentColor);
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

        private void CreateYesNoButtons(Panel buttonPanel, Color accentColor)
        {
            var noBtn = new Button
            {
                Text = "No",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 36),
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

            buttonPanel.Layout += (s, e) =>
            {
                int pw = buttonPanel.Width;
                noBtn.Location = new Point((pw / 2) - 110, 20);
                yesBtn.Location = new Point((pw / 2) + 10, 20);
            };
        }

        private void CreateOkCancelButtons(Panel buttonPanel, Color accentColor)
        {
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                Size = new Size(100, 36),
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

            buttonPanel.Layout += (s, e) =>
            {
                int pw = buttonPanel.Width;
                cancelBtn.Location = new Point((pw / 2) - 110, 20);
                okBtn.Location = new Point((pw / 2) + 10, 20);
            };
        }

        private void CreateOkButton(Panel buttonPanel, Color accentColor)
        {
            var okBtn = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(120, 36),
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

            buttonPanel.Layout += (s, e) =>
            {
                int pw = buttonPanel.Width;
                okBtn.Location = new Point((pw - 120) / 2, 20);
            };
        }

        // Static helper methods
        public static bool ShowConfirm(string message, string title, ConfirmDialogIcon icon = ConfirmDialogIcon.Question)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.YesNo, icon))
            {
                DialogOwner.ShowCentered(dialog);
                return dialog.Confirmed;
            }
        }

        public static bool ShowOkCancel(string message, string title, ConfirmDialogIcon icon = ConfirmDialogIcon.Question)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.OkCancel, icon))
            {
                DialogOwner.ShowCentered(dialog);
                return dialog.Confirmed;
            }
        }

        public static void ShowInfo(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Info))
            {
                DialogOwner.ShowCentered(dialog);
            }
        }

        public static void ShowSuccess(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Success))
            {
                DialogOwner.ShowCentered(dialog);
            }
        }

        public static void ShowWarning(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Warning))
            {
                DialogOwner.ShowCentered(dialog);
            }
        }

        public static void ShowError(string message, string title)
        {
            using (var dialog = new ConfirmDialog(message, title, ConfirmDialogType.Ok, ConfirmDialogIcon.Error))
            {
                DialogOwner.ShowCentered(dialog);
            }
        }
    }
}

