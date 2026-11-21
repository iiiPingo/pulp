using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pulp
{
    class PulpPanel : Form
    {
        class Hit { public Rectangle R; public string Key; public Action Click; public Action<Point> Right; public Item Row; }

        readonly App app;
        readonly float s;
        readonly TextBox search;
        readonly Font fBody, fSmall, fChip, fChipSel, fIcon, fIconSmall, fIconRow;
        readonly List<Hit> hits = new List<Hit>();
        readonly Timer searchTimer;
        readonly ThumbCache thumbs;
        readonly ToolTip tip = new ToolTip { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 4000 };

        List<Item> rows = new List<Item>();
        readonly List<int> tops = new List<int>();
        int totalH, scrollY, sel, gen;
        // smooth scrolling: the wheel moves scrollGoal, scrollAnim eases scrollY toward it (~60 fps)
        float scrollPos, scrollGoal;
        readonly Timer scrollAnim = new Timer { Interval = 15 };
        bool more, loadingMore;
        long count;
        string hover, query = "";
        Point downPt;
        Item downItem;

        public IntPtr Target;
        public bool SuppressHide;
        public DateTime LastHidden = DateTime.MinValue;

        const int Page = 80;
        int S(float v) => (int)Math.Round(v * s);
        int W => ClientSize.Width;
        int Ht => ClientSize.Height;
        int Pad => S(12);
        int ListTop => S(104);
        int FooterH => S(36);
        int ListH => Math.Max(S(60), Ht - ListTop - FooterH);
        Rectangle SearchField => new Rectangle(Pad, S(12), W - Pad * 2 - S(44), S(40));
        Rectangle MenuButton => new Rectangle(W - Pad - S(36), S(16), S(34), S(34));
        Rectangle ListRect => new Rectangle(0, ListTop, W, ListH);
        int RowH(Item it) => it.Kind == Kind.Image ? S(92) : S(64);

        // quick filters shown as buttons under the search box (Ctrl+1..6 or Tab to switch)
        static readonly string[] FilterNames = { "All", "Text", "Links", "Images", "Files", "Pinned" };
        static readonly string[] FilterWords = { "", "is:text", "is:link", "is:image", "is:file", "is:pinned" };
        int filter;
        string EffectiveQuery => (FilterWords[filter] + " " + query).Trim();
        bool Filtering => filter != 0 || query.Length > 0;
        string targetName;
        IntPtr targetNameFor;
        DateTime lastDeactivated = DateTime.MinValue;
        public bool AllowClose;

        public PulpPanel(App a)
        {
            app = a;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            MaximizeBox = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            Text = "Pulp";
            Icon = app.AppIcon;
            KeyPreview = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            using (var g = CreateGraphics()) s = g.DpiX / 96f;

            fBody = Theme.Ui(10.5f);
            fSmall = Theme.Ui(9f);
            fChip = Theme.Ui(9.5f);
            fChipSel = Theme.UiSemi(9.5f);
            fIcon = new Font(Theme.IconFont, 11.5f);
            fIconSmall = new Font(Theme.IconFont, 10f);
            fIconRow = new Font(Theme.IconFont, 13f);
            MinimumSize = new Size(S(380), S(420));
            ClientSize = new Size(S(470), S(660));
            thumbs = new ThumbCache(app.Store, 48);

            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = fBody };
            search.KeyDown += SearchKeyDown;
            search.PreviewKeyDown += (o, e) => { if (e.KeyCode == Keys.Tab) e.IsInputKey = true; };
            search.TextChanged += (o, e) => { searchTimer.Stop(); searchTimer.Start(); };
            search.HandleCreated += (o, e) => { try { Native.SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search your clipboard"); } catch { } };
            Controls.Add(search);

            scrollAnim.Tick += (o, e) => ScrollStep();
            searchTimer = new Timer { Interval = 110 };
            searchTimer.Tick += (o, e) => { searchTimer.Stop(); if (search.Text.Trim() != query) { query = search.Text.Trim(); Reload(false); } };

            Relayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);           // dark title bar
                int cap = Theme.Bg.R | (Theme.Bg.G << 8) | (Theme.Bg.B << 16);
                Native.DwmSetWindowAttribute(Handle, 35, ref cap, 4);                          // title bar matches the window (Win11)
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);                       // border color
            }
            catch { }
        }

        void Relayout()
        {
            var f = SearchField;
            int x = f.X + S(36);
            search.SetBounds(x, f.Y + (f.Height - search.PreferredHeight) / 2, f.Right - x - S(10), search.PreferredHeight);
        }

        // ------------------------------------------------------------ show / hide
        bool placed;

        // open (or bring to the front) with a fresh search; the window keeps its own size and place
        public void ShowWindow()
        {
            bool wasVisible = Visible && WindowState != FormWindowState.Minimized;
            if (!placed) { PlaceFromSettings(); placed = true; }
            if (!wasVisible)
            {
                hover = null;
                downItem = null;
                searchTimer.Stop();
                search.Text = "";
                query = "";
                sel = 0;
                scrollY = 0;
                SnapScroll();
                Reload(true);
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Relayout();
            Show();
            Activate();
            Native.ForceForeground(Handle);
            search.Focus();
            if (!wasVisible) search.SelectAll();
            Invalidate();
        }

        void PlaceFromSettings()
        {
            var st = app.S;
            var r = new Rectangle(st.WinX, st.WinY, st.WinW, st.WinH);
            bool onScreen = st.WinW > 0 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X + S(40), r.Y + S(10), Math.Max(1, r.Width - S(80)), S(30))));
            if (onScreen) { Bounds = r; return; }
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
        }

        public void SaveBounds()
        {
            if (!placed) return;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (b.Width < 100 || b.Height < 100) return;
            app.S.WinX = b.X; app.S.WinY = b.Y; app.S.WinW = b.Width; app.S.WinH = b.Height;
            app.S.Save();
        }

        // restore: hand focus back to the window that was active before (Esc, hotkey again)
        public void HidePanel(bool restore = false)
        {
            if (!Visible) return;
            SaveBounds();
            Hide();
            LastHidden = DateTime.Now;
            if (restore && app.ValidTarget(Target)) Native.SetForegroundWindow(Target);
        }

        public bool RecentlyActive => Form.ActiveForm == this || (DateTime.Now - lastDeactivated).TotalMilliseconds < 400;

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            lastDeactivated = DateTime.Now;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // the close button just tucks Pulp back into the tray; Exit in the tray menu really quits
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(true); return; }
            SaveBounds();
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (search == null) return;
            Relayout();
            ClampScroll();
            Invalidate();
        }

        protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); SaveBounds(); }

        public void TargetChanged() { if (Visible) Invalidate(new Rectangle(0, Ht - FooterH, W, FooterH)); }

        void SetFilter(int f)
        {
            f = ((f % FilterNames.Length) + FilterNames.Length) % FilterNames.Length;
            if (f == filter) return;
            filter = f;
            Reload(false);
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { thumbs.Trim(12, true); rows = new List<Item>(); tops.Clear(); }
        }

        // a new capture or a change from elsewhere
        public void StoreChanged()
        {
            if (!Visible) return;
            long selId = sel >= 0 && sel < rows.Count ? rows[sel].Id : 0;
            Reload(!Filtering, selId);
        }

        // ------------------------------------------------------------ data
        void Reload(bool sync, long keepId = 0)
        {
            int g = ++gen;
            string q = EffectiveQuery;
            int want = Math.Max(Page, Math.Min(rows.Count, 400));
            if (sync)
            {
                try { Apply(app.Store.List(q, 0, want), want, app.Store.Count(), keepId); }
                catch (Exception ex) { Log.Crash(ex); }
                return;
            }
            Task.Run(() => Tuple.Create(app.Store.List(q, 0, want), app.Store.Count())).ContinueWith(t =>
            {
                if (t.IsFaulted) { Log.Crash(t.Exception); return; }
                if (g != gen || IsDisposed) return;
                Apply(t.Result.Item1, want, t.Result.Item2, keepId);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Apply(List<Item> list, int want, long total, long keepId)
        {
            rows = list;
            more = list.Count >= want;
            count = total;
            loadingMore = false;
            int keep = keepId != 0 ? rows.FindIndex(r => r.Id == keepId) : -1;
            if (keep >= 0) sel = keep;
            else if (keepId != 0) sel = Math.Max(0, Math.Min(sel, rows.Count - 1));   // the selected item was deleted
            else { sel = 0; scrollY = 0; SnapScroll(); }
            LayoutRows();
            EnsureVisible();
            Invalidate();
        }

        void LoadMore()
        {
            if (!more || loadingMore) return;
            loadingMore = true;
            int g = gen, off = rows.Count;
            string q = EffectiveQuery;
            Task.Run(() => app.Store.List(q, off, Page)).ContinueWith(t =>
            {
                loadingMore = false;
                if (t.IsFaulted) { Log.Crash(t.Exception); return; }
                if (g != gen || IsDisposed) return;
                rows.AddRange(t.Result);
                more = t.Result.Count >= Page;
                LayoutRows();
                Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void LayoutRows()
        {
            tops.Clear();
            int y = 0;
            foreach (var r in rows) { tops.Add(y); y += RowH(r); }
            totalH = y;
            ClampScroll();
        }

        int MaxScroll => Math.Max(0, totalH - ListH + S(4));

        void ClampScroll()
        {
            scrollY = Math.Max(0, Math.Min(scrollY, MaxScroll));
            scrollGoal = Math.Max(0, Math.Min(scrollGoal, MaxScroll));
            scrollPos = Math.Max(0, Math.Min(scrollPos, MaxScroll));
        }

        // a jump (keyboard, new search, reload): no glide
        void SnapScroll() { scrollAnim.Stop(); scrollGoal = scrollPos = scrollY; }

        void ScrollStep()
        {
            float d = scrollGoal - scrollPos;
            if (Math.Abs(d) < 0.6f) { scrollPos = scrollGoal; scrollAnim.Stop(); }
            else scrollPos += d * 0.3f;
            int y = (int)Math.Round(scrollPos);
            if (y == scrollY && scrollAnim.Enabled) return;
            scrollY = y;
            if (more && scrollY + ListH > totalH - S(400)) LoadMore();
            hover = null;
            Invalidate();
            Update();
        }

        void EnsureVisible()
        {
            if (sel < 0 || sel >= rows.Count) return;
            int y = tops[sel], h = RowH(rows[sel]), before = scrollY;
            if (y < scrollY) scrollY = y;
            else if (y + h > scrollY + ListH) scrollY = y + h - ListH + S(4);
            ClampScroll();
            if (scrollY != before) SnapScroll();
            if (more && scrollY + ListH > totalH - S(400)) LoadMore();
        }

        Item Selected => sel >= 0 && sel < rows.Count ? rows[sel] : null;

        void MoveSel(int delta)
        {
            if (rows.Count == 0) return;
            sel = Math.Max(0, Math.Min(rows.Count - 1, sel + delta));
            EnsureVisible();
            Invalidate();
        }

        // ------------------------------------------------------------ keyboard
        void SearchKeyDown(object o, KeyEventArgs e)
        {
            bool handled = true;
            var it = Selected;
            switch (e.KeyCode)
            {
                case Keys.Down: MoveSel(1); break;
                case Keys.Up: MoveSel(-1); break;
                case Keys.PageDown: MoveSel(7); break;
                case Keys.PageUp: MoveSel(-7); break;
                case Keys.Home when e.Control: MoveSel(-rows.Count); break;
                case Keys.End when e.Control: MoveSel(rows.Count); break;
                case Keys.Enter:
                    if (it != null) app.Use(it, plain: e.Shift, copyOnly: e.Control, target: Target);
                    break;
                case Keys.Tab: SetFilter(filter + (e.Shift ? -1 : 1)); break;
                case Keys.D1 when e.Control: case Keys.D2 when e.Control: case Keys.D3 when e.Control:
                case Keys.D4 when e.Control: case Keys.D5 when e.Control: case Keys.D6 when e.Control:
                    SetFilter(e.KeyCode - Keys.D1); break;
                case Keys.Escape:
                    if (search.Text.Length > 0) search.Clear(); else HidePanel(true);
                    break;
                case Keys.P when e.Control: if (it != null) app.TogglePin(it); break;
                case Keys.C when e.Control && search.SelectionLength == 0: if (it != null) app.Use(it, false, true, IntPtr.Zero); break;
                case Keys.S when e.Control: if (it != null && it.Kind == Kind.Image) app.SaveImageAs(it); break;
                case Keys.Delete when e.Shift || search.Text.Length == 0: if (it != null) app.DeleteItem(it); break;
                case Keys.Back when e.Control:
                    {
                        // TextBox would insert a box character; delete the previous word instead
                        int c = search.SelectionStart;
                        if (search.SelectionLength > 0) { search.SelectedText = ""; break; }
                        int i = c;
                        while (i > 0 && char.IsWhiteSpace(search.Text[i - 1])) i--;
                        while (i > 0 && !char.IsWhiteSpace(search.Text[i - 1])) i--;
                        search.Text = search.Text.Remove(i, c - i);
                        search.SelectionStart = i;
                        break;
                    }
                default: handled = false; break;
            }
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
        }

        // ------------------------------------------------------------ painting
        const TextFormatFlags L = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        const TextFormatFlags R = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        const TextFormatFlags C = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        void AddHit(Rectangle r, string key, Action click, Action<Point> right = null, Item row = null)
            => hits.Add(new Hit { R = r, Key = key, Click = click, Right = right, Row = row });

        Hit FindHit(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }

        void IconButton(Graphics g, string glyph, Rectangle r, string key, Action click, Font f = null, Color? color = null)
        {
            if (hover == key) Theme.Fill(g, r, Theme.CardHover, S(6));
            TextRenderer.DrawText(g, glyph, f ?? fIcon, r, color ?? (hover == key ? Theme.Text : Theme.Sub), C);
            AddHit(r, key, click);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            hits.Clear();

            // list
            var lr = ListRect;
            if (rows.Count == 0)
            {
                string head = query.Length > 0 ? "No matches" : filter != 0 ? "No " + FilterNames[filter].ToLowerInvariant() + " yet" : app.S.Paused ? "Recording is paused" : "Nothing copied yet";
                string sub = query.Length > 0 ? "Try fewer words" + (filter != 0 ? ", or switch back to All" : "") : filter == 5 ? "Pin anything with Ctrl+P" : "Copy some text, a link, files or a screenshot";
                int ey = lr.Y + Math.Min(S(150), lr.Height / 3);
                TextRenderer.DrawText(g, head, fBody, new Rectangle(0, ey, W, S(26)), Theme.Sub, C);
                TextRenderer.DrawText(g, sub, fSmall, new Rectangle(0, ey + S(28), W, S(20)), Theme.Dim, C);
            }
            else
            {
                g.SetClip(lr);
                for (int i = 0; i < rows.Count; i++)
                {
                    int y = lr.Y + tops[i] - scrollY, h = RowH(rows[i]);
                    if (y + h < lr.Y) continue;
                    if (y > lr.Bottom) break;
                    DrawRow(g, i, new Rectangle(Pad - S(2), y + S(1), W - Pad * 2 + S(4) - S(4), h - S(3)), lr);
                }
                g.ResetClip();
                if (totalH > ListH)
                {
                    int trackH = ListH - S(8);
                    int thumbH = Math.Max(S(24), (int)((long)trackH * ListH / Math.Max(1, totalH)));
                    int maxScroll = Math.Max(1, totalH - ListH + S(4));
                    int thumbY = lr.Y + S(4) + (int)((long)(trackH - thumbH) * scrollY / maxScroll);
                    Theme.Fill(g, new Rectangle(W - S(6), thumbY, S(3), thumbH), Theme.Border, S(2));
                }
            }

            // header and footer are painted last, over solid background, so scrolled rows never show through
            g.ResetClip();
            using (var bg = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(bg, 0, 0, W, ListTop);
                g.FillRectangle(bg, 0, Ht - FooterH, W, FooterH);
            }

            // search
            var sf = SearchField;
            Theme.Fill(g, sf, Theme.Field, S(8));
            if (search.Focused) Theme.Stroke(g, sf, Theme.Border, S(8), Math.Max(1, s));
            TextRenderer.DrawText(g, Theme.GSearch, fIconSmall, new Rectangle(sf.X + S(8), sf.Y, S(24), sf.Height), Theme.Dim, C);
            IconButton(g, Theme.GMore, MenuButton, "menu", () => app.ShowMainMenu(PointToScreen(new Point(MenuButton.Right - S(4), MenuButton.Bottom))));

            // filter buttons
            int cx = Pad, cy = S(62), ch = S(30);
            for (int i = 0; i < FilterNames.Length; i++)
            {
                var f = i == filter ? fChipSel : fChip;
                int cw = TextRenderer.MeasureText(g, FilterNames[i], f, Size.Empty, TextFormatFlags.NoPadding).Width + S(26);
                if (cx + cw > W - Pad) break;
                var cr = new Rectangle(cx, cy, cw, ch);
                string key = "chip:" + i;
                if (i == filter) Theme.Fill(g, cr, Theme.Accent, ch / 2f);
                else Theme.Fill(g, cr, hover == key ? Theme.CardHover : Theme.Card, ch / 2f);
                TextRenderer.DrawText(g, FilterNames[i], f, cr, i == filter ? Color.FromArgb(28, 20, 14) : hover == key ? Theme.Text : Theme.Sub, C);
                int idx = i;
                AddHit(cr, key, () => SetFilter(idx));
                cx += cw + S(6);
            }

            // footer
            using (var p = new Pen(Theme.Border, Math.Max(1, s))) g.DrawLine(p, Pad, Ht - FooterH, W - Pad, Ht - FooterH);
            var fr = new Rectangle(Pad + S(4), Ht - FooterH, W - Pad * 2 - S(8), FooterH);
            string left = app.S.Paused ? "Paused · not recording" : Filtering ? rows.Count + (more ? "+" : "") + " found" : count.ToString("N0") + (count == 1 ? " item" : " items");
            int lw = TextRenderer.MeasureText(g, left, fSmall, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, left, fSmall, fr, app.S.Paused ? Theme.Accent : Theme.Dim, L);
            bool canPaste = app.S.PasteOnEnter && app.ValidTarget(Target);
            string hint = canPaste ? "Enter pastes into " + TargetName() + "  ·  Shift+Enter plain" : "Enter copies  ·  Ctrl+P pin";
            TextRenderer.DrawText(g, hint, fSmall, new Rectangle(fr.X + lw + S(16), fr.Y, fr.Width - lw - S(16), fr.Height), Theme.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        string TargetName()
        {
            if (targetNameFor != Target || targetName == null)
            {
                targetNameFor = Target;
                targetName = Clip.AppNameOf(Target) ?? "the last app";
            }
            return targetName;
        }

        void DrawRow(Graphics g, int i, Rectangle r, Rectangle clip)
        {
            var it = rows[i];
            bool selected = i == sel;
            string rowKey = "row:" + it.Id, actPrefix = "act:" + it.Id + ":";
            bool hov = hover == rowKey || (hover != null && hover.StartsWith(actPrefix, StringComparison.Ordinal));

            if (selected) Theme.Fill(g, r, Theme.Card, S(8));
            else if (hov) Theme.Fill(g, r, Theme.CardSoft, S(8));
            if (selected) Theme.Fill(g, new Rectangle(r.X + S(3), r.Y + S(12), S(3), r.Height - S(24)), Theme.Accent, S(1.5f));
            AddHit(Rectangle.Intersect(r, clip), rowKey, () => app.Use(it, false, false, Target), pt => app.ShowRowMenu(it, PointToScreen(pt)), it);

            int x = r.X + S(12);
            if (it.Kind == Kind.Image)
            {
                var tb = new Rectangle(x, r.Y + S(8), S(120), r.Height - S(16));
                DrawThumb(g, it, tb);
                x = tb.Right + S(12);
            }
            else
            {
                var ib = new Rectangle(x, r.Y + (r.Height - S(36)) / 2, S(36), S(36));
                Theme.Fill(g, ib, selected ? Theme.IconBgSel : Theme.IconBg, S(7));
                string glyph = it.Kind == Kind.Link ? Theme.GLink : it.Kind == Kind.Files ? Theme.GFolder : Theme.GText;
                TextRenderer.DrawText(g, glyph, fIconRow, ib, it.Kind == Kind.Link ? Theme.Accent : Theme.Sub, C);
                x = ib.Right + S(12);
            }

            // right side: actions on hover, pin marker otherwise
            int right = r.Right - S(8);
            if (hov)
            {
                var acts = new List<Tuple<string, string, Action>>();
                if (it.Kind == Kind.Image)
                {
                    acts.Add(Tuple.Create(Theme.GCopy, "copy", (Action)(() => app.Use(it, false, true, IntPtr.Zero))));
                    acts.Add(Tuple.Create(Theme.GSave, "save", (Action)(() => app.SaveImageAs(it))));
                }
                if (it.Original != null)
                    acts.Add(Tuple.Create(Theme.GUndo, "orig", (Action)(() => app.CopyOriginal(it))));
                acts.Add(Tuple.Create(it.Pinned ? Theme.GUnpin : Theme.GPin, "pin", (Action)(() => app.TogglePin(it))));
                acts.Add(Tuple.Create(Theme.GDelete, "del", (Action)(() => app.DeleteItem(it))));
                int bw = S(28), bx = right - bw * acts.Count, by = r.Y + (r.Height - bw) / 2;
                foreach (var a in acts)
                {
                    var br = new Rectangle(bx, by, bw, bw);
                    string key = actPrefix + a.Item2;
                    if (hover == key) Theme.Fill(g, br, Theme.CardHover, S(6));
                    Color c = a.Item2 == "pin" && it.Pinned ? Theme.Accent : hover == key ? Theme.Text : Theme.Sub;
                    TextRenderer.DrawText(g, a.Item1, fIconSmall, br, c, C);
                    AddHit(Rectangle.Intersect(br, clip), key, a.Item3, null, it);
                    bx += bw;
                }
                right -= bw * acts.Count + S(4);
            }
            else if (it.Pinned)
            {
                TextRenderer.DrawText(g, Theme.GPinned, fIconSmall, new Rectangle(right - S(22), r.Y, S(22), r.Height), Theme.Accent, C);
                right -= S(26);
            }

            int ty = it.Kind == Kind.Image ? r.Y + S(22) : r.Y + S(9);
            TextRenderer.DrawText(g, Title(it), fBody, new Rectangle(x, ty, right - x, S(23)), Theme.Text, L);
            var mr = new Rectangle(x, ty + S(25), right - x, S(19));
            string meta = Meta(it);
            if (it.Original != null)
            {
                // cleaned links get an accent tag; the undo button (hover) copies the original
                const string tag = "cleaned";
                int tw = TextRenderer.MeasureText(g, tag, fSmall, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                TextRenderer.DrawText(g, tag, fSmall, new Rectangle(mr.X, mr.Y, tw + S(2), mr.Height), Theme.Accent, L);
                mr = new Rectangle(mr.X + tw + S(2), mr.Y, Math.Max(0, mr.Width - tw - S(2)), mr.Height);
                meta = "  ·  " + meta;
            }
            TextRenderer.DrawText(g, meta, fSmall, mr, selected ? Theme.Sub : Theme.Dim, L);
        }

        void DrawThumb(Graphics g, Item it, Rectangle box)
        {
            Theme.Fill(g, box, Theme.ThumbBg, S(6));
            var bmp = thumbs.Get(it.Img);
            if (bmp == null)
            {
                TextRenderer.DrawText(g, Theme.GImage, fIcon, box, Theme.Dim, C);
                return;
            }
            double k = Math.Min((double)box.Width / bmp.Width, (double)box.Height / bmp.Height);
            int w = Math.Max(1, (int)(bmp.Width * k)), h = Math.Max(1, (int)(bmp.Height * k));
            var dr = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            var state = g.Save();
            using (var path = Theme.Round(box, S(6)))
            {
                g.SetClip(path, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(bmp, dr);
            }
            g.Restore(state);
            Theme.Stroke(g, box, Color.FromArgb(56, 56, 56), S(6), Math.Max(1, s));
        }

        static string Collapse(string s, int max = 160)
        {
            var sb = new System.Text.StringBuilder(Math.Min(s.Length, max));
            bool space = false;
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch) || char.IsControl(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(ch);
                if (sb.Length >= max) break;
            }
            return sb.ToString();
        }

        static string Title(Item it)
        {
            switch (it.Kind)
            {
                case Kind.Link: return Links.Display(it.Preview);
                case Kind.Files:
                    {
                        var parts = it.Preview.Split('\n');
                        string name = Path.GetFileName(parts[0].TrimEnd('\\'));
                        if (name.Length == 0) name = parts[0];
                        int n = it.Lines;
                        return n > 1 ? name + "  + " + (n - 1) + " more" : name;
                    }
                case Kind.Image: return "Image  ·  " + it.W + " × " + it.H;
                default:
                    {
                        // first non-empty line
                        foreach (var line in it.Preview.Split('\n'))
                        {
                            var t = Collapse(line);
                            if (t.Length > 0) return t;
                        }
                        return "(blank)";
                    }
            }
        }

        string Meta(Item it)
        {
            var parts = new List<string> { Ago(it.Ts) };
            if (!string.IsNullOrEmpty(it.App)) parts.Add(it.App);
            switch (it.Kind)
            {
                case Kind.Text:
                    if (it.Lines > 1) parts.Add(it.Lines.ToString("N0") + " lines");
                    else if (it.Len > 120) parts.Add(it.Len.ToString("N0") + " chars");
                    break;
                case Kind.Files:
                    {
                        string dir = Path.GetDirectoryName(it.Preview.Split('\n')[0].TrimEnd('\\')) ?? "";
                        if (dir.Length > 0) parts.Add(ShortPath(dir));
                        break;
                    }
                case Kind.Image: parts.Add(FmtSize(it.Bytes)); break;
            }
            return string.Join("  ·  ", parts);
        }

        static string ShortPath(string p)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (p.StartsWith(home, StringComparison.OrdinalIgnoreCase)) p = "~" + p.Substring(home.Length);
            return p;
        }

        public static string FmtSize(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024) + " KB";
            return (b / 1048576.0).ToString("0.0") + " MB";
        }

        public static string Ago(long ts)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
            var now = DateTime.Now;
            var d = now - t;
            if (d.TotalSeconds < 60) return "just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
            if (t.Date == now.Date) return t.ToString("HH:mm");
            if (t.Date == now.Date.AddDays(-1)) return "Yesterday " + t.ToString("HH:mm");
            if (d.TotalDays < 6) return t.ToString("ddd HH:mm");
            if (t.Year == now.Year) return t.ToString("d MMM");
            return t.ToString("d MMM yyyy");
        }

        // ------------------------------------------------------------ mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            downItem = null;
            if (e.Button != MouseButtons.Left) return;
            var h = FindHit(e.Location);
            if (h != null && h.Key.StartsWith("row:", StringComparison.Ordinal)) { downItem = h.Row; downPt = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left && downItem != null)
            {
                var ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - downPt.X) > ds.Width || Math.Abs(e.Y - downPt.Y) > ds.Height)
                {
                    var it = downItem;
                    downItem = null;
                    StartDrag(it);
                    return;
                }
            }
            var h = FindHit(e.Location);
            string k = h?.Key;
            if (k != hover)
            {
                hover = k;
                Invalidate();
                string label = TipFor(k, h?.Row);
                if (label != tip.GetToolTip(this)) tip.SetToolTip(this, label);
            }
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var h = FindHit(e.Location);
            bool wasDown = downItem != null && h != null && h.Row == downItem;
            downItem = null;
            if (h == null) return;
            if (e.Button == MouseButtons.Left)
            {
                if (h.Key.StartsWith("row:", StringComparison.Ordinal) && !wasDown) return; // a drag ended here, or the press began elsewhere
                if (h.Row != null && h.Key.StartsWith("row:", StringComparison.Ordinal)) sel = rows.IndexOf(h.Row);
                h.Click?.Invoke();
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (h.Row != null) { sel = rows.IndexOf(h.Row); Invalidate(); }
                h.Right?.Invoke(e.Location);
            }
            Invalidate();
            if (Visible && !SuppressHide) search.Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // the real delta: a touchpad sends small steps and scrolls by the pixel
            scrollGoal = Math.Max(0, Math.Min(MaxScroll, scrollGoal - e.Delta * S(64) * 3 / 120f));
            if (!scrollAnim.Enabled) { scrollPos = scrollY; scrollAnim.Start(); }
        }

        static string TipFor(string key, Item row)
        {
            if (key == null) return null;
            if (key == "menu") return "Menu";
            if (key.StartsWith("chip:", StringComparison.Ordinal)) return "Ctrl+" + (int.Parse(key.Substring(5)) + 1) + "  (Tab cycles)";
            int i = key.LastIndexOf(':');
            if (!key.StartsWith("act:", StringComparison.Ordinal) || i < 0) return null;
            switch (key.Substring(i + 1))
            {
                case "copy": return "Copy again";
                case "save": return "Save as PNG";
                case "pin": return row != null && row.Pinned ? "Unpin" : "Pin";
                case "del": return "Delete";
                case "orig": return "Copy the original link (before cleaning)";
            }
            return null;
        }

        void StartDrag(Item it)
        {
            DataObject data;
            try { data = app.DragData(it); }
            catch (Exception ex) { Log.Crash(ex); return; }
            if (data == null) return;
            SuppressHide = true;
            try { DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Link); }
            catch (Exception ex) { Log.Crash(ex); }
            finally { SuppressHide = false; }
            if (Visible) { Activate(); Native.ForceForeground(Handle); search.Focus(); }
        }

        public void FocusSearch() { if (Visible) search.Focus(); }
    }

    // small LRU of decoded thumbnails
    class ThumbCache
    {
        readonly Store store;
        readonly int cap;
        readonly Dictionary<string, LinkedListNode<Tuple<string, Bitmap>>> map = new Dictionary<string, LinkedListNode<Tuple<string, Bitmap>>>();
        readonly LinkedList<Tuple<string, Bitmap>> lru = new LinkedList<Tuple<string, Bitmap>>();
        readonly HashSet<string> missing = new HashSet<string>();

        public ThumbCache(Store s, int capacity) { store = s; cap = capacity; }

        public Bitmap Get(string img)
        {
            if (string.IsNullOrEmpty(img) || missing.Contains(img)) return null;
            if (map.TryGetValue(img, out var node)) { lru.Remove(node); lru.AddFirst(node); return node.Value.Item2; }
            Bitmap b = null;
            try
            {
                string p = store.ThumbPath(img);
                if (!File.Exists(p)) p = store.ImagePath(img);
                if (File.Exists(p))
                {
                    b = ImageTools.Load(p);
                    if (b.Width > 400 || b.Height > 300) { var t = ImageTools.Thumb(b, 256, 160); b.Dispose(); b = t; }
                }
            }
            catch { b = null; }
            if (b == null) { missing.Add(img); return null; }
            var n = lru.AddFirst(Tuple.Create(img, b));
            map[img] = n;
            Trim(cap);
            return b;
        }

        public void Trim(int keep, bool forgetMissing = false)
        {
            while (lru.Count > keep)
            {
                var last = lru.Last;
                lru.RemoveLast();
                map.Remove(last.Value.Item1);
                last.Value.Item2.Dispose();
            }
            if (forgetMissing) missing.Clear();
        }
    }
}
