using System;
using System.Drawing;
using System.Windows.Forms;

namespace HoldfastModdingLauncher
{
    /// <summary>
    /// Vertical-only list host. Custom gold/gray bar, no horizontal scrollbar.
    /// </summary>
    public class ThemeScrollPanel : Panel
    {
        public const int BarWidth = 12;

        private readonly ThemeVScrollBar _bar;
        private bool _syncing;

        public Panel Content { get; }

        public ThemeScrollPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.PageBg;
            BorderStyle = BorderStyle.None;

            Content = new Panel
            {
                BackColor = Theme.PageBg,
                BorderStyle = BorderStyle.None
            };
            _bar = new ThemeVScrollBar();
            _bar.Scroll += (s, e) => ApplyOffset();

            Controls.Add(Content);
            Controls.Add(_bar);

            MouseEnter += (s, e) => Focus();
            Content.MouseEnter += (s, e) => Focus();
            MouseWheel += OnWheel;
            Content.MouseWheel += OnWheel;
            Content.ControlAdded += (s, e) =>
            {
                e.Control.MouseWheel += OnWheel;
                Recalc();
            };
            Content.ControlRemoved += (s, e) => Recalc();
        }

        public void Recalc()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                int contentH = MeasureContentHeight();
                int viewH = Height;
                bool needBar = contentH > viewH && viewH > 0;
                int barW = needBar ? BarWidth : 0;

                _bar.Visible = needBar;
                _bar.Bounds = new Rectangle(Width - barW, 0, barW, Math.Max(0, viewH));
                Content.Width = Math.Max(0, Width - barW);
                Content.Height = Math.Max(viewH, contentH);

                int max = Math.Max(0, Content.Height - viewH);
                _bar.ViewSize = viewH;
                _bar.ContentSize = Content.Height;
                _bar.Maximum = max;
                if (_bar.Value > max)
                    _bar.Value = max;
                ApplyOffset();
            }
            finally
            {
                _syncing = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalc();
        }

        private int MeasureContentHeight()
        {
            int bottom = 0;
            foreach (Control child in Content.Controls)
                bottom = Math.Max(bottom, child.Bottom);
            return bottom;
        }

        private void ApplyOffset()
        {
            Content.Location = new Point(0, -_bar.Value);
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            if (!_bar.Visible) return;
            int step = 56;
            _bar.Value = Math.Max(0, Math.Min(_bar.Maximum, _bar.Value - Math.Sign(e.Delta) * step));
        }
    }

    public class ThemeVScrollBar : Control
    {
        private int _value;
        private int _maximum;
        private bool _dragging;
        private int _dragThumbY;
        private int _dragStartValue;
        private bool _hot;

        public int Value
        {
            get => _value;
            set
            {
                int next = Math.Max(0, Math.Min(Maximum, value));
                if (_value == next) return;
                _value = next;
                Invalidate();
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(0, value);
                if (_value > _maximum)
                    Value = _maximum;
                Invalidate();
            }
        }

        public int ViewSize { get; set; } = 1;
        public int ContentSize { get; set; } = 1;

        public event EventHandler Scroll;

        public ThemeVScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = ThemeScrollPanel.BarWidth;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_dragging) _hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var thumb = ThumbRect();
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragThumbY = e.Y;
                _dragStartValue = Value;
                Capture = true;
                return;
            }

            int page = Math.Max(40, ViewSize);
            Value += e.Y < thumb.Y ? -page : page;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            int track = Math.Max(1, Height - ThumbRect().Height);
            int deltaPx = e.Y - _dragThumbY;
            int deltaVal = (int)Math.Round(deltaPx * (Maximum / (float)track));
            Value = _dragStartValue + deltaVal;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            _hot = ClientRectangle.Contains(PointToClient(MousePosition));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.HeaderBg);
            using (var track = new SolidBrush(Theme.PanelAlt))
                g.FillRectangle(track, 3, 4, Width - 6, Height - 8);

            var thumb = ThumbRect();
            Color thumbColor = _dragging || _hot ? Theme.Brand : Theme.Border;
            using var brush = new SolidBrush(thumbColor);
            g.FillRectangle(brush, thumb);
        }

        private Rectangle ThumbRect()
        {
            int pad = 4;
            int trackH = Math.Max(1, Height - pad * 2);
            int content = Math.Max(ViewSize, ContentSize);
            int thumbH = Math.Max(32, (int)(trackH * (ViewSize / (float)content)));
            thumbH = Math.Min(trackH, thumbH);
            int travel = Math.Max(0, trackH - thumbH);
            int y = pad;
            if (Maximum > 0)
                y = pad + (int)(travel * (Value / (float)Maximum));
            return new Rectangle(2, y, Width - 4, thumbH);
        }
    }
}
