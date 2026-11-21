using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pulp
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, "Pulp.SingleInstance.v1", out bool created))
            {
                bool exit = args.Contains("--exit");
                if (!created)
                {
                    Native.PostMessage((IntPtr)0xffff, exit ? Native.WM_EXITPULP : Native.WM_SHOWPULP, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (exit) return;   // asked to close, but Pulp wasn't running
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (o, e) => Log.Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log.Crash(e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (o, e) => { Log.Crash(e.Exception); e.SetObserved(); };
                // background work posts results back to the UI thread through this context,
                // so it must exist before anything is created
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                App app;
                try { app = new App(args.Contains("--tray")); }
                catch (Exception ex)
                {
                    Log.Crash(ex);
                    MessageBox.Show("Pulp couldn't start:\n\n" + ex.Message + "\n\nDetails are in " + Log.Path, "Pulp", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(app);
                GC.KeepAlive(mutex);
            }
        }
    }

    static class Log
    {
        public static string Path => System.IO.Path.Combine(Settings.Dir, "crash.log");
        static readonly object gate = new object();
        public static void Crash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(Path); // never let it grow forever
                    File.AppendAllText(Path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + "\r\n\r\n");
                }
            }
            catch { }
        }
    }

    // one background thread for captures: images are decoded/compressed here, never on the UI thread
    class Worker : IDisposable
    {
        readonly BlockingCollection<Action> q = new BlockingCollection<Action>();
        public Worker()
        {
            new Thread(Run) { IsBackground = true, Name = "PulpWorker", Priority = ThreadPriority.BelowNormal }.Start();
        }
        void Run()
        {
            foreach (var a in q.GetConsumingEnumerable())
                try { a(); } catch (Exception ex) { Log.Crash(ex); }
        }
        public void Post(Action a) { try { q.Add(a); } catch { } }
        public void Dispose() { try { q.CompleteAdding(); } catch { } }
    }

    class MsgWindow : NativeWindow, IDisposable
    {
        readonly App app;
        public bool HotkeyOk;
        public MsgWindow(App a)
        {
            app = a;
            CreateHandle(new CreateParams { Caption = "PulpMsgWindow" });
            Native.AddClipboardFormatListener(Handle);
        }
        public bool SetHotkey(Hotkey h)
        {
            Native.UnregisterHotKey(Handle, 1);
            HotkeyOk = Native.RegisterHotKey(Handle, 1, h.Modifiers | 0x4000 /* MOD_NOREPEAT */, (int)h.Key);
            return HotkeyOk;
        }
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == 0x031D) app.OnClipboardUpdate();
                else if (m.Msg == 0x0312) app.OnHotkey();
                else if (m.Msg == Native.WM_SHOWPULP) app.OpenFromTray();
                else if (m.Msg == Native.WM_EXITPULP) app.ExitSoon();
            }
            catch (Exception ex) { Log.Crash(ex); }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            Native.RemoveClipboardFormatListener(Handle);
            Native.UnregisterHotKey(Handle, 1);
            DestroyHandle();
        }
    }

    class App : ApplicationContext
    {
        public readonly Settings S;
        public readonly Store Store;
        public readonly Icon AppIcon;
        public readonly PulpPanel Panel;
        public LinkCleaner Cleaner { get; private set; }
        SettingsForm settingsForm;

        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly MsgWindow msg;
        readonly Worker worker = new Worker();
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer captureTimer;
        uint ignoreSeq, lastSeq;
        int captureAttempt, adds;
        Action balloonAction;
        WinEventHook fgHook;
        int modal;   // >0 while a dialog we opened from the panel is up

        public App(bool startHidden)
        {
            ui = SynchronizationContext.Current;
            S = Settings.Load();
            Store = OpenStore();
            Cleaner = LinkCleaner.LoadDefault();
            if (S.FirstRun) { Settings.StartWithWindows = true; S.Save(); }

            try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { AppIcon = SystemIcons.Application; }

            Panel = new PulpPanel(this);
            menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.Ui(9.5f), ShowImageMargin = true };
            menu.Opening += (o, e) => { BuildMenu(); e.Cancel = false; };
            menu.Closed += (o, e) => ReleasePanel();

            tray = new NotifyIcon { Text = "Pulp", Visible = true, ContextMenuStrip = menu, Icon = TrayArt.Render(S.Paused) };
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) TogglePanelFromTray(); };
            tray.BalloonTipClicked += (o, e) => { var a = balloonAction; balloonAction = null; a?.Invoke(); };
            tray.BalloonTipClosed += (o, e) => balloonAction = null;
            UpdateTray();

            captureTimer = new System.Windows.Forms.Timer { Interval = 60 };
            captureTimer.Tick += (o, e) => CaptureTick();

            msg = new MsgWindow(this);
            lastSeq = Native.GetClipboardSequenceNumber();   // don't re-record what was already there at startup
            if (!msg.SetHotkey(S.Hotkey))
                Balloon("Pulp", S.Hotkey + " is already used by another app. Click here to pick a different shortcut.", ToolTipIcon.Warning, () => OpenSettings("General"));
            else if (S.FirstRun)
                Balloon("Pulp is keeping your clipboard", "Press " + S.Hotkey + " to open your history.", ToolTipIcon.None, OpenFromTray);

            worker.Post(Housekeeping);
            CleanTemp();
            // remember the last window outside Pulp: that's where Enter pastes
            Panel.Target = Native.GetForegroundWindow();
            fgHook = WinEventHook.Foreground(hwnd =>
            {
                if (!ValidTarget(hwnd)) return;
                Panel.Target = hwnd;
                Panel.TargetChanged();
            });
            if (!startHidden) Panel.ShowWindow();
        }

        Store OpenStore()
        {
            Directory.CreateDirectory(Settings.Dir);
            try { return new Store(Settings.Dir); }
            catch (DllNotFoundException) { throw new Exception("Windows' built-in SQLite (winsqlite3.dll) is missing. Pulp needs Windows 10 or 11."); }
            catch (Exception ex)
            {
                // a damaged database shouldn't stop the app: set it aside and start a fresh one
                Log.Crash(ex);
                string db = Path.Combine(Settings.Dir, "history.db");
                if (!File.Exists(db)) throw;
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                foreach (var ext in new[] { "", "-wal", "-shm" })
                    try { if (File.Exists(db + ext)) File.Move(db + ext, Path.Combine(Settings.Dir, "history.broken-" + stamp + ".db" + ext)); } catch { }
                return new Store(Settings.Dir);
            }
        }

        void Housekeeping()
        {
            Store.CleanOrphans();
            Store.Maintain(S.MaxItems, S.KeepFormatting);
            Store.EnforceImageCap((long)S.ImageCapMB * 1024 * 1024);
        }

        void Ui(Action a) => ui.Post(_ => { try { a(); } catch (Exception ex) { Log.Crash(ex); } }, null);

        // ------------------------------------------------------------ tray
        void UpdateTray()
        {
            var old = tray.Icon;
            tray.Icon = TrayArt.Render(S.Paused);
            if (old != null && old != tray.Icon) old.Dispose();
            tray.Text = S.Paused ? "Pulp · paused" : "Pulp · " + S.Hotkey;
        }

        void Balloon(string title, string text, ToolTipIcon icon, Action click)
        {
            balloonAction = click;
            tray.ShowBalloonTip(5000, title, text, icon);
        }

        void TogglePanelFromTray()
        {
            // clicking the tray takes focus first, so "was it just active?" decides between hide and show
            if (Panel.Visible && Panel.WindowState != FormWindowState.Minimized && Panel.RecentlyActive) { Panel.HidePanel(true); return; }
            Panel.ShowWindow();
        }

        public void OpenFromTray()
        {
            Panel.ShowWindow();
        }

        public void OnHotkey()
        {
            if (Panel.Visible && Form.ActiveForm == Panel) { Panel.HidePanel(true); return; }
            IntPtr fg = Native.GetForegroundWindow();
            if (ValidTarget(fg)) Panel.Target = fg;
            Panel.ShowWindow();
        }

        // ------------------------------------------------------------ capture
        public void OnClipboardUpdate()
        {
            if (S.Paused) return;
            uint seq = Native.GetClipboardSequenceNumber();
            if (seq == ignoreSeq || seq == lastSeq) return;
            // apps often write several formats in a row: wait a beat, then read once
            captureAttempt = 0;
            captureTimer.Stop();
            captureTimer.Interval = 60;
            captureTimer.Start();
        }

        void CaptureTick()
        {
            captureTimer.Stop();
            uint seq = Native.GetClipboardSequenceNumber();
            if (S.Paused || seq == ignoreSeq || seq == lastSeq) return;
            ReadResult r;
            Snapshot snap;
            try { r = Clip.Read(msg.Handle, out snap); }
            catch (Exception ex) { Log.Crash(ex); lastSeq = seq; return; }
            if (r == ReadResult.Busy)
            {
                // someone else has the clipboard open; back off and try again (about 3 s in total)
                if (++captureAttempt < 14) { captureTimer.Interval = Math.Min(400, 30 + captureAttempt * 30); captureTimer.Start(); }
                return;
            }
            lastSeq = seq;
            if (r != ReadResult.Ok || snap == null) return;
            if (S.IsIgnored(snap.Exe)) return;
            if (S.CleanLinks && snap.Text != null && Links.IsSingleUrl(snap.Text)) CleanOnClipboard(snap);
            worker.Post(() => Ingest(snap));
        }

        // a single copied link: swap in the cleaned version right away (only if nobody changed the clipboard meanwhile)
        void CleanOnClipboard(Snapshot snap)
        {
            string cleaned;
            try { cleaned = Cleaner.Clean(snap.Text); }
            catch (Exception ex) { Log.Crash(ex); return; }
            if (cleaned == null) return;
            uint seq = Clip.Write(msg.Handle, new Clip.Payload { Text = cleaned }, snap.Seq);
            if (seq == 0) return;
            ignoreSeq = seq;
            lastSeq = seq;
            snap.Original = snap.Text.Trim();
            snap.Text = cleaned;
            snap.Html = snap.Rtf = null;
        }

        void Ingest(Snapshot s)
        {
            if (s.Files != null)
                Store.AddFiles(s.Files, s.AppName);
            else if (s.Text != null)
            {
                bool link = Links.IsSingleUrl(s.Text);
                Store.AddText(link ? s.Text.Trim() : s.Text, link ? Kind.Link : Kind.Text, s.Html, s.Rtf, s.AppName, s.Original);
            }
            else if (s.Png != null || s.Dib != null)
            {
                if (!S.SaveImages) return;
                IngestImage(s);
            }
            else return;
            if (++adds % 25 == 0) Store.Maintain(S.MaxItems, S.KeepFormatting);
            Ui(Panel.StoreChanged);
        }

        void IngestImage(Snapshot s)
        {
            Pixels px = null;
            try { if (s.Png != null) px = ImageTools.FromBytes(s.Png); } catch (Exception ex) { Log.Crash(ex); }
            try { if (px == null && s.Dib != null) px = ImageTools.FromDib(s.Dib); } catch (Exception ex) { Log.Crash(ex); }
            s.Png = s.Dib = null;
            if (px == null) return;
            int w = px.W, h = px.H;
            string hash = ImageTools.Hash(px);
            long existing = Store.FindHash("i:" + hash);
            if (existing != 0 && File.Exists(Store.ImagePath(hash + ".png")))
            {
                Store.Touch(existing);   // same picture again: just move it to the top
            }
            else
            {
                if (existing != 0) Store.Delete(existing);
                string name = hash + ".png";
                long bytes;
                using (var bmp = ImageTools.ToBitmap(px))
                {
                    px = null;
                    bytes = ImageTools.SavePng(bmp, Store.ImagePath(name));
                    using (var th = ImageTools.Thumb(bmp, 256, 160)) bytes += ImageTools.SavePng(th, Store.ThumbPath(name));
                }
                Store.AddImage(hash, name, w, h, bytes, s.AppName);
                Store.EnforceImageCap((long)S.ImageCapMB * 1024 * 1024);
            }
            if ((long)w * h > 2000000)
            {
                // give big screenshot buffers back to Windows right away
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }
        }

        // ------------------------------------------------------------ using an item
        static readonly int MyPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        public bool ValidTarget(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || h == Panel.Handle) return false;
            string c = Native.ClassOf(h);
            if (Native.ProcessIdOf(h) == MyPid) return false;
            // taskbar, tray overflow, Start/search, Alt+Tab: never paste targets
            return c != "Shell_TrayWnd" && c != "Shell_SecondaryTrayWnd" && c != "NotifyIconOverflowWindow" && c != "TopLevelWindowForOverflowXamlIsland"
                && c != "Windows.UI.Core.CoreWindow" && c != "XamlExplorerHostIslandWindow" && c != "MultitaskingViewFrame" && c != "ForegroundStaging" && c != "TaskSwitcherWnd";
        }

        Clip.Payload BuildPayload(Full f, bool plain)
        {
            var p = new Clip.Payload();
            switch (f.Kind)
            {
                case Kind.Files:
                    var paths = (f.Text ?? "").Split('\n').Where(x => x.Length > 0).ToArray();
                    if (plain) p.Text = string.Join("\r\n", paths); else p.Files = paths;
                    break;
                case Kind.Image:
                    string path = Store.ImagePath(f.Img);
                    if (plain && !string.IsNullOrWhiteSpace(f.Text)) { p.Text = f.Text; break; } // OCR text, once stage 3 is in
                    if (!File.Exists(path)) return null;
                    p.Png = File.ReadAllBytes(path);
                    using (var b = ImageTools.Load(path)) p.Dib = ImageTools.ToDib(b);
                    break;
                default:
                    p.Text = f.Text ?? "";
                    if (!plain) { p.Html = f.Html; p.Rtf = f.Rtf; }
                    break;
            }
            return p;
        }

        // Enter / click: put the item on the clipboard and paste it into the window that was active
        public async void Use(Item it, bool plain, bool copyOnly, IntPtr target)
        {
            try
            {
                bool paste = !copyOnly && S.PasteOnEnter && ValidTarget(target);
                if (paste && S.HideAfterPaste) Panel.HidePanel();
                Full f = Store.Get(it.Id);
                if (f == null) return;
                Clip.Payload p = f.Kind == Kind.Image ? await Task.Run(() => BuildPayload(f, plain)) : BuildPayload(f, plain);
                if (p == null) { Balloon("Pulp", "That image file is missing.", ToolTipIcon.Warning, null); return; }
                uint seq = Clip.Write(msg.Handle, p);
                if (seq == 0) { Balloon("Pulp", "Another app is holding the clipboard. Try again in a moment.", ToolTipIcon.Warning, null); return; }
                ignoreSeq = seq;
                lastSeq = seq;
                long id = it.Id;
                worker.Post(() => Store.Touch(id));
                if (copyOnly || !S.PasteOnEnter || !ValidTarget(target)) return;

                Native.SetForegroundWindow(target);
                for (int i = 0; i < 30 && Native.GetForegroundWindow() != target; i++)
                {
                    await Task.Delay(10);
                    if (i == 8) Native.ForceForeground(target);
                }
                await Task.Delay(40);
                if (Native.GetForegroundWindow() == target) Native.SendCtrlV();
            }
            catch (Exception ex) { Log.Crash(ex); }
        }

        public void TogglePin(Item it)
        {
            it.Pinned = !it.Pinned;
            bool pin = it.Pinned;
            long id = it.Id;
            worker.Post(() => { Store.SetPinned(id, pin); Ui(Panel.StoreChanged); });
            Panel.Invalidate();
        }

        public void DeleteItem(Item it)
        {
            long id = it.Id;
            worker.Post(() => { Store.Delete(id); Ui(Panel.StoreChanged); });
        }

        void ClearHistory() => ClearHistoryFrom(Panel.Visible ? Panel : null);

        public void ClearHistoryFrom(IWin32Window owner)
        {
            Panel.SuppressHide = true;
            modal++;
            DialogResult r;
            try { r = MessageBox.Show(owner, "Delete everything in your clipboard history except pinned items?", "Clear history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2); }
            finally { modal--; Panel.SuppressHide = false; }
            if (r != DialogResult.Yes) { if (Panel.Visible) Panel.Activate(); return; }
            worker.Post(() =>
            {
                int n = Store.ClearUnpinned();
                Ui(() => { Panel.StoreChanged(); Balloon("Pulp", n.ToString("N0") + " items cleared. Pinned items were kept.", ToolTipIcon.None, null); });
            });
            if (Panel.Visible) Panel.Activate();
        }

        static string NiceName(Item it) => "Pulp " + DateTimeOffset.FromUnixTimeMilliseconds(it.Ts).LocalDateTime.ToString("yyyy-MM-dd HHmmss") + ".png";

        static string TempDir => Path.Combine(Path.GetTempPath(), "Pulp");

        static void CleanTemp()
        {
            try
            {
                if (!Directory.Exists(TempDir)) return;
                foreach (var f in Directory.EnumerateFiles(TempDir))
                    if ((DateTime.Now - File.GetLastWriteTime(f)).TotalHours > 12) try { File.Delete(f); } catch { }
            }
            catch { }
        }

        public DataObject DragData(Item it)
        {
            var f = Store.Get(it.Id);
            if (f == null) return null;
            var d = new DataObject();
            switch (f.Kind)
            {
                case Kind.Image:
                    {
                        string src = Store.ImagePath(f.Img);
                        if (!File.Exists(src)) return null;
                        Directory.CreateDirectory(TempDir);
                        string dst = Path.Combine(TempDir, NiceName(it));
                        if (!File.Exists(dst)) File.Copy(src, dst, true);
                        d.SetData(DataFormats.FileDrop, new[] { dst });
                        break;
                    }
                case Kind.Files:
                    {
                        var paths = (f.Text ?? "").Split('\n').Where(p => p.Length > 0 && (File.Exists(p) || Directory.Exists(p))).ToArray();
                        if (paths.Length == 0) d.SetText(f.Text ?? ""); else d.SetData(DataFormats.FileDrop, paths);
                        break;
                    }
                default:
                    if (string.IsNullOrEmpty(f.Text)) return null;
                    d.SetText(f.Text);
                    break;
            }
            return d;
        }

        public void SaveImageAs(Item it)
        {
            var f = Store.Get(it.Id);
            if (f == null || f.Kind != Kind.Image) return;
            string src = Store.ImagePath(f.Img);
            if (!File.Exists(src)) return;
            string dir = Directory.Exists(S.SaveFolder) ? S.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            using (var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = NiceName(it), InitialDirectory = dir, OverwritePrompt = true, AddExtension = true, DefaultExt = "png" })
            {
                Panel.SuppressHide = true;
                modal++;
                DialogResult r;
                try { r = dlg.ShowDialog(Panel.Visible ? Panel : null); }
                finally { modal--; Panel.SuppressHide = false; }
                if (r == DialogResult.OK)
                {
                    try
                    {
                        File.Copy(src, dlg.FileName, true);
                        S.SaveFolder = Path.GetDirectoryName(dlg.FileName);
                        S.Save();
                    }
                    catch (Exception ex) { MessageBox.Show("Couldn't save the image: " + ex.Message, "Pulp"); }
                }
                if (Panel.Visible) { Panel.Activate(); Panel.FocusSearch(); }
            }
        }

        void OpenImage(Item it)
        {
            var f = Store.Get(it.Id);
            if (f?.Img == null) return;
            try { Process.Start(new ProcessStartInfo(Store.ImagePath(f.Img)) { UseShellExecute = true }); } catch { }
        }

        void ShowFileInFolder(Item it)
        {
            var f = Store.Get(it.Id);
            string first = (f?.Text ?? "").Split('\n').FirstOrDefault(p => p.Length > 0);
            if (first == null) return;
            try
            {
                if (File.Exists(first) || Directory.Exists(first)) Process.Start("explorer.exe", "/select,\"" + first + "\"");
                else if (Directory.Exists(Path.GetDirectoryName(first))) Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(first) + "\"");
            }
            catch { }
        }

        void OpenLink(Item it)
        {
            var f = Store.Get(it.Id);
            if (f?.Text == null || !Links.IsSingleUrl(f.Text)) return;
            try { Process.Start(new ProcessStartInfo(f.Text.Trim()) { UseShellExecute = true }); } catch { }
        }

        public void OpenDataFolder()
        {
            try { Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); } catch { }
        }

        public void SetPaused(bool p)
        {
            if (S.Paused == p) return;
            S.Paused = p;
            S.Save();
            if (!p) lastSeq = Native.GetClipboardSequenceNumber(); // don't grab what was copied while paused
            UpdateTray();
            Panel.Invalidate();
        }

        // ------------------------------------------------------------ settings
        public void OpenSettings(string page = null)
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(this, page);
            settingsForm.FormClosed += (o, e) => settingsForm = null;
            settingsForm.Show();
            settingsForm.Activate();
        }

        bool hotkeySuspended;
        public void SuspendHotkey() { if (msg == null) return; Native.UnregisterHotKey(msg.Handle, 1); hotkeySuspended = true; }
        public void ResumeHotkey() { if (hotkeySuspended && msg != null) { msg.SetHotkey(S.Hotkey); hotkeySuspended = false; } }

        public bool ApplyHotkey(Hotkey h)
        {
            if (msg.SetHotkey(h)) { S.Hotkey = h; S.Save(); UpdateTray(); return true; }
            msg.SetHotkey(S.Hotkey);
            return false;
        }

        public void SaveRules(string text)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(LinkCleaner.UserRulesPath, (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { Log.Crash(ex); }
            Cleaner = new LinkCleaner(text);
        }

        public void SettingsChanged()
        {
            S.Save();
            UpdateTray();
            Panel.Invalidate();
            worker.Post(() => { Housekeeping(); Ui(Panel.StoreChanged); });
        }

        public void CopyOriginal(Item it)
        {
            var f = Store.Get(it.Id);
            if (f?.Original == null) return;
            uint seq = Clip.Write(msg.Handle, new Clip.Payload { Text = f.Original });
            if (seq == 0) { Balloon("Pulp", "Another app is holding the clipboard. Try again in a moment.", ToolTipIcon.Warning, null); return; }
            ignoreSeq = seq;
            lastSeq = seq;
        }

        // clean exit on request: after this message is handled, not inside it
        public void ExitSoon()
        {
            var t = new System.Windows.Forms.Timer { Interval = 10 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); try { Exit(); } catch (Exception ex) { Log.Crash(ex); ExitThread(); } };
            t.Start();
        }

        void Exit()
        {
            settingsForm?.Close();
            Panel.SaveBounds();
            Panel.AllowClose = true;
            fgHook?.Dispose();
            tray.Visible = false;
            msg.Dispose();
            worker.Dispose();
            ExitThread();
        }

        // ------------------------------------------------------------ menus
        void ReleasePanel()
        {
            if (!Panel.IsHandleCreated) { Panel.SuppressHide = false; return; }
            Panel.BeginInvoke((Action)(() =>
            {
                if (modal > 0) return;
                Panel.SuppressHide = false;
                if (Panel.Visible && Form.ActiveForm == Panel) Panel.FocusSearch();
            }));
        }

        ToolStripMenuItem Mi(string text, Action click, bool check = false, bool isChecked = false, string keys = null)
        {
            var it = new ToolStripMenuItem(text) { ForeColor = Theme.Text, Checked = check && isChecked };
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            if (click != null) it.Click += (o, e) => { try { click(); } catch (Exception ex) { Log.Crash(ex); } };
            return it;
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            var open = Mi("Open Pulp", OpenFromTray, keys: S.Hotkey.ToString());
            open.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(Mi("Pause recording", () => SetPaused(!S.Paused), true, S.Paused));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Mi("Clean links", () => { S.CleanLinks = !S.CleanLinks; S.Save(); }, true, S.CleanLinks));
            menu.Items.Add(Mi("Keep screenshots and images", () => { S.SaveImages = !S.SaveImages; S.Save(); }, true, S.SaveImages));
            menu.Items.Add(Mi("Start with Windows", () => { Settings.StartWithWindows = !Settings.StartWithWindows; }, true, Settings.StartWithWindows));
            menu.Items.Add(Mi("Settings…", () => OpenSettings()));
            menu.Items.Add(Mi("Open data folder", OpenDataFolder));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Mi("Clear history…", ClearHistory));
            menu.Items.Add(Mi("Exit", Exit));
        }

        public void ShowMainMenu(Point screen)
        {
            Panel.SuppressHide = true;
            menu.Show(screen, ToolStripDropDownDirection.BelowLeft);
        }

        public void ShowRowMenu(Item it, Point screen)
        {
            var m = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = menu.Font, ShowImageMargin = false };
            bool canPaste = S.PasteOnEnter && ValidTarget(Panel.Target);
            IntPtr target = Panel.Target;
            if (canPaste) m.Items.Add(Mi("Paste", () => Use(it, false, false, target), keys: "Enter"));
            if (canPaste && it.Kind != Kind.Image) m.Items.Add(Mi(it.Kind == Kind.Files ? "Paste as file paths" : "Paste as plain text", () => Use(it, true, false, target), keys: "Shift+Enter"));
            m.Items.Add(Mi("Copy", () => Use(it, false, true, IntPtr.Zero), keys: canPaste ? "Ctrl+Enter" : "Enter"));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi(it.Pinned ? "Unpin" : "Pin", () => TogglePin(it), keys: "Ctrl+P"));
            if (it.Kind == Kind.Image)
            {
                m.Items.Add(Mi("Save as PNG…", () => SaveImageAs(it), keys: "Ctrl+S"));
                m.Items.Add(Mi("Open image", () => OpenImage(it)));
            }
            if (it.Kind == Kind.Files) m.Items.Add(Mi("Show in folder", () => ShowFileInFolder(it)));
            if (it.Kind == Kind.Link) m.Items.Add(Mi("Open link", () => OpenLink(it)));
            if (it.Original != null) m.Items.Add(Mi("Copy original link (before cleaning)", () => CopyOriginal(it)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Mi("Delete", () => DeleteItem(it), keys: "Del"));
            Panel.SuppressHide = true;
            m.Closed += (o, e) => ReleasePanel();
            m.Show(screen);
        }
    }
}
