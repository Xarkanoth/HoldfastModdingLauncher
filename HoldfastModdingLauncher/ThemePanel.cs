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
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.PaintBorder(this, e);
        }
    }
}
