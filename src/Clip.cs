using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Pulp
{
    // what was on the clipboard at one moment (raw bytes; decoding happens off the UI thread)
    class Snapshot
    {
        public string Text;
        public byte[] Html, Rtf;
        public string[] Files;
        public byte[] Png, Dib;
        public string AppName, Exe;
        public string Original;   // link before cleaning
        public uint Seq;
    }

    enum ReadResult { Ok, Busy, Skip, Empty }

    static class Clip
    {
        public static readonly uint FmtHtml = Native.RegisterClipboardFormat("HTML Format");
        public static readonly uint FmtRtf = Native.RegisterClipboardFormat("Rich Text Format");
        public static readonly uint FmtPng = Native.RegisterClipboardFormat("PNG");
        public static readonly uint FmtExclude = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        public static readonly uint FmtCanInclude = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
        public static readonly uint FmtViewerIgnore = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
        public static readonly uint FmtDropEffect = Native.RegisterClipboardFormat("Preferred DropEffect");

        public const int MaxTextChars = 4 * 1024 * 1024;
        const int MaxRich = 2 * 1024 * 1024;
        const int MaxImageBytes = 260 * 1024 * 1024;
        const int RichTextLimit = 200 * 1024;

        // clipboard memory is often larger than the data; HTML/RTF end at the first NUL
        static byte[] TrimAtNull(byte[] b)
        {
            if (b == null) return null;
            int i = Array.IndexOf(b, (byte)0);
            if (i < 0) return b;
            if (i == 0) return null;
            var r = new byte[i];
            Buffer.BlockCopy(b, 0, r, 0, i);
            return r;
        }

        static bool Avail(uint f) => f != 0 && Native.IsClipboardFormatAvailable(f);

        // ------------------------------------------------------------ source app
        static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SourceApp(out string exe, out string name)
        {
            string path = Native.ProcessPathOf(Native.GetClipboardOwner());
            if (path == null) path = Native.ProcessPathOf(Native.GetForegroundWindow());
            exe = path == null ? null : Path.GetFileName(path);
            name = null;
            if (path == null) return;
            lock (nameCache)
            {
                if (nameCache.TryGetValue(path, out name)) return;
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    name = vi.FileDescription;
                    if (!string.IsNullOrWhiteSpace(vi.ProductName) && (string.IsNullOrWhiteSpace(name) || name.Length > 28 || name.IndexOf(' ') < 0 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                        name = vi.ProductName;
                }
                catch { }
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
                name = name.Trim();
                if (name.Length > 32) name = name.Substring(0, 32);
                nameCache[path] = name;
            }
        }

        // friendly name of the app that owns a window ("Discord", "Google Chrome"), or null
        public static string AppNameOf(IntPtr hwnd)
        {
            string path = Native.ProcessPathOf(hwnd);
            if (path == null) return null;
            lock (nameCache) if (nameCache.TryGetValue(path, out string n)) return n;
            string name = null;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                name = vi.FileDescription;
                if (!string.IsNullOrWhiteSpace(vi.ProductName) && (string.IsNullOrWhiteSpace(name) || name.Length > 28)) name = vi.ProductName;
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
            name = name.Trim();
            if (name.Length > 32) name = name.Substring(0, 32);
            lock (nameCache) nameCache[path] = name;
            return name;
        }

        // ------------------------------------------------------------ read
        public static ReadResult Read(IntPtr hwnd, out Snapshot snap)
        {
            snap = null;
            // password managers and other apps mark private copies; these checks don't need the clipboard open
            if (Avail(FmtExclude) || Avail(FmtViewerIgnore)) return ReadResult.Skip;
            bool hasFiles = Avail(Native.CF_HDROP), hasText = Avail(Native.CF_UNICODETEXT);
            bool hasPng = Avail(FmtPng), hasDib = Avail(Native.CF_DIB) || Avail(Native.CF_DIBV5) || Avail(Native.CF_BITMAP);
            if (!hasFiles && !hasText && !hasPng && !hasDib) return ReadResult.Empty;

            var s = new Snapshot { Seq = Native.GetClipboardSequenceNumber() };
            SourceApp(out s.Exe, out s.AppName);

            if (!Native.OpenClipboard(hwnd)) return ReadResult.Busy;
            try
            {
                if (Avail(FmtCanInclude))
                {
                    var b = Bytes(FmtCanInclude, 64);
                    if (b != null && b.Length >= 4 && BitConverter.ToInt32(b, 0) == 0) return ReadResult.Skip;
                }
                if (hasFiles)
                {
                    s.Files = ReadFiles();
                    if (s.Files != null && s.Files.Length > 0) { snap = s; return ReadResult.Ok; }
                }
                if (hasText)
                {
                    s.Text = ReadText(out bool tooBig);
                    if (tooBig) return ReadResult.Skip;
                    if (string.IsNullOrWhiteSpace(s.Text)) s.Text = null;
                }
                // a browser's "Copy image" can come with the image's URL as text: the image is what was meant
                bool textWins = s.Text != null && !((hasPng || hasDib) && Links.IsSingleUrl(s.Text));
                if (textWins)
                {
                    // formatting is rendered on demand by the source app; for huge copies (a big Excel range)
                    // that can take seconds, so only ask for it when the text is a sane size
                    if (s.Text.Length <= RichTextLimit)
                    {
                        if (Avail(FmtHtml)) s.Html = TrimAtNull(Bytes(FmtHtml, MaxRich));
                        if (Avail(FmtRtf)) s.Rtf = TrimAtNull(Bytes(FmtRtf, MaxRich));
                    }
                    snap = s;
                    return ReadResult.Ok;
                }
                if (hasPng) s.Png = Bytes(FmtPng, MaxImageBytes);
                if (s.Png == null && hasDib) s.Dib = Bytes(Native.CF_DIB, MaxImageBytes);
                if (s.Png != null || s.Dib != null) { s.Text = null; snap = s; return ReadResult.Ok; }
                if (s.Text != null) { snap = s; return ReadResult.Ok; }
                return ReadResult.Empty;
            }
            finally { Native.CloseClipboard(); }
        }

        static byte[] Bytes(uint fmt, int max)
        {
            IntPtr h = Native.GetClipboardData(fmt);
            if (h == IntPtr.Zero) return null;
            long size = (long)Native.GlobalSize(h).ToUInt64();
            if (size <= 0 || size > max) return null;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try
            {
                var b = new byte[size];
                Marshal.Copy(p, b, 0, (int)size);
                return b;
            }
            finally { Native.GlobalUnlock(h); }
        }

        static unsafe string ReadText(out bool tooBig)
        {
            tooBig = false;
            IntPtr h = Native.GetClipboardData(Native.CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            long chars = (long)Native.GlobalSize(h).ToUInt64() / 2;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try
            {
                char* c = (char*)p;
                long n = 0;
                while (n < chars && c[n] != '\0') n++;
                if (n > MaxTextChars) { tooBig = true; return null; }
                return new string(c, 0, (int)n);
            }
            finally { Native.GlobalUnlock(h); }
        }

        static string[] ReadFiles()
        {
            IntPtr h = Native.GetClipboardData(Native.CF_HDROP);
            if (h == IntPtr.Zero) return null;
            uint n = Native.DragQueryFile(h, 0xFFFFFFFF, null, 0);
            var list = new List<string>();
            for (uint i = 0; i < n && i < 5000; i++)
            {
                uint len = Native.DragQueryFile(h, i, null, 0);
                var sb = new StringBuilder((int)len + 1);
                Native.DragQueryFile(h, i, sb, len + 1);
                if (sb.Length > 0) list.Add(sb.ToString());
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------ write
        static IntPtr Global(byte[] data)
        {
            IntPtr h = Native.GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)(uint)Math.Max(1, data.Length));
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) { Native.GlobalFree(h); return IntPtr.Zero; }
            Marshal.Copy(data, 0, p, data.Length);
            Native.GlobalUnlock(h);
            return h;
        }

        static bool Put(uint fmt, byte[] data)
        {
            if (data == null) return false;
            IntPtr h = Global(data);
            if (h == IntPtr.Zero) return false;
            if (Native.SetClipboardData(fmt, h) == IntPtr.Zero) { Native.GlobalFree(h); return false; }
            return true;
        }

        static byte[] Utf16z(string s) => Encoding.Unicode.GetBytes(s + "\0");

        static byte[] DropFiles(string[] paths)
        {
            var body = Encoding.Unicode.GetBytes(string.Join("\0", paths) + "\0\0");
            var b = new byte[20 + body.Length];
            BitConverter.GetBytes(20).CopyTo(b, 0);   // pFiles
            BitConverter.GetBytes(1).CopyTo(b, 16);   // fWide
            Buffer.BlockCopy(body, 0, b, 20, body.Length);
            return b;
        }

        static bool Open(IntPtr hwnd)
        {
            // the clipboard is often held for a moment by whoever wrote last
            for (int i = 0; i < 12; i++)
            {
                if (Native.OpenClipboard(hwnd)) return true;
                Thread.Sleep(Math.Min(60, 8 + i * 6));
            }
            return false;
        }

        public class Payload
        {
            public string Text;
            public byte[] Html, Rtf, Png, Dib;
            public string[] Files;
        }

        // returns the clipboard sequence number after writing (0 on failure)
        // expectSeq: only write if the clipboard still holds what we read (0 = always write)
        public static uint Write(IntPtr hwnd, Payload p, uint expectSeq = 0)
        {
            if (!Open(hwnd)) return 0;
            bool ok = false;
            try
            {
                if (expectSeq != 0 && Native.GetClipboardSequenceNumber() != expectSeq) return 0;
                if (!Native.EmptyClipboard()) return 0;
                if (p.Files != null && p.Files.Length > 0)
                {
                    ok = Put(Native.CF_HDROP, DropFiles(p.Files));
                    Put(FmtDropEffect, BitConverter.GetBytes(1)); // DROPEFFECT_COPY
                }
                if (p.Text != null) ok |= Put(Native.CF_UNICODETEXT, Utf16z(p.Text));
                if (p.Html != null) Put(FmtHtml, p.Html);
                if (p.Rtf != null) Put(FmtRtf, p.Rtf);
                if (p.Png != null) ok |= Put(FmtPng, p.Png);
                if (p.Dib != null) ok |= Put(Native.CF_DIB, p.Dib);
            }
            finally { Native.CloseClipboard(); }
            return ok ? Native.GetClipboardSequenceNumber() : 0;
        }
    }
}
