using System.Drawing;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    /// <summary>
    /// Regiment Control colors for the launcher. Gold accent, gray panels, no cyan.
    /// </summary>
    public static class Theme
    {
        public static readonly Color PageBg = Color.FromArgb(18, 20, 26);
        public static readonly Color HeaderBg = Color.FromArgb(24, 26, 33);
        public static readonly Color Panel = Color.FromArgb(24, 26, 33);
        public static readonly Color PanelAlt = Color.FromArgb(30, 32, 40);
        public static readonly Color Border = Color.FromArgb(55, 65, 81);
        public static readonly Color Brand = Color.FromArgb(168, 130, 63);
        public static readonly Color BrandHover = Color.FromArgb(134, 98, 47);
        public static readonly Color BrandText = Color.FromArgb(205, 163, 85);
        public static readonly Color Umber = Color.FromArgb(140, 104, 77);
        public static readonly Color Text = Color.FromArgb(229, 231, 235);
        public static readonly Color TextMuted = Color.FromArgb(156, 163, 175);
        public static readonly Color Success = Color.FromArgb(16, 185, 129);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Warning = Color.FromArgb(201, 163, 92);

        public static void ApplyForm(Form form)
        {
            form.BackColor = PageBg;
            form.ForeColor = Text;
        }

        public static void ApplyPrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Brand;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = Brand;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = BrandHover;
            button.FlatAppearance.MouseDownBackColor = BrandHover;
        }

        public static void ApplyGhostButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Panel;
            button.ForeColor = Text;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = PanelAlt;
            button.FlatAppearance.MouseDownBackColor = PanelAlt;
        }

        public static void ApplyDangerButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Danger;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = Danger;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(185, 28, 28);
        }

        public static void ApplyInput(TextBox box)
        {
            box.BackColor = PanelAlt;
            box.ForeColor = Text;
            box.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void PaintBorder(Control control, PaintEventArgs e)
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
        }
    }
}
