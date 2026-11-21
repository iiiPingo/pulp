using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pulp
{
    class Settings
    {
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulp");
        static string FilePath => Path.Combine(Dir, "settings.ini");

        public Hotkey Hotkey = new Hotkey(Keys.V, true, true, false, false);
        public bool Paused;
        public bool SaveImages = true;
        public bool CleanLinks = true;
        public bool PasteOnEnter = true;
        public bool HideAfterPaste = false;
        public int WinX, WinY, WinW, WinH;     // window position/size (0 = not placed yet)
        public int MaxItems = 100000;          // text/link/file items kept (pinned don't count)
        public int ImageCapMB = 2048;          // screenshots/images on disk (pinned never removed)
        public int KeepFormatting = 1000;      // newest items that keep HTML/RTF formatting
        public List<string> IgnoreApps = new List<string> { "KeePass.exe", "KeePassXC.exe", "1Password.exe", "Bitwarden.exe", "Dashlane.exe", "NordPass.exe", "Proton Pass.exe" };
        public string SaveFolder = "";
        public bool FirstRun;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) { s.FirstRun = true; return s; }
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.TrimStart().StartsWith(";")) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "hotkey": var h = Hotkey.Parse(v); if (h != null) s.Hotkey = h; break;
                        case "paused": s.Paused = v == "1"; break;
                        case "saveimages": s.SaveImages = v != "0"; break;
                        case "cleanlinks": s.CleanLinks = v != "0"; break;
                        case "pasteonenter": s.PasteOnEnter = v != "0"; break;
                        case "hideafterpaste": s.HideAfterPaste = v == "1"; break;
                        case "window":
                            var w = v.Split(',');
                            if (w.Length == 4 && int.TryParse(w[0], out int wx) && int.TryParse(w[1], out int wy) && int.TryParse(w[2], out int ww) && int.TryParse(w[3], out int wh))
                            { s.WinX = wx; s.WinY = wy; s.WinW = ww; s.WinH = wh; }
                            break;
                        case "maxitems": if (int.TryParse(v, out int m) && m >= 100) s.MaxItems = m; break;
                        case "imagecapmb": if (int.TryParse(v, out int c) && c >= 50) s.ImageCapMB = c; break;
                        case "keepformatting": if (int.TryParse(v, out int f) && f >= 0) s.KeepFormatting = f; break;
                        case "ignoreapps": s.IgnoreApps = v.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); break;
                        case "savefolder": s.SaveFolder = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, string.Join("\r\n", new[]
                {
                    "; Pulp settings (also editable from the Settings window)",
                    "hotkey=" + Hotkey,
                    "paused=" + B(Paused),
                    "saveimages=" + B(SaveImages),
                    "cleanlinks=" + B(CleanLinks),
                    "pasteonenter=" + B(PasteOnEnter),
                    "hideafterpaste=" + B(HideAfterPaste),
                    "window=" + WinX + "," + WinY + "," + WinW + "," + WinH,
                    "maxitems=" + MaxItems,
                    "imagecapmb=" + ImageCapMB,
                    "keepformatting=" + KeepFormatting,
                    "ignoreapps=" + string.Join(";", IgnoreApps),
                    "savefolder=" + SaveFolder,
                }) + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
        static string B(bool b) => b ? "1" : "0";

        public bool IsIgnored(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            string bare = Path.GetFileNameWithoutExtension(exe);
            foreach (var a in IgnoreApps)
            {
                string x = a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a.Substring(0, a.Length - 4) : a;
                if (string.Equals(x, bare, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool StartWithWindows
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("Pulp") != null; } catch { return false; } }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue("Pulp", "\"" + Application.ExecutablePath + "\" --tray");
                        else k.DeleteValue("Pulp", false);
                    }
                }
                catch { }
            }
        }
    }

    class Hotkey
    {
        public Keys Key;
        public bool Ctrl, Alt, Shift, Win;
        public Hotkey(Keys k, bool ctrl, bool alt, bool shift, bool win) { Key = k; Ctrl = ctrl; Alt = alt; Shift = shift; Win = win; }

        public int Modifiers => (Alt ? 1 : 0) | (Ctrl ? 2 : 0) | (Shift ? 4 : 0) | (Win ? 8 : 0);

        public static Hotkey Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var h = new Hotkey(Keys.None, false, false, false, false);
            foreach (var raw in s.Split('+'))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (t == "ctrl" || t == "control") h.Ctrl = true;
                else if (t == "alt") h.Alt = true;
                else if (t == "shift") h.Shift = true;
                else if (t == "win") h.Win = true;
                else if (t.Length == 1 && char.IsLetterOrDigit(t[0])) h.Key = char.IsDigit(t[0]) ? Keys.D0 + (t[0] - '0') : (Keys)char.ToUpperInvariant(t[0]);
                else if (Enum.TryParse(raw.Trim(), true, out Keys k)) h.Key = k;
                else return null;
            }
            if (h.Key == Keys.None || h.Modifiers == 0) return null;
            return h;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            string k = Key.ToString();
            if (Key >= Keys.D0 && Key <= Keys.D9) k = ((char)('0' + (Key - Keys.D0))).ToString();
            parts.Add(k);
            return string.Join("+", parts);
        }
    }
}
