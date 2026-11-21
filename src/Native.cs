using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulp
{
    static class Native
    {
        // ------------------------------------------------------------ clipboard
        public const uint CF_BITMAP = 2, CF_DIB = 8, CF_UNICODETEXT = 13, CF_HDROP = 15, CF_DIBV5 = 17;

        [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern bool EmptyClipboard();
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint fmt);
        [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint fmt);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr h);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr h);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint DragQueryFile(IntPtr hDrop, uint i, StringBuilder sb, uint cch);

        // ------------------------------------------------------------ windows
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string msg);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public static readonly int WM_SHOWPULP = RegisterWindowMessage("Pulp.ShowPanel.v1");

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            return GetClassName(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        // full exe path of the process that owns a window (null if unknown)
        public static string ProcessPathOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(h); }
        }

        public static int ProcessIdOf(IntPtr hwnd) { GetWindowThreadProcessId(hwnd, out uint pid); return (int)pid; }

        // brings our window to the front even when Windows' foreground lock objects
        public static void ForceForeground(IntPtr h)
        {
            if (SetForegroundWindow(h) && GetForegroundWindow() == h) return;
            IntPtr fg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(fg, out _), me = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me)
            {
                AttachThreadInput(me, fgThread, true);
                BringWindowToTop(h);
                SetForegroundWindow(h);
                AttachThreadInput(me, fgThread, false);
            }
            else SetForegroundWindow(h);
        }

        // ------------------------------------------------------------ keyboard input (for Ctrl+V only)
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public int type; public InputUnion u; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

        static INPUT Key(ushort vk, bool up)
        {
            uint flags = up ? 0x0002u : 0u; // KEYEVENTF_KEYUP
            if (vk == 0x5B || vk == 0x5C || vk == 0xA3 || vk == 0xA5) flags |= 0x0001; // extended: win, right ctrl/alt
            return new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags } } };
        }

        public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        // Ctrl+V, releasing any held Shift/Alt/Win first (with Ctrl already down, so a released Alt
        // doesn't open the target's menu bar)
        public static bool SendCtrlV()
        {
            var list = new System.Collections.Generic.List<INPUT> { Key(0x11, false) };
            foreach (var vk in new ushort[] { 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C }) // L/R shift, L/R alt, L/R win
                if (KeyDown(vk)) list.Add(Key(vk, true));
            list.Add(Key(0x56, false));
            list.Add(Key(0x56, true));
            list.Add(Key(0x11, true));
            var arr = list.ToArray();
            return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT))) == arr.Length;
        }
    }

    // tells us when another app's window comes to the front (an out-of-context WinEvent, not a keyboard hook)
    sealed class WinEventHook : IDisposable
    {
        delegate void Proc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, Proc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        readonly Proc proc;   // kept alive so the GC doesn't collect the callback
        IntPtr hook;

        WinEventHook(Action<IntPtr> onForeground)
        {
            proc = (h, ev, hwnd, obj, child, thread, time) => { try { if (obj == 0) onForeground(hwnd); } catch { } };
            hook = SetWinEventHook(3 /* EVENT_SYSTEM_FOREGROUND */, 3, IntPtr.Zero, proc, 0, 0, 0x0000 | 0x0002 /* OUTOFCONTEXT | SKIPOWNPROCESS */);
        }

        public static WinEventHook Foreground(Action<IntPtr> onForeground) => new WinEventHook(onForeground);

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
        }
    }

    // ---------------------------------------------------------------- SQLite (Windows' own winsqlite3.dll)
    static class Sql
    {
        const string Dll = "winsqlite3";
        const CallingConvention CC = CallingConvention.StdCall;
        public static readonly IntPtr TRANSIENT = new IntPtr(-1);
        public const int ROW = 100, DONE = 101;

        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_close(IntPtr db);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr sqlite3_errmsg16(IntPtr db);
        [DllImport(Dll, CallingConvention = CC)] public static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_changes(IntPtr db);
        [DllImport(Dll, CallingConvention = CC, CharSet = CharSet.Unicode)] public static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int nBytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_reset(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_bind_null(IntPtr stmt, int i);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_bind_int64(IntPtr stmt, int i, long v);
        [DllImport(Dll, CallingConvention = CC, CharSet = CharSet.Unicode)] public static extern int sqlite3_bind_text16(IntPtr stmt, int i, string v, int nBytes, IntPtr destructor);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_bind_blob(IntPtr stmt, int i, byte[] v, int n, IntPtr destructor);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CC)] public static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr sqlite3_column_text16(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_column_bytes16(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
        [DllImport(Dll, CallingConvention = CC)] public static extern int sqlite3_column_bytes(IntPtr stmt, int col);
    }

    sealed class SqliteDb : IDisposable
    {
        IntPtr db;
        public readonly object Gate = new object();

        public SqliteDb(string path)
        {
            // READWRITE | CREATE | FULLMUTEX
            int rc = Sql.sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 0x02 | 0x04 | 0x10000, IntPtr.Zero);
            if (rc != 0) { string m = Error(); Sql.sqlite3_close(db); db = IntPtr.Zero; throw new IOException("Couldn't open the history database (" + rc + "): " + m); }
            Sql.sqlite3_busy_timeout(db, 4000);
        }

        public string Error()
        {
            IntPtr p = Sql.sqlite3_errmsg16(db);
            return p == IntPtr.Zero ? "unknown" : Marshal.PtrToStringUni(p);
        }

        public Stmt Prepare(string sql)
        {
            int rc = Sql.sqlite3_prepare16_v2(db, sql, sql.Length * 2, out IntPtr st, IntPtr.Zero);
            if (rc != 0) throw new InvalidOperationException("SQL error " + rc + ": " + Error() + " in: " + sql);
            return new Stmt(this, st);
        }

        public int Exec(string sql, params object[] args)
        {
            lock (Gate) using (var st = Prepare(sql)) { st.Bind(args); while (st.Step()) { } return Sql.sqlite3_changes(db); }
        }

        public long Scalar(string sql, params object[] args)
        {
            lock (Gate) using (var st = Prepare(sql)) { st.Bind(args); return st.Step() && !st.IsNull(0) ? st.Long(0) : 0; }
        }

        public long LastId => Sql.sqlite3_last_insert_rowid(db);

        public void Dispose()
        {
            lock (Gate) { if (db != IntPtr.Zero) { Sql.sqlite3_close(db); db = IntPtr.Zero; } }
        }
    }

    sealed class Stmt : IDisposable
    {
        readonly SqliteDb db;
        IntPtr h;
        public Stmt(SqliteDb d, IntPtr st) { db = d; h = st; }

        public void Bind(params object[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                int n = i + 1, rc;
                switch (args[i])
                {
                    case null: rc = Sql.sqlite3_bind_null(h, n); break;
                    case string s: rc = Sql.sqlite3_bind_text16(h, n, s, s.Length * 2, Sql.TRANSIENT); break;
                    case byte[] b: rc = Sql.sqlite3_bind_blob(h, n, b, b.Length, Sql.TRANSIENT); break;
                    case bool v: rc = Sql.sqlite3_bind_int64(h, n, v ? 1 : 0); break;
                    case int v: rc = Sql.sqlite3_bind_int64(h, n, v); break;
                    case long v: rc = Sql.sqlite3_bind_int64(h, n, v); break;
                    default: rc = Sql.sqlite3_bind_text16(h, n, args[i].ToString(), args[i].ToString().Length * 2, Sql.TRANSIENT); break;
                }
                if (rc != 0) throw new InvalidOperationException("bind failed " + rc + ": " + db.Error());
            }
        }

        public bool Step()
        {
            int rc = Sql.sqlite3_step(h);
            if (rc == Sql.ROW) return true;
            if (rc == Sql.DONE) return false;
            throw new InvalidOperationException("SQL step error " + rc + ": " + db.Error());
        }

        public void Reset() => Sql.sqlite3_reset(h);
        public bool IsNull(int c) => Sql.sqlite3_column_type(h, c) == 5;
        public long Long(int c) => Sql.sqlite3_column_int64(h, c);
        public int Int(int c) => (int)Sql.sqlite3_column_int64(h, c);

        public string Text(int c)
        {
            IntPtr p = Sql.sqlite3_column_text16(h, c);
            if (p == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(p, Sql.sqlite3_column_bytes16(h, c) / 2);
        }

        public byte[] Blob(int c)
        {
            if (IsNull(c)) return null;
            IntPtr p = Sql.sqlite3_column_blob(h, c);
            int n = Sql.sqlite3_column_bytes(h, c);
            var b = new byte[n];
            if (n > 0 && p != IntPtr.Zero) Marshal.Copy(p, b, 0, n);
            return b;
        }

        public void Dispose()
        {
            if (h != IntPtr.Zero) { Sql.sqlite3_finalize(h); h = IntPtr.Zero; }
        }
    }
}
