using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Pulp
{
    // IBM Plex Sans + IBM Plex Sans Arabic ship inside Pulp.exe (src/fonts, SIL Open Font License).
    // They are loaded for this process only, never installed on the PC.
    // Two registrations are needed: GDI+ (Font objects) uses the PrivateFontCollection,
    // and GDI (TextRenderer, TextBox) finds the same fonts by name through AddFontMemResourceEx.
    static class Fonts
    {
        public const string Sans = "IBM Plex Sans", SansSemi = "IBM Plex Sans SmBld",
            Arabic = "IBM Plex Sans Arabic", ArabicSemi = "IBM Plex Sans Arabic SmBld";
        public static readonly string[] Files = {
            "IBMPlexSans-Regular.ttf", "IBMPlexSans-SemiBold.ttf",
            "IBMPlexSansArabic-Regular.ttf", "IBMPlexSansArabic-SemiBold.ttf" };

        static readonly PrivateFontCollection pfc = new PrivateFontCollection();
        static bool loaded;

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var asm = typeof(Fonts).Assembly;
            foreach (var name in Files)
            {
                try
                {
                    byte[] data;
                    using (var s = asm.GetManifestResourceStream("Pulp.Fonts." + name))
                    {
                        if (s == null) continue;
                        using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    }
                    // GDI+ needs the memory to stay alive for the life of the process: never freed on purpose
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, mem, data.Length);
                    pfc.AddMemoryFont(mem, data.Length);
                    try { AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, out _); }
                    catch (DllNotFoundException) { }      // not Windows (tests)
                    catch (EntryPointNotFoundException) { }
                }
                catch { }   // a font that fails to load just falls back to Segoe UI
            }
        }

        // the bundled family with this name, or null if it didn't load
        public static FontFamily Family(string name)
        {
            Load();
            foreach (var f in pfc.Families) if (f.Name == name) return f;
            return null;
        }

        public static Font Make(string name, float size, string fallback, FontStyle fallbackStyle = FontStyle.Regular)
        {
            var fam = Family(name);
            if (fam != null && fam.IsStyleAvailable(FontStyle.Regular)) return new Font(fam, size, FontStyle.Regular, GraphicsUnit.Point);
            return new Font(fallback, size, fallbackStyle);
        }
    }
}
