using System;
using System.Drawing;
using System.Windows.Forms;

namespace MemLens
{
    internal static class DpiScale
    {
        public static float Factor(Form form)
        {
            try
            {
                uint dpi = Native.GetDpiForSystem();
                if (dpi >= 96) return dpi / 96f;
            }
            catch { }

            try
            {
                uint dpi = Native.GetDpiForWindow(form.Handle);
                if (dpi >= 96) return dpi / 96f;
            }
            catch { }

            try
            {
                using (Graphics g = form.CreateGraphics())
                    return Math.Max(1f, g.DpiX / 96f);
            }
            catch
            {
                return 1f;
            }
        }

        public static float Apply(Form form, Size designClient, Size designMinimum)
        {
            float scale = Factor(form);
            if (scale <= 1.01f) return 1f;

            form.SuspendLayout();
            form.Scale(new SizeF(scale, scale));
            form.ClientSize = new Size(
                (int)Math.Round(designClient.Width * scale),
                (int)Math.Round(designClient.Height * scale));
            if (designMinimum.Width > 0 && designMinimum.Height > 0)
            {
                form.MinimumSize = new Size(
                    (int)Math.Round(designMinimum.Width * scale),
                    (int)Math.Round(designMinimum.Height * scale));
            }
            form.ResumeLayout(true);
            return scale;
        }
    }
}
