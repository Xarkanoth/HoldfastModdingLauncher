using System.Drawing;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    /// <summary>
    /// Flat gray panel with a 1px border. Matches RC cards.
    /// </summary>
    public class ThemePanel : Panel
    {
        public ThemePanel()
        {
            BackColor = Theme.Panel;
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Theme.PaintBorder(this, e);
        }
    }
}
