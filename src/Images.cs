using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pulp
{
    // raw 32-bit BGRA pixels, top-down, stride = W*4
    class Pixels
    {
        public int W, H;
        public byte[] Data;
        public bool Opaque;
    }

    static class ImageTools
    {
        public const int MaxMegapixels = 40;

        // clipboard CF_DIB -> pixels, by wrapping it in a BMP file header so GDI+ handles every DIB variant
        public static Pixels FromDib(byte[] dib)
        {
            if (dib == null || dib.Length < 40) return null;
            int biSize = BitConverter.ToInt32(dib, 0);
            int bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int clrUsed = BitConverter.ToInt32(dib, 32);
            int masks = biSize == 40 && compression == 3 ? 12 : biSize == 40 && compression == 6 ? 16 : 0;
            int colors = clrUsed != 0 ? clrUsed : bitCount <= 8 ? 1 << bitCount : 0;
            int offBits = 14 + biSize + masks + colors * 4;
            var bmp = new byte[14 + dib.Length];
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
            BitConverter.GetBytes(offBits).CopyTo(bmp, 10);
            Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
            return FromBytes(bmp);
        }

        // PNG/BMP/etc. bytes -> pixels (read straight from the decoded image, never redrawn,
        // so colour under a zero alpha channel survives)
        public static Pixels FromBytes(byte[] file)
        {
            using (var ms = new MemoryStream(file))
            using (var img = Image.FromStream(ms, false, false))
            {
                if ((long)img.Width * img.Height > MaxMegapixels * 1000000L) return null;
                if (img is Bitmap b) return GetPixels(b);
                using (var copy = new Bitmap(img)) return GetPixels(copy);
            }
        }

        public static Bitmap Load(string path)
        {
            // load without keeping the file locked
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var img = Image.FromStream(fs, false, false))
                return new Bitmap(img);
        }

        public static Pixels GetPixels(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            var px = new Pixels { W = w, H = h, Data = new byte[w * h * 4] };
            var bd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, px.Data, y * w * 4, w * 4);
            }
            finally { src.UnlockBits(bd); }
            bool allZero = true, opaque = true;
            var d = px.Data;
            for (int i = 3; i < d.Length; i += 4)
            {
                byte a = d[i];
                if (a != 0) allZero = false;
                if (a != 255) opaque = false;
                if (!allZero && !opaque) break;
            }
            if (allZero)
            {
                // some apps put 32-bit DIBs with an unused (zero) alpha byte: that means opaque
                for (int i = 3; i < d.Length; i += 4) d[i] = 255;
                opaque = true;
            }
            px.Opaque = opaque;
            return px;
        }

        public static string Hash(Pixels p)
        {
            using (var sha = SHA1.Create())
            {
                var head = Encoding.ASCII.GetBytes(p.W + "x" + p.H + ";");
                sha.TransformBlock(head, 0, head.Length, null, 0);
                sha.TransformFinalBlock(p.Data, 0, p.Data.Length);
                var sb = new StringBuilder(40);
                foreach (var b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // 24-bit when fully opaque (smaller PNG), 32-bit ARGB otherwise
        public static Bitmap ToBitmap(Pixels p)
        {
            int w = p.W, h = p.H;
            if (!p.Opaque)
            {
                var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < h; y++) Marshal.Copy(p.Data, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4); }
                finally { b.UnlockBits(bd); }
                return b;
            }
            var o = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var od = o.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[od.Stride];
                for (int y = 0; y < h; y++)
                {
                    int s = y * w * 4;
                    for (int x = 0, t = 0; x < w; x++, s += 4, t += 3)
                    {
                        row[t] = p.Data[s]; row[t + 1] = p.Data[s + 1]; row[t + 2] = p.Data[s + 2];
                    }
                    Marshal.Copy(row, 0, od.Scan0 + y * od.Stride, od.Stride);
                }
            }
            finally { o.UnlockBits(od); }
            return o;
        }

        // writes to a temp name first so a crash never leaves a half-written file behind
        public static long SavePng(Image img, string path)
        {
            string tmp = path + ".tmp";
            img.Save(tmp, ImageFormat.Png);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return new FileInfo(path).Length;
        }

        public static Bitmap Thumb(Image src, int maxW, int maxH)
        {
            double k = Math.Min(1.0, Math.Min((double)maxW / src.Width, (double)maxH / src.Height));
            int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
            var t = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return t;
        }

        // Bitmap -> CF_DIB bytes (bottom-up; 24-bit if opaque, else 32-bit BI_RGB with alpha)
        public static byte[] ToDib(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            bool alpha = Image.IsAlphaPixelFormat(b.PixelFormat);
            int bpp = alpha ? 32 : 24;
            var fmt = alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;
            int stride = ((w * bpp / 8) + 3) & ~3;
            var dib = new byte[40 + stride * h];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(w).CopyTo(dib, 4);
            BitConverter.GetBytes(h).CopyTo(dib, 8);                 // positive = bottom-up
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);
            BitConverter.GetBytes((short)bpp).CopyTo(dib, 14);
            BitConverter.GetBytes(stride * h).CopyTo(dib, 20);
            BitConverter.GetBytes(3780).CopyTo(dib, 24);             // 96 dpi
            BitConverter.GetBytes(3780).CopyTo(dib, 28);
            var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, fmt);
            try
            {
                int rowBytes = Math.Min(stride, Math.Abs(bd.Stride));
                for (int y = 0; y < h; y++)
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, dib, 40 + (h - 1 - y) * stride, rowBytes);
            }
            finally { b.UnlockBits(bd); }
            return dib;
        }
    }
}
