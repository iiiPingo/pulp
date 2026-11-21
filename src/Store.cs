using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Pulp
{
    enum Kind { Text = 0, Link = 1, Files = 2, Image = 3 }

    // one row as the panel needs it (light: no rich formats, preview only)
    class Item
    {
        public long Id;
        public Kind Kind;
        public long Ts;            // last copied, unix ms
        public bool Pinned;
        public string Preview;     // first ~400 chars (text / url / paths / ocr)
        public string App;
        public string Img;         // image file name (hash.png)
        public int W, H, Lines, Len;
        public long Bytes;
        public string Original;    // link before cleaning (stage 2)
        public bool HasOcr;
    }

    // everything needed to put an item back on the clipboard
    class Full
    {
        public long Id;
        public Kind Kind;
        public string Text;
        public byte[] Html, Rtf;
        public string Img;
        public string Original;
    }

    class Store : IDisposable
    {
        public readonly string Dir, ImgDir, ThumbDir;
        readonly SqliteDb db;

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public Store(string dir)
        {
            Dir = dir;
            ImgDir = Path.Combine(dir, "images");
            ThumbDir = Path.Combine(dir, "thumbs");
            Directory.CreateDirectory(ImgDir);
            Directory.CreateDirectory(ThumbDir);
            db = new SqliteDb(Path.Combine(dir, "history.db"));
            db.Exec("PRAGMA auto_vacuum=INCREMENTAL");   // only takes effect on a new file
            db.Exec("PRAGMA journal_mode=WAL");
            db.Exec("PRAGMA synchronous=NORMAL");
            db.Exec("PRAGMA temp_store=MEMORY");
            db.Exec("PRAGMA cache_size=-4000");          // ~4 MB page cache, keeps RAM low
            db.Exec(@"CREATE TABLE IF NOT EXISTS items(
                id INTEGER PRIMARY KEY,
                kind INTEGER NOT NULL,
                ts INTEGER NOT NULL,
                created INTEGER NOT NULL,
                pinned INTEGER NOT NULL DEFAULT 0,
                hash TEXT NOT NULL,
                text TEXT,
                html BLOB,
                rtf BLOB,
                rich INTEGER NOT NULL DEFAULT 0,
                original TEXT,
                app TEXT,
                img TEXT,
                w INTEGER NOT NULL DEFAULT 0,
                h INTEGER NOT NULL DEFAULT 0,
                bytes INTEGER NOT NULL DEFAULT 0,
                lines INTEGER NOT NULL DEFAULT 0,
                len INTEGER NOT NULL DEFAULT 0,
                ocr INTEGER NOT NULL DEFAULT 0)");
            db.Exec("CREATE UNIQUE INDEX IF NOT EXISTS ix_hash ON items(hash)");
            db.Exec("CREATE INDEX IF NOT EXISTS ix_order ON items(pinned, ts)");
            db.Exec("CREATE INDEX IF NOT EXISTS ix_kind ON items(kind, pinned, ts)");
            db.Exec("CREATE INDEX IF NOT EXISTS ix_rich ON items(ts) WHERE rich=1");
            db.Exec("CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT)");
        }

        public void Dispose() => db.Dispose();

        public static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(data);
                var sb = new StringBuilder(40);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string TextHash(string text) => "t:" + Sha1(Encoding.UTF8.GetBytes(text));
        static string FilesHash(string[] paths) => "f:" + Sha1(Encoding.UTF8.GetBytes(string.Join("\n", paths).ToLowerInvariant()));

        static int CountLines(string s)
        {
            int n = 1;
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') n++;
            if (s.EndsWith("\n")) n--;
            return Math.Max(1, n);
        }

        // ------------------------------------------------------------ writes
        // insert or bump to the top; returns the row id
        long Upsert(string hash, Kind kind, string text, byte[] html, byte[] rtf, string app, string original,
                    string img = null, int w = 0, int h = 0, long bytes = 0)
        {
            long now = Now;
            lock (db.Gate)
            {
                long id = 0;
                using (var st = db.Prepare("SELECT id FROM items WHERE hash=?"))
                {
                    st.Bind(hash);
                    if (st.Step()) id = st.Long(0);
                }
                bool rich = html != null || rtf != null;
                int lines = text == null ? 0 : CountLines(text), len = text?.Length ?? 0;
                if (id != 0)
                {
                    if (kind == Kind.Image)
                        db.Exec("UPDATE items SET ts=?, app=coalesce(?, app) WHERE id=?", now, app, id);
                    else
                        db.Exec("UPDATE items SET ts=?, kind=?, app=coalesce(?, app), html=?, rtf=?, rich=?, original=coalesce(?, original) WHERE id=?",
                            now, (int)kind, app, html, rtf, rich, original, id);
                    return id;
                }
                db.Exec(@"INSERT INTO items(kind, ts, created, hash, text, html, rtf, rich, original, app, img, w, h, bytes, lines, len)
                          VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                    (int)kind, now, now, hash, text, html, rtf, rich, original, app, img, w, h, bytes, lines, len);
                return db.LastId;
            }
        }

        public long AddText(string text, Kind kind, byte[] html, byte[] rtf, string app, string original = null)
            => Upsert(TextHash(text), kind, text, html, rtf, app, original);

        public long AddFiles(string[] paths, string app)
            => Upsert(FilesHash(paths), Kind.Files, string.Join("\n", paths), null, null, app, null);

        public long FindHash(string hash) => db.Scalar("SELECT id FROM items WHERE hash=?", hash);

        public long AddImage(string pixelHash, string img, int w, int h, long bytes, string app)
            => Upsert("i:" + pixelHash, Kind.Image, null, null, null, app, null, img, w, h, bytes);

        public void Touch(long id) => db.Exec("UPDATE items SET ts=? WHERE id=?", Now, id);

        public void SetPinned(long id, bool pin) => db.Exec("UPDATE items SET pinned=? WHERE id=?", pin, id);

        public void SetOriginal(long id, string original) => db.Exec("UPDATE items SET original=? WHERE id=?", original, id);

        public void Delete(long id)
        {
            string img = null;
            lock (db.Gate)
            {
                using (var st = db.Prepare("SELECT img FROM items WHERE id=?")) { st.Bind(id); if (st.Step()) img = st.Text(0); }
                db.Exec("DELETE FROM items WHERE id=?", id);
            }
            DeleteImageFiles(img);
        }

        // removes everything that isn't pinned; returns how many went
        public int ClearUnpinned()
        {
            var imgs = new List<string>();
            int n;
            lock (db.Gate)
            {
                using (var st = db.Prepare("SELECT img FROM items WHERE pinned=0 AND img IS NOT NULL"))
                    while (st.Step()) imgs.Add(st.Text(0));
                n = db.Exec("DELETE FROM items WHERE pinned=0");
            }
            foreach (var i in imgs) DeleteImageFiles(i);
            try { db.Exec("PRAGMA incremental_vacuum"); } catch { }
            return n;
        }

        public void DeleteImageFiles(string img)
        {
            if (string.IsNullOrEmpty(img)) return;
            try { File.Delete(Path.Combine(ImgDir, img)); } catch { }
            try { File.Delete(Path.Combine(ThumbDir, img)); } catch { }
        }

        public string ImagePath(string img) => Path.Combine(ImgDir, img);
        public string ThumbPath(string img) => Path.Combine(ThumbDir, img);

        // ------------------------------------------------------------ reads
        const string Cols = "id, kind, ts, pinned, substr(text,1,400), app, img, w, h, bytes, lines, len, original, ocr";

        static Item ReadItem(Stmt st) => new Item
        {
            Id = st.Long(0), Kind = (Kind)st.Int(1), Ts = st.Long(2), Pinned = st.Long(3) != 0,
            Preview = st.Text(4) ?? "", App = st.Text(5), Img = st.Text(6),
            W = st.Int(7), H = st.Int(8), Bytes = st.Long(9), Lines = st.Int(10), Len = st.Int(11),
            Original = st.Text(12), HasOcr = st.Int(13) == 1,
        };

        static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        static readonly Dictionary<string, Kind> KindWords = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase)
        {
            { "is:image", Kind.Image }, { "is:img", Kind.Image }, { "is:link", Kind.Link }, { "is:file", Kind.Files }, { "is:text", Kind.Text },
        };

        // newest first, pinned on top; every word of the query must match somewhere
        public List<Item> List(string query, int offset, int limit)
        {
            var where = new List<string>();
            var args = new List<object>();
            bool pinnedOnly = false;
            foreach (var w in (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (KindWords.TryGetValue(w, out Kind k)) { where.Add("kind=?"); args.Add((int)k); continue; }
                if (w.Equals("is:pinned", StringComparison.OrdinalIgnoreCase)) { pinnedOnly = true; continue; }
                where.Add("(text LIKE ? ESCAPE '\\' OR app LIKE ? ESCAPE '\\' OR original LIKE ? ESCAPE '\\')");
                string p = "%" + EscapeLike(w) + "%";
                args.Add(p); args.Add(p); args.Add(p);
            }
            if (pinnedOnly) where.Add("pinned=1");
            string sql = "SELECT " + Cols + " FROM items" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                       + " ORDER BY pinned DESC, ts DESC, id DESC LIMIT ? OFFSET ?";
            args.Add(limit); args.Add(offset);
            var list = new List<Item>();
            lock (db.Gate)
                using (var st = db.Prepare(sql))
                {
                    st.Bind(args.ToArray());
                    while (st.Step()) list.Add(ReadItem(st));
                }
            return list;
        }

        public Item GetItem(long id)
        {
            lock (db.Gate)
                using (var st = db.Prepare("SELECT " + Cols + " FROM items WHERE id=?"))
                {
                    st.Bind(id);
                    return st.Step() ? ReadItem(st) : null;
                }
        }

        public Full Get(long id)
        {
            lock (db.Gate)
                using (var st = db.Prepare("SELECT id, kind, text, html, rtf, img, original FROM items WHERE id=?"))
                {
                    st.Bind(id);
                    if (!st.Step()) return null;
                    return new Full { Id = st.Long(0), Kind = (Kind)st.Int(1), Text = st.Text(2), Html = st.Blob(3), Rtf = st.Blob(4), Img = st.Text(5), Original = st.Text(6) };
                }
        }

        public long Count() => db.Scalar("SELECT count(*) FROM items");
        public void ImageUsage(out long count, out long bytes)
        {
            lock (db.Gate)
                using (var q = db.Prepare("SELECT count(*), coalesce(sum(bytes),0) FROM items WHERE kind=3"))
                {
                    q.Step();
                    count = q.Long(0);
                    bytes = q.Long(1);
                }
        }
        public long ImageBytes() => db.Scalar("SELECT coalesce(sum(bytes),0) FROM items WHERE kind=3");

        public string GetMeta(string k)
        {
            lock (db.Gate) using (var st = db.Prepare("SELECT v FROM meta WHERE k=?")) { st.Bind(k); return st.Step() ? st.Text(0) : null; }
        }
        public void SetMeta(string k, string v) => db.Exec("INSERT OR REPLACE INTO meta(k, v) VALUES(?, ?)", k, v);

        // ------------------------------------------------------------ OCR bookkeeping (stage 3)
        public List<Item> PendingOcr(int limit)
        {
            var list = new List<Item>();
            lock (db.Gate)
                using (var st = db.Prepare("SELECT " + Cols + " FROM items WHERE kind=3 AND ocr=0 ORDER BY ts DESC LIMIT ?"))
                {
                    st.Bind(limit);
                    while (st.Step()) list.Add(ReadItem(st));
                }
            return list;
        }
        public void SetOcr(long id, string text, int state) => db.Exec("UPDATE items SET text=?, ocr=? WHERE id=?", text, state, id);

        // ------------------------------------------------------------ limits + housekeeping
        // oldest unpinned images go first until the total fits the cap; pinned are never touched
        public int EnforceImageCap(long capBytes)
        {
            int removed = 0;
            long total = ImageBytes();
            while (total > capBytes)
            {
                var batch = new List<Tuple<long, string, long>>();
                lock (db.Gate)
                    using (var st = db.Prepare("SELECT id, img, bytes FROM items WHERE kind=3 AND pinned=0 ORDER BY ts ASC, id ASC LIMIT 50"))
                        while (st.Step()) batch.Add(Tuple.Create(st.Long(0), st.Text(1), st.Long(2)));
                if (batch.Count == 0) break;
                foreach (var b in batch)
                {
                    if (total <= capBytes) break;
                    db.Exec("DELETE FROM items WHERE id=?", b.Item1);
                    DeleteImageFiles(b.Item2);
                    total -= b.Item3;
                    removed++;
                }
            }
            return removed;
        }

        public void Maintain(int maxItems, int keepRich)
        {
            // text/link/file history cap (images have their own size cap)
            db.Exec("DELETE FROM items WHERE id IN (SELECT id FROM items WHERE pinned=0 AND kind<>3 ORDER BY ts DESC, id DESC LIMIT -1 OFFSET ?)", maxItems);
            // keep formatting (HTML/RTF) only for recent items and pins, so the file stays small
            db.Exec("UPDATE items SET html=NULL, rtf=NULL, rich=0 WHERE rich=1 AND pinned=0 AND id NOT IN (SELECT id FROM items WHERE rich=1 ORDER BY ts DESC, id DESC LIMIT ?)", keepRich);
            try { db.Exec("PRAGMA incremental_vacuum(2000)"); } catch { }
        }

        // image files on disk that the database doesn't know (and rows whose file vanished)
        public void CleanOrphans()
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<long>();
            lock (db.Gate)
                using (var st = db.Prepare("SELECT id, img FROM items WHERE kind=3"))
                    while (st.Step())
                    {
                        string img = st.Text(1);
                        if (string.IsNullOrEmpty(img)) { missing.Add(st.Long(0)); continue; }
                        known.Add(img);
                        if (!File.Exists(Path.Combine(ImgDir, img))) missing.Add(st.Long(0));
                    }
            foreach (var id in missing) db.Exec("DELETE FROM items WHERE id=?", id);
            foreach (var dir in new[] { ImgDir, ThumbDir })
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    string name = Path.GetFileName(f);
                    bool old = (DateTime.Now - File.GetLastWriteTime(f)).TotalMinutes > 5; // don't race a save in progress
                    if (old && (!known.Contains(name) || name.EndsWith(".tmp"))) try { File.Delete(f); } catch { }
                }
        }
    }
}
