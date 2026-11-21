using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Pulp
{
    // ---------------------------------------------------------------- small dark controls
    static class Ux
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        public static void DarkScrollbars(Control c)
        {
            c.HandleCreated += (o, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        }
    }

    class DarkBox : Panel
    {
        public readonly TextBox Box;
        readonly float s;
        public DarkBox(float scale, bool multiline = false, bool mono = false)
        {
            s = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Padding = multiline ? new Padding(S(10), S(8), S(4), S(8)) : new Padding(S(10), S(7), S(10), S(5));
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text,
                Font = mono ? new Font("Consolas", 10f) : Theme.Ui(10f),
                Multiline = multiline, AcceptsReturn = multiline, WordWrap = false, Dock = DockStyle.Fill,
                ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
            };
            if (multiline) Ux.DarkScrollbars(Box);
            Box.GotFocus += (o, e) => Invalidate();
            Box.LostFocus += (o, e) => Invalidate();
            Controls.Add(Box);
        }
        int S(float v) => (int)Math.Round(v * s);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(e.Graphics, r, Box.ReadOnly && Box.Multiline ? Color.FromArgb(38, 38, 38) : Theme.Field, S(6));
            Theme.Stroke(e.Graphics, r, Box.Focused ? Theme.Accent : Theme.Border, S(6), Math.Max(1, s));
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
    }

    class Toggle : Control
    {
        bool on;
        readonly float s;
        public event Action Changed;
        public Toggle(string text, float scale)
        {
            s = scale;
            Text = text;
            Font = Theme.Ui(10f);
            ForeColor = Theme.Text;
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = S(28);
        }
        int S(float v) => (int)Math.Round(v * s);
        public bool On { get => on; set { if (on != value) { on = value; Invalidate(); } } }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); On = !On; Changed?.Invoke(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { On = !On; Changed?.Invoke(); e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var pill = new RectangleF(S(1), (Height - S(20)) / 2f, S(40), S(20));
            if (on) Theme.Fill(g, pill, Enabled ? Theme.Accent : Theme.Dim, pill.Height / 2);
            else Theme.Stroke(g, pill, Focused ? Theme.Text : Theme.Sub, pill.Height / 2, Math.Max(1, s));
            float d = on ? S(14) : S(12);
            float kx = on ? pill.Right - S(3) - d : pill.X + S(4);
            using (var b = new SolidBrush(on ? Color.FromArgb(30, 30, 30) : Theme.Sub))
                g.FillEllipse(b, kx, pill.Y + (pill.Height - d) / 2, d, d);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(S(52), 0, Width - S(52), Height), Enabled ? ForeColor : Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    class FlatBtn : Control
    {
        public bool Primary, Danger, Selected;
        bool hover, down;
        readonly float s;
        public FlatBtn(string text, float scale, bool primary = false)
        {
            s = scale;
            Text = text;
            Primary = primary;
            Font = primary ? Theme.UiSemi(10f) : Theme.Ui(10f);
            Cursor = Cursors.Hand;
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(S(96), S(32));
        }
        int S(float v) => (int)Math.Round(v * s);
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, fg;
            if (Primary) { bg = hover ? Theme.AccentHover : Theme.Accent; fg = Color.FromArgb(28, 20, 14); }
            else if (Selected) { bg = Theme.CardHover; fg = Theme.Text; }
            else { bg = down ? Theme.CardHover : hover ? Color.FromArgb(52, 52, 52) : Theme.Card; fg = Danger ? Color.FromArgb(255, 140, 140) : Theme.Text; }
            Theme.Fill(g, r, bg, S(6));
            if (Focused && !Primary) Theme.Stroke(g, r, Theme.Sub, S(6), Math.Max(1, s));
            TextRenderer.DrawText(g, Text, Font, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    // ---------------------------------------------------------------- the window
    class SettingsForm : Form
    {
        readonly App app;
        readonly float s;
        readonly string[] pages = { "General", "History", "Link cleaner", "Ignored apps" };
        readonly List<Panel> pagePanels = new List<Panel>();
        readonly Font fBody, fSmall, fTitle, fNav;
        int page, hoverNav = -1;
        readonly Label status;

        // controls
        DarkBox hotkeyBox, maxItems, imageCap, keepFmt, rules, tryLink, ignored;
        Toggle tPaste, tHide, tStart, tPaused, tImages, tClean;
        Label imageUsage, tryResult, rulesErrors;
        FlatBtn rulesMine, rulesBuiltIn;
        ComboBox runningApps;
        Hotkey pendingHotkey;
        bool hotkeySuspended, showingBuiltIn;
        string userRulesText;

        int S(float v) => (int)Math.Round(v * s);
        int NavW => S(176);
        int W => S(700);
        int H => S(590);
        Rectangle Content => new Rectangle(NavW + S(24), S(20), W - NavW - S(48), H - S(90));

        public SettingsForm(App a, string startPage)
        {
            app = a;
            Text = "Pulp settings";
            Icon = app.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            ClientSize = new Size(W, H);
            fBody = Theme.Ui(10f);
            fSmall = Theme.Ui(9f);
            fTitle = Theme.UiTitle(15f);
            fNav = Theme.Ui(10.5f);

            BuildGeneral();
            BuildHistory();
            BuildLinks();
            BuildIgnored();

            var save = new FlatBtn("Save", s, true) { Location = new Point(W - S(24) - S(96), H - S(52)) };
            save.Click += (o, e) => Save();
            var cancel = new FlatBtn("Cancel", s) { Location = new Point(save.Left - S(104), save.Top) };
            cancel.Click += (o, e) => Close();
            status = new Label { AutoSize = false, Location = new Point(NavW + S(24), save.Top), Size = new Size(cancel.Left - NavW - S(36), save.Height), ForeColor = Theme.Red, Font = fSmall, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Bg };
            Controls.Add(save);
            Controls.Add(cancel);
            Controls.Add(status);
            AcceptButton = null;
            CancelButton = null;

            int start = Array.IndexOf(pages, startPage ?? "");
            ShowPage(start < 0 ? 0 : start);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
            if (hotkeyBox.Box.Focused) Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && !(hotkeyBox != null && hotkeyBox.Box.Focused)) { Close(); e.Handled = true; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (hotkeySuspended) { app.ResumeHotkey(); hotkeySuspended = false; }
        }

        // ------------------------------------------------------------ layout helpers
        Panel NewPage(string title, string subtitle)
        {
            var c = Content;
            var p = new Panel { Location = c.Location, Size = c.Size, BackColor = Theme.Bg, Visible = false };
            p.Controls.Add(new Label { Text = title, Font = fTitle, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 0), BackColor = Theme.Bg });
            if (subtitle != null)
                p.Controls.Add(new Label { Text = subtitle, Font = fSmall, ForeColor = Theme.Sub, AutoSize = false, Size = new Size(c.Width, S(36)), Location = new Point(0, S(34)), BackColor = Theme.Bg });
            Controls.Add(p);
            pagePanels.Add(p);
            return p;
        }

        Label Lbl(Panel p, string text, int y, Color? color = null, Font f = null, int h = 0, int x = 0, int w = 0)
        {
            var l = new Label { Text = text, Font = f ?? fBody, ForeColor = color ?? Theme.Text, BackColor = Theme.Bg, AutoSize = h == 0 && w == 0, Location = new Point(x, y) };
            if (h > 0 || w > 0) l.Size = new Size(w > 0 ? w : p.Width - x, h > 0 ? h : S(20));
            p.Controls.Add(l);
            return l;
        }

        Toggle AddToggle(Panel p, string text, int y, bool value)
        {
            var t = new Toggle(text, s) { Location = new Point(0, y), Width = p.Width, On = value };
            p.Controls.Add(t);
            return t;
        }

        DarkBox Box(Panel p, int x, int y, int w, int h, bool multiline = false, bool mono = false)
        {
            var b = new DarkBox(s, multiline, mono) { Location = new Point(x, y), Size = new Size(w, h) };
            p.Controls.Add(b);
            return b;
        }

        DarkBox Number(Panel p, int x, int y, int value)
        {
            var b = Box(p, x, y, S(96), S(32));
            b.Box.Text = value.ToString();
            b.Box.TextAlign = HorizontalAlignment.Right;
            b.Box.KeyPress += (o, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            return b;
        }

        // "label [ 123 ] suffix", placed by measured text width
        DarkBox LabeledNumber(Panel p, string label, string suffix, int y, int value)
        {
            var l = Lbl(p, label, y + S(6));
            int w = TextRenderer.MeasureText(label, fBody).Width;
            var box = Number(p, w + S(10), y, value);
            Lbl(p, suffix, y + S(6), x: box.Right + S(10));
            return box;
        }

        FlatBtn Btn(Panel p, string text, int x, int y, int w, Action click)
        {
            var b = new FlatBtn(text, s) { Location = new Point(x, y), Size = new Size(w, S(32)) };
            b.Click += (o, e) => click();
            p.Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------ pages
        void BuildGeneral()
        {
            var p = NewPage("General", null);
            int y = S(52);
            Lbl(p, "Shortcut that opens Pulp", y);
            hotkeyBox = Box(p, 0, y + S(26), S(220), S(34));
            var hb = hotkeyBox.Box;
            hb.ReadOnly = true;
            hb.TabStop = false;          // capture starts only when clicked, never on its own
            hb.ShortcutsEnabled = false;
            hb.BackColor = Theme.Field;
            hb.Cursor = Cursors.Hand;
            pendingHotkey = app.S.Hotkey;
            hb.Text = pendingHotkey.ToString();
            var hint = Lbl(p, "Click the box, then press the new shortcut (use Ctrl, Alt or Win with a key).", y + S(30), Theme.Dim, fSmall, x: S(236), w: p.Width - S(236), h: S(34));
            hb.GotFocus += (o, e) => { if (!hotkeySuspended) { app.SuspendHotkey(); hotkeySuspended = true; } hint.Text = "Press the shortcut now… (Esc to cancel)"; hint.ForeColor = Theme.Accent; };
            hb.LostFocus += (o, e) => { if (hotkeySuspended) { app.ResumeHotkey(); hotkeySuspended = false; } hint.Text = "Click the box, then press the new shortcut (use Ctrl, Alt or Win with a key)."; hint.ForeColor = Theme.Dim; };
            hb.PreviewKeyDown += (o, e) => e.IsInputKey = true;
            hb.KeyDown += (o, e) =>
            {
                e.Handled = true; e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { hb.Text = pendingHotkey.ToString(); ActiveControl = null; return; }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
                bool win = Native.KeyDown(0x5B) || Native.KeyDown(0x5C);
                if (!e.Control && !e.Alt && !win) { hint.Text = "Add Ctrl, Alt or Win so normal typing isn't caught."; hint.ForeColor = Theme.Red; return; }
                pendingHotkey = new Hotkey(e.KeyCode, e.Control, e.Alt, e.Shift, win);
                hb.Text = pendingHotkey.ToString();
                hint.Text = "Got it. Press Save to use " + pendingHotkey + ".";
                hint.ForeColor = Theme.Sub;
            };
            y += S(84);
            tPaste = AddToggle(p, "Paste automatically when I press Enter", y, app.S.PasteOnEnter);
            Lbl(p, "Off: Enter only copies, and you paste with Ctrl+V yourself.", y + S(26), Theme.Dim, fSmall, x: S(52), h: S(18));
            y += S(18);
            y += S(36);
            tHide = AddToggle(p, "Hide Pulp after pasting", y, app.S.HideAfterPaste);
            y += S(36);
            tStart = AddToggle(p, "Start Pulp with Windows", y, Settings.StartWithWindows);
            y += S(36);
            tPaused = AddToggle(p, "Pause recording", y, app.S.Paused);
            y += S(56);
            Lbl(p, "Windows' own clipboard history (Win+V)", y, Theme.Text);
            Lbl(p, "Pulp leaves Win+V alone. If you only want Pulp, turn off \"Clipboard history\" in Windows Settings > System > Clipboard.", y + S(24), Theme.Sub, fSmall, h: S(36));
            Btn(p, "Open Windows clipboard settings", 0, y + S(64), S(250), () => { try { Process.Start(new ProcessStartInfo("ms-settings:clipboard") { UseShellExecute = true }); } catch { } });
        }

        void BuildHistory()
        {
            var p = NewPage("History", "Pinned items are never removed. Lowering a limit removes the oldest unpinned items when you save.");
            int y = S(80);
            maxItems = LabeledNumber(p, "Keep up to", "text, link and file items", y, app.S.MaxItems);
            y += S(48);
            tImages = AddToggle(p, "Keep screenshots and images", y, app.S.SaveImages);
            y += S(44);
            imageCap = LabeledNumber(p, "Image storage limit", "MB", y, app.S.ImageCapMB);
            imageUsage = Lbl(p, "", y + S(40), Theme.Dim, fSmall, h: S(18));
            y += S(66);
            keepFmt = LabeledNumber(p, "Keep formatting for the newest", "items", y, app.S.KeepFormatting);
            Lbl(p, "Bold, colours and links are kept when those items are pasted. Older items paste as plain text.", y + S(40), Theme.Dim, fSmall, h: S(34));
            y += S(80);
            Btn(p, "Open data folder", 0, y, S(150), app.OpenDataFolder);
            var clear = Btn(p, "Clear history…", S(160), y, S(140), () => { app.ClearHistoryFrom(this); UpdateUsage(); });
            clear.Danger = true;
            UpdateUsage();
        }

        void UpdateUsage()
        {
            try
            {
                app.Store.ImageUsage(out long n, out long bytes);
                imageUsage.Text = "Now using " + PulpPanel.FmtSize(bytes) + " for " + n.ToString("N0") + (n == 1 ? " image" : " images");
            }
            catch { }
        }

        void BuildLinks()
        {
            var p = NewPage("Link cleaner", null);
            int y = S(46);
            tClean = AddToggle(p, "Clean links when I copy them", y, app.S.CleanLinks);
            Lbl(p, "Removes tracking, shortens Amazon and noon product links, unwraps redirect links.", y + S(26), Theme.Dim, fSmall, x: S(52), h: S(18));
            y += S(56);
            rulesMine = Btn(p, "Your rules", 0, y, S(110), () => ShowRules(false));
            rulesBuiltIn = Btn(p, "Built-in rules", S(116), y, S(120), () => ShowRules(true));
            Lbl(p, "Saved in link-rules.txt", y + S(8), Theme.Dim, fSmall, x: S(250), w: p.Width - S(250));
            y += S(40);
            try { userRulesText = File.Exists(LinkCleaner.UserRulesPath) ? File.ReadAllText(LinkCleaner.UserRulesPath, Encoding.UTF8) : LinkCleaner.UserTemplate; }
            catch { userRulesText = LinkCleaner.UserTemplate; }
            rules = Box(p, 0, y, p.Width, S(186), true, true);
            rules.Box.Text = Crlf(userRulesText);
            rules.Box.TextChanged += (o, e) => { if (!showingBuiltIn) { userRulesText = rules.Box.Text; CheckRules(); } };
            y += S(192);
            rulesErrors = Lbl(p, "", y, Theme.Red, fSmall, h: S(18));
            y += S(22);
            Lbl(p, "Try a link", y);
            tryLink = Box(p, 0, y + S(24), p.Width, S(32));
            tryLink.Box.TextChanged += (o, e) => TryLink();
            tryResult = Lbl(p, "Paste a link to see what Pulp would do with it.", y + S(62), Theme.Dim, fSmall, h: S(40));
            ShowRules(false);
        }

        static string Crlf(string t) => (t ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        void ShowRules(bool builtIn)
        {
            showingBuiltIn = builtIn;
            rules.Box.ReadOnly = builtIn;
            rules.Box.Text = builtIn ? Crlf(LinkCleaner.BuiltIn) : Crlf(userRulesText);
            rules.Box.ForeColor = builtIn ? Theme.Sub : Theme.Text;
            rules.Box.BackColor = builtIn ? Color.FromArgb(38, 38, 38) : Theme.Field;
            rulesMine.Selected = !builtIn;
            rulesBuiltIn.Selected = builtIn;
            rulesMine.Invalidate(); rulesBuiltIn.Invalidate(); rules.Invalidate();
            CheckRules();
        }

        void CheckRules()
        {
            var lc = new LinkCleaner(userRulesText);
            var errs = lc.Errors.Where(x => !x.StartsWith("built-in")).ToList();
            rulesErrors.Text = errs.Count == 0 ? "" : "Can't read " + errs[0] + (errs.Count > 1 ? " (+" + (errs.Count - 1) + " more)" : "");
            TryLink();
        }

        void TryLink()
        {
            if (tryLink == null) return;
            string t = tryLink.Box.Text.Trim();
            if (t.Length == 0) { tryResult.Text = "Paste a link to see what Pulp would do with it."; tryResult.ForeColor = Theme.Dim; return; }
            if (!Links.IsSingleUrl(t)) { tryResult.Text = "That isn't a single http(s) link."; tryResult.ForeColor = Theme.Dim; return; }
            var lc = new LinkCleaner(userRulesText);
            string r = lc.Clean(t);
            if (r == null) { tryResult.Text = "No change: nothing Pulp knows to remove."; tryResult.ForeColor = Theme.Sub; return; }
            tryResult.Text = r + "\n" + lc.Describe(t);
            tryResult.ForeColor = Theme.Accent;
        }

        void BuildIgnored()
        {
            var p = NewPage("Ignored apps", "Pulp never records anything copied from these apps (one per line). Password managers that mark copies as private are skipped automatically as well.");
            int y = S(80);
            ignored = Box(p, 0, y, p.Width, S(250), true);
            ignored.Box.Text = string.Join("\r\n", app.S.IgnoreApps);
            y += S(262);
            Lbl(p, "Add an app that's running now", y);
            runningApps = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Field, ForeColor = Theme.Text,
                Font = fBody, Location = new Point(0, y + S(26)), Width = S(300), IntegralHeight = false, MaxDropDownItems = 14,
            };
            p.Controls.Add(runningApps);
            Btn(p, "Add", S(310), y + S(25), S(80), () =>
            {
                if (!(runningApps.SelectedItem is string exe)) return;
                var list = ignored.Box.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (!list.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase))) list.Add(exe);
                ignored.Box.Text = string.Join("\r\n", list);
            });
            runningApps.DropDown += (o, e) => FillRunning();
            FillRunning();
        }

        void FillRunning()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in Process.GetProcesses())
            {
                try
                {
                    if (pr.MainWindowHandle == IntPtr.Zero || pr.Id == Process.GetCurrentProcess().Id) continue;
                    names.Add(pr.ProcessName + ".exe");
                }
                catch { }
                finally { pr.Dispose(); }
            }
            object sel = runningApps.SelectedItem;
            runningApps.Items.Clear();
            foreach (var n in names) runningApps.Items.Add(n);
            if (sel != null && runningApps.Items.Contains(sel)) runningApps.SelectedItem = sel;
            else if (runningApps.Items.Count > 0) runningApps.SelectedIndex = 0;
        }

        // ------------------------------------------------------------ nav
        Rectangle NavItem(int i) => new Rectangle(S(10), S(70) + i * S(40), NavW - S(20), S(36));

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pagePanels.Count; k++) pagePanels[k].Visible = k == i;
            status.Text = "";
            if (i == 1) UpdateUsage();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            using (var b = new SolidBrush(Color.FromArgb(27, 27, 27))) g.FillRectangle(b, 0, 0, NavW, H);
            Theme.DrawLogo(g, new RectangleF(S(18), S(20), S(30), S(30)));
            TextRenderer.DrawText(g, "Pulp", Theme.UiTitle(13f), new Rectangle(S(56), S(18), S(110), S(34)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            for (int i = 0; i < pages.Length; i++)
            {
                var r = NavItem(i);
                if (i == page) Theme.Fill(g, r, Theme.Card, S(6));
                else if (i == hoverNav) Theme.Fill(g, r, Color.FromArgb(36, 36, 36), S(6));
                if (i == page) Theme.Fill(g, new Rectangle(r.X, r.Y + S(9), S(3), r.Height - S(18)), Theme.Accent, S(1.5f));
                TextRenderer.DrawText(g, pages[i], fNav, new Rectangle(r.X + S(14), r.Y, r.Width - S(14), r.Height), i == page ? Theme.Text : Theme.Sub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "Version " + Application.ProductVersion, fSmall, new Rectangle(S(18), H - S(40), NavW - S(20), S(20)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < pages.Length; i++) if (NavItem(i).Contains(e.Location)) h = i;
            if (h != hoverNav) { hoverNav = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hoverNav != -1) { hoverNav = -1; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < pages.Length; i++) if (NavItem(i).Contains(e.Location)) ShowPage(i);
        }

        // ------------------------------------------------------------ save
        bool ReadNumber(DarkBox b, int min, int max, string what, int pageIndex, out int value)
        {
            if (int.TryParse(b.Box.Text.Trim(), out value) && value >= min && value <= max) return true;
            ShowPage(pageIndex);
            status.Text = what + " must be between " + min.ToString("N0") + " and " + max.ToString("N0") + ".";
            b.Box.Focus();
            return false;
        }

        void Save()
        {
            if (!ReadNumber(maxItems, 100, 10000000, "Items to keep", 1, out int mi)) return;
            if (!ReadNumber(imageCap, 50, 1000000, "Image limit", 1, out int cap)) return;
            if (!ReadNumber(keepFmt, 0, 1000000, "Formatting count", 1, out int kf)) return;
            var errs = new LinkCleaner(userRulesText).Errors.Where(x => !x.StartsWith("built-in")).ToList();
            if (errs.Count > 0) { ShowPage(2); ShowRules(false); status.Text = "Fix the link rule marked in red first."; return; }

            if (hotkeySuspended) { app.ResumeHotkey(); hotkeySuspended = false; }
            if (pendingHotkey.ToString() != app.S.Hotkey.ToString() && !app.ApplyHotkey(pendingHotkey))
            {
                ShowPage(0);
                status.Text = pendingHotkey + " is already used by another app. Pick a different shortcut.";
                return;
            }

            var st = app.S;
            st.PasteOnEnter = tPaste.On;
            st.HideAfterPaste = tHide.On;
            Settings.StartWithWindows = tStart.On;
            st.MaxItems = mi;
            st.ImageCapMB = cap;
            st.KeepFormatting = kf;
            st.SaveImages = tImages.On;
            st.CleanLinks = tClean.On;
            st.IgnoreApps = ignored.Box.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            app.SaveRules(userRulesText);
            app.SetPaused(tPaused.On);
            app.SettingsChanged();
            Close();
        }
    }
}
