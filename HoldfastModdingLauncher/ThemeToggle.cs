using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    /// <summary>
    /// Gold on / gray off switch. Circular thumb, 44x24 hit box.
    /// </summary>
    public class ThemeToggle : Control
    {
        private bool _checked;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler CheckedChanged;

        public ThemeToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(44, 24);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled)
                Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int trackH = 18;
            int trackY = (Height - trackH) / 2;
            var track = new Rectangle(1, trackY, Width - 3, trackH);
            Color trackColor = !Enabled
                ? Theme.Border
                : (_checked ? Theme.Brand : Color.FromArgb(75, 85, 99));

            using (var path = RoundedRect(track, trackH / 2))
            using (var brush = new SolidBrush(trackColor))
                g.FillPath(brush, path);

            int thumb = 14;
            int thumbY = (Height - thumb) / 2;
            int thumbX = _checked ? Width - thumb - 4 : 4;
            using var thumbBrush = new SolidBrush(Enabled ? Color.White : Theme.TextMuted);
            g.FillEllipse(thumbBrush, thumbX, thumbY, thumb, thumb);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
