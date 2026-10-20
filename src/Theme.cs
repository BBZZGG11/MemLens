using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MemLens
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(241, 245, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Ink = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color Navy = Color.FromArgb(15, 23, 42);
        public static readonly Color Teal = Color.FromArgb(13, 148, 136);
        public static readonly Color TealSoft = Color.FromArgb(240, 253, 250);
        public static readonly Color Sky = Color.FromArgb(14, 165, 233);
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);
        public static readonly Color AmberSoft = Color.FromArgb(255, 251, 235);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color RedSoft = Color.FromArgb(254, 242, 242);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Header = Color.FromArgb(248, 250, 252);

        public static Font Ui(float size)
        {
            return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font UiBold(float size)
        {
            return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font UiSemibold(float size)
        {
            return new Font("Segoe UI Semibold", size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    internal sealed class MemoryBar : Control
    {
        private ulong _used;
        private ulong _standby;
        private ulong _modified;
        private ulong _free;
        private ulong _total;

        public MemoryBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 14;
            BackColor = Theme.Bg;
        }

        public void SetValues(ulong used, ulong standby, ulong modified, ulong free, ulong total)
        {
            _used = used;
            _standby = standby;
            _modified = modified;
            _free = free;
            _total = total;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            int radius = Math.Max(3, rect.Height / 2);
            using (GraphicsPath path = Rounded(rect, radius))
            {
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(226, 232, 240)))
                    g.FillPath(bg, path);

                if (_total > 0)
                {
                    g.SetClip(path);
                    float x = 0;
                    x = DrawSegment(g, x, rect.Width, _used, Theme.Sky);
                    x = DrawSegment(g, x, rect.Width, _standby, Theme.Teal);
                    x = DrawSegment(g, x, rect.Width, _modified, Theme.Amber);
                    DrawSegment(g, x, rect.Width, _free, Color.FromArgb(203, 213, 225));
                    g.ResetClip();
                }

                using (Pen pen = new Pen(Color.FromArgb(203, 213, 225), 1f))
                    g.DrawPath(pen, path);
            }
        }

        private float DrawSegment(Graphics g, float x, int width, ulong value, Color color)
        {
            if (value == 0 || _total == 0) return x;
            double fraction = (double)value / (double)_total;
            float w = (float)(fraction * width);
            if (w < 0) w = 0;
            if (x + w > width) w = width - x;
            if (w <= 0) return x;
            using (SolidBrush brush = new SolidBrush(color))
                g.FillRectangle(brush, x, 0, w, ClientSize.Height);
            return x + w;
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
