using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pulp
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 32);
        public static readonly Color Card = Color.FromArgb(46, 46, 46);
        public static readonly Color CardSoft = Color.FromArgb(39, 39, 39);
        public static readonly Color CardHover = Color.FromArgb(58, 58, 58);
        public static readonly Color Field = Color.FromArgb(44, 44, 44);
        public static readonly Color IconBg = Color.FromArgb(44, 44, 44);
        public static readonly Color IconBgSel = Color.FromArgb(58, 58, 58);
        public static readonly Color ThumbBg = Color.FromArgb(24, 24, 24);
        public static readonly Color Border = Color.FromArgb(62, 62, 62);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Sub = Color.FromArgb(165, 165, 165);
        public static readonly Color Dim = Color.FromArgb(118, 118, 118);
        public static readonly Color Red = Color.FromArgb(240, 96, 96);

        // brand: option A "Slice" (tangerine). Swap these four to rebrand.
        public static readonly Color Accent = Color.FromArgb(255, 138, 61);     // #FF8A3D
        public static readonly Color AccentHover = Color.FromArgb(255, 160, 96);
        public static readonly Color LogoA = Color.FromArgb(224, 72, 11);       // #E0480B
        public static readonly Color LogoB = Color.FromArgb(255, 174, 69);      // #FFAE45

        public static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        // Windows 11's own UI font (Segoe UI Variable), falling back to Segoe UI on Windows 10
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string UiSemiFamily = FontExists("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold"
            : FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : null;
        static readonly string UiDisplaySemi = FontExists("Segoe UI Variable Display Semibold") ? "Segoe UI Variable Display Semibold" : null;
        public static Font Ui(float size) => new Font(UiFamily, size);
        public static Font UiSemi(float size) => UiSemiFamily != null ? new Font(UiSemiFamily, size) : new Font(UiFamily, size, FontStyle.Bold);
        public static Font UiTitle(float size) => UiDisplaySemi != null ? new Font(UiDisplaySemi, size) : UiSemi(size);

        // Arabic text: Segoe UI Variable has no Arabic letters, so Windows falls back to an old font.
        // Use SF Arabic if it's installed, then Dubai, then Segoe UI (which has proper Arabic).
        public static readonly string ArabicFamily =
            FontExists("SF Arabic") ? "SF Arabic" : FontExists("SF Pro Arabic") ? "SF Pro Arabic"
            : FontExists("Dubai") ? "Dubai" : "Segoe UI";
        public static Font Arabic(float size) => new Font(ArabicFamily, size);

        public static bool HasArabic(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            int n = Math.Min(t.Length, 400);
            for (int i = 0; i < n; i++)
            {
                char c = t[i];
                if ((c >= '\u0600' && c <= '\u06FF') || (c >= '\u0750' && c <= '\u077F') || (c >= '\u08A0' && c <= '\u08FF')
                    || (c >= '\uFB50' && c <= '\uFDFF') || (c >= '\uFE70' && c <= '\uFEFF')) return true;
            }
            return false;
        }

        // starts with Arabic (first real letter): show it right-aligned, reading right to left
        public static bool RtlFirst(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            foreach (char c in t)
            {
                if (char.IsLetter(c)) return (c >= '\u0590' && c <= '\u08FF') || (c >= '\uFB1D' && c <= '\uFEFF');
            }
            return false;
        }

        // Segoe Fluent Icons / MDL2 glyphs
        public const string GSearch = "", GLink = "", GText = "", GFolder = "", GImage = "",
            GPin = "", GUnpin = "", GPinned = "", GDelete = "", GSave = "", GCopy = "",
            GMore = "", GPause = "", GUndo = "", GClear = "", GCheck = "";

        static bool FontExists(string name)
        {
            try { using (var f = new Font(name, 10)) return f.Name == name; } catch { return false; }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, Color c, float rad)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, Color c, float rad, float w = 1)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        // citrus wheel: rind ring + segments between spokes (R = outer radius)
        public static void DrawSlice(Graphics g, PointF c, float R, Color rind, Color seg, int spokes, float spokeW, float hole, float rotDeg = 22.5f)
        {
            float R2 = R * 0.8576f, R3 = R * 0.7636f, d = R * spokeW / 2f, rh = R * hole;
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddEllipse(c.X - R, c.Y - R, 2 * R, 2 * R);
                path.AddEllipse(c.X - R2, c.Y - R2, 2 * R2, 2 * R2);
                using (var b = new SolidBrush(rind)) g.FillPath(b, path);
            }
            using (var b = new SolidBrush(seg))
                for (int k = 0; k < spokes; k++)
                {
                    double a0 = (rotDeg + k * 360.0 / spokes) * Math.PI / 180, a1 = a0 + 2 * Math.PI / spokes;
                    var pts = new List<PointF>();
                    double o = Math.Asin(Math.Min(1, d / R3)), s0 = a0 + o, s1 = a1 - o;
                    const int N = 12;
                    for (int i = 0; i <= N; i++)
                    {
                        double t = s0 + (s1 - s0) * i / N;
                        pts.Add(new PointF(c.X + (float)(Math.Cos(t) * R3), c.Y + (float)(Math.Sin(t) * R3)));
                    }
                    double half = (a1 - a0) / 2, apex = d / Math.Sin(half);
                    if (rh <= apex) pts.Add(new PointF(c.X + (float)(Math.Cos(a0 + half) * apex), c.Y + (float)(Math.Sin(a0 + half) * apex)));
                    else
                    {
                        double oi = Math.Asin(Math.Min(1, d / rh)), i0 = a1 - oi, i1 = a0 + oi;
                        for (int i = 0; i <= N / 2; i++)
                        {
                            double t = i0 + (i1 - i0) * i / (N / 2);
                            pts.Add(new PointF(c.X + (float)(Math.Cos(t) * rh), c.Y + (float)(Math.Sin(t) * rh)));
                        }
                    }
                    g.FillPolygon(b, pts.ToArray());
                }
        }

        // app logo: tangerine squircle with a white citrus wheel
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            using (var path = Round(r, r.Width * 0.24f))
            using (var b = new LinearGradientBrush(new PointF(r.X, r.Y), new PointF(r.Right, r.Bottom), LogoA, LogoB))
                g.FillPath(b, path);
            var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            bool small = r.Width < 40;
            DrawSlice(g, c, r.Width * 0.33f, Color.White, Color.White, small ? 6 : 8, small ? 0.16f : 0.103f, small ? 0f : 0.136f);
        }
    }

    static class TrayArt
    {
        // accent rind + white segments; grey rind while paused
        public static Icon Render(bool paused)
        {
            int sz = SystemInformation.SmallIconSize.Width;
            if (sz < 16) sz = 16;
            int big = sz * 4;
            using (var hi = new Bitmap(big, big))
            using (var bmp = new Bitmap(sz, sz))
            {
                using (var g = Graphics.FromImage(hi))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    Color rind = paused ? Color.FromArgb(150, 150, 150) : Theme.Accent;
                    Color seg = paused ? Color.FromArgb(200, 200, 200) : Color.White;
                    Theme.DrawSlice(g, new PointF(big / 2f, big / 2f), big * 0.48f, rind, seg, 6, 0.182f, 0f);
                }
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(hi, new Rectangle(0, 0, sz, sz));
                }
                IntPtr h = bmp.GetHicon();
                using (var tmp = Icon.FromHandle(h))
                {
                    var ico = (Icon)tmp.Clone();
                    Native.DestroyIcon(h);
                    return ico;
                }
            }
        }
    }

    // ---------------------------------------------------------------- dark menu
    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Back = Color.FromArgb(40, 40, 40), Hi = Color.FromArgb(60, 60, 60);
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color MenuItemPressedGradientBegin => Hi;
        public override Color MenuItemPressedGradientEnd => Hi;
        public override Color SeparatorDark => Color.FromArgb(64, 64, 64);
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hi;
        public override Color CheckPressedBackground => Hi;
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item is ToolStripMenuItem mi && mi.ShortcutKeyDisplayString == e.Text ? Theme.Dim : Theme.Text) : Theme.Dim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Sub;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (var f = new Font(Theme.IconFont, 9f))
                TextRenderer.DrawText(e.Graphics, Theme.GCheck, f, e.ImageRectangle, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
