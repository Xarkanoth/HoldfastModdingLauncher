using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    public class DisclaimerForm : Form
    {
        private bool _accepted = false;
        
        // Dark theme colors
        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color AccentOrange = Color.FromArgb(255, 180, 100);
        private static readonly Color AccentRed = Color.FromArgb(220, 80, 80);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);

        public bool Accepted => _accepted;

        public DisclaimerForm(bool isFirstRun = true)
        {
            InitializeComponent(isFirstRun);
        }

        private void InitializeComponent(bool isFirstRun)
        {
            this.SuspendLayout();

            int formWidth = 550;
            int formHeight = isFirstRun ? 520 : 480;

            // Form properties
            this.Text = "Holdfast Modding Launcher - Disclaimer";
            this.Size = new Size(formWidth, formHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = DarkBg;
            this.ForeColor = TextLight;
            this.ShowInTaskbar = true;
            this.TopMost = true;

            // Add border effect
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentOrange, 2))
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

            // Warning icon
            var warningIcon = new Label
            {
                Text = "⚠️",
                Font = new Font("Segoe UI", 18F),
                ForeColor = AccentOrange,
                AutoSize = true,
                Location = new Point(15, 8),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(warningIcon);

            // Title
            var titleLabel = new Label
            {
                Text = "IMPORTANT DISCLAIMER",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = AccentOrange,
                AutoSize = true,
                Location = new Point(55, 12),
                BackColor = Color.Transparent
            };
            titleBar.Controls.Add(titleLabel);

            // Close button (only if not first run)
            if (!isFirstRun)
            {
                var closeBtn = new Label
                {
                    Text = "✕",
                    Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                    ForeColor = TextGray,
                    AutoSize = true,
                    Location = new Point(formWidth - 35, 12),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                closeBtn.Click += (s, e) => this.Close();
                closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = AccentRed;
                closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = TextGray;
                titleBar.Controls.Add(closeBtn);
            }

            int yPos = 60;

            // Unofficial notice box
            var unofficialPanel = new Panel
            {
                BackColor = Color.FromArgb(40, 30, 20),
                Location = new Point(20, yPos),
                Size = new Size(formWidth - 40, 60)
            };
            unofficialPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentOrange, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, unofficialPanel.Width - 1, unofficialPanel.Height - 1);
                }
            };
            this.Controls.Add(unofficialPanel);

            var unofficialText = new Label
            {
                Text = "This is an UNOFFICIAL modding tool.\nIt is NOT developed, endorsed, or supported by Anvil Game Studios.",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentOrange,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 10),
                Size = new Size(unofficialPanel.Width - 20, 40),
                BackColor = Color.Transparent
            };
            unofficialPanel.Controls.Add(unofficialText);

            yPos += 75;

            // Fair Play header
            var fairPlayHeader = new Label
            {
                Text = "🎮  FAIR PLAY NOTICE",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccentCyan,
                AutoSize = true,
                Location = new Point(20, yPos),
                BackColor = Color.Transparent
            };
            this.Controls.Add(fairPlayHeader);

            yPos += 30;

            // Rules list
            string[] rules = new[]
            {
                "❌  DO NOT use mods to cheat or gain unfair advantages",
                "❌  DO NOT use mods to harass or abuse other players",
                "❌  DO NOT use mods to disrupt public servers",
                "✅  Respect all server rules and Terms of Service",
                "✅  Use mods responsibly for legitimate purposes only"
            };

            foreach (var rule in rules)
            {
                var ruleLabel = new Label
                {
                    Text = rule,
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = rule.StartsWith("❌") ? Color.FromArgb(255, 120, 120) : Color.FromArgb(120, 255, 120),
                    AutoSize = true,
                    Location = new Point(35, yPos),
                    BackColor = Color.Transparent
                };
                this.Controls.Add(ruleLabel);
                yPos += 26;
            }

            yPos += 10;

            // Responsibility notice
            var responsibilityPanel = new Panel
            {
                BackColor = Color.FromArgb(25, 35, 35),
                Location = new Point(20, yPos),
                Size = new Size(formWidth - 40, 50)
            };
            responsibilityPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentCyan, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, responsibilityPanel.Width - 1, responsibilityPanel.Height - 1);
                }
            };
            this.Controls.Add(responsibilityPanel);

            var responsibilityText = new Label
            {
                Text = "By using this tool, you acknowledge that you are solely\nresponsible for how you use it and its modifications.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextLight,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 8),
                Size = new Size(responsibilityPanel.Width - 20, 35),
                BackColor = Color.Transparent
            };
            responsibilityPanel.Controls.Add(responsibilityText);

            yPos += 65;

            // Trademark notice
            var trademarkLabel = new Label
            {
                Text = "Holdfast: Nations At War is a trademark of Anvil Game Studios.\nThis project is not affiliated with Anvil Game Studios.",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = TextGray,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, yPos),
                Size = new Size(formWidth - 40, 35),
                BackColor = Color.Transparent
            };
            this.Controls.Add(trademarkLabel);

            yPos += 50;

            if (isFirstRun)
            {
                // Buttons for first run
                var declineBtn = new Button
                {
                    Text = "Decline",
                    Font = new Font("Segoe UI", 10F),
                    Size = new Size(120, 40),
                    Location = new Point((formWidth / 2) - 130, yPos),
                    BackColor = DarkPanel,
                    ForeColor = TextGray,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                declineBtn.FlatAppearance.BorderColor = TextGray;
                declineBtn.Click += (s, e) =>
                {
                    _accepted = false;
                    this.Close();
                };
                this.Controls.Add(declineBtn);

                var acceptBtn = new Button
                {
                    Text = "✓  I Accept",
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Size = new Size(130, 40),
                    Location = new Point((formWidth / 2) + 10, yPos),
                    BackColor = DarkPanel,
                    ForeColor = AccentCyan,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                acceptBtn.FlatAppearance.BorderColor = AccentCyan;
                acceptBtn.Click += (s, e) =>
                {
                    _accepted = true;
                    this.Close();
                };
                this.Controls.Add(acceptBtn);
            }
            else
            {
                // Single OK button for viewing
                var okBtn = new Button
                {
                    Text = "I Understand",
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Size = new Size(150, 40),
                    Location = new Point((formWidth - 150) / 2, yPos),
                    BackColor = DarkPanel,
                    ForeColor = AccentCyan,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                okBtn.FlatAppearance.BorderColor = AccentCyan;
                okBtn.Click += (s, e) => this.Close();
                this.Controls.Add(okBtn);
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

            this.ResumeLayout(false);
        }

        // Static method to show first-run disclaimer and return acceptance
        public static bool ShowFirstRunDisclaimer()
        {
            using (var form = new DisclaimerForm(true))
            {
                form.ShowDialog();
                return form.Accepted;
            }
        }

        // Static method to show disclaimer for viewing
        public static void ShowDisclaimerInfo()
        {
            using (var form = new DisclaimerForm(false))
            {
                form.ShowDialog();
            }
        }
    }
}

