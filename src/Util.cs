// TS Launcher - shared helpers: paths, logging, JSON, crash-safe files, formatting, game catalog.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TSLauncher
{
    public static class AppInfo
    {
        public const string Name = "TS Launcher";
        public const string Version = "1.0";
    }

    /// <summary>Everything lives next to the exe (portable). File names are kept from the old launcher.</summary>
    public static class Paths
    {
        public static string Root { get; private set; }
        public static string Settings { get { return Path.Combine(Root, "launcher-settings.json"); } }
        public static string Playtime { get { return Path.Combine(Root, "playtime.json"); } }
        public static string Sessions { get { return Path.Combine(Root, "sessions.csv"); } }
        public static string Covers { get { return Path.Combine(Root, "covers"); } }
        public static string Backups { get { return Path.Combine(Root, "backups"); } }
        public static string DataBackups { get { return Path.Combine(Backups, "_launcher"); } }
        public static string Logs { get { return Path.Combine(Root, "logs"); } }
        public static string TrackLog { get { return Path.Combine(Root, "tracking.log"); } }
        public static string AppLog { get { return Path.Combine(Logs, "app.log"); } }
        public static string StartupMarker { get { return Path.Combine(Logs, "starting.lock"); } }

        public static void Init(string root)
        {
            Root = root.TrimEnd('\\', '/');
            try { Directory.CreateDirectory(Logs); } catch { }
        }
    }

    public static class Log
    {
        static readonly object Gate = new object();

        static void Write(string file, string msg)
        {
            lock (Gate)
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Exists && fi.Length > 512 * 1024) { File.Delete(file + ".old"); File.Move(file, file + ".old"); }
                    File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        public static void App(string msg) { Write(Paths.AppLog, msg); }
        public static void Track(string msg) { Write(Paths.TrackLog, msg); }
        public static void Error(string where, Exception ex) { Write(Paths.AppLog, "ERROR in " + where + ": " + ex); }

        public static string Tail(string file, int lines)
        {
            try
            {
                if (!File.Exists(file)) return "";
                var all = File.ReadAllLines(file);
                return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
            }
            catch { return ""; }
        }
    }

    /// <summary>JSON via the framework's JavaScriptSerializer, plus tolerant accessors.</summary>
    public static class Json
    {
        static JavaScriptSerializer NewSer() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 }; }

        public static object Parse(string text) { return NewSer().DeserializeObject(text); }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var o = Parse(text) as Dictionary<string, object>;
            if (o == null) throw new FormatException("JSON root is not an object");
            return o;
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static object[] Arr(object o) { var a = o as object[]; if (a != null) return a; var l = o as System.Collections.ArrayList; return l != null ? l.ToArray() : new object[0]; }

        public static object Get(Dictionary<string, object> d, string key) { object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        public static string Str(Dictionary<string, object> d, string key) { var v = Get(d, key); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static bool? Bool(Dictionary<string, object> d, string key) { var v = Get(d, key); if (v is bool) return (bool)v; return null; }
        public static double? Num(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null || v is bool || v is string && string.IsNullOrEmpty((string)v)) return null;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        /// <summary>Indented output so the files stay readable by humans.</summary>
        public static string Serialize(object o)
        {
            var sb = new StringBuilder();
            Write(sb, o, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o, int ind)
        {
            string pad = new string(' ', ind * 2), pad2 = new string(' ', ind * 2 + 2);
            if (o == null) { sb.Append("null"); return; }
            var dict = o as System.Collections.IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (System.Collections.DictionaryEntry e in dict)
                {
                    sb.Append(pad2); WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture)); sb.Append(": ");
                    Write(sb, e.Value, ind + 1);
                    if (++i < dict.Count) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(pad).Append('}');
                return;
            }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is double || o is float || o is decimal) { sb.Append(Convert.ToDouble(o, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (o is int || o is long || o is short || o is byte || o is uint || o is ulong) { sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
            var list = o as System.Collections.IEnumerable;
            if (list != null)
            {
                var items = list.Cast<object>().ToList();
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(pad2); Write(sb, items[i], ind + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(pad).Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>
    /// Crash-safe file writes: write to a temp file, flush to disk, then swap it in while keeping the
    /// previous version as .bak. A power cut or crash mid-write can never leave a half-written file.
    /// </summary>
    public static class SafeFile
    {
        public static void WriteAllText(string path, string text)
        {
            string tmp = path + ".tmp";
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, path + ".bak", true); return; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (PlatformNotSupportedException) { }
                File.Copy(path, path + ".bak", true);
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }

        /// <summary>
        /// Loads a data file, falling back to its .bak and then to the daily snapshots in
        /// backups\_launcher. A damaged original is kept aside as .corrupt-*. Returns null if nothing usable.
        /// </summary>
        public static T LoadWithRecovery<T>(string path, Func<string, T> parse, out string recoveredFrom) where T : class
        {
            recoveredFrom = null;
            bool existed = File.Exists(path);
            var candidates = new List<KeyValuePair<string, string>>();
            candidates.Add(new KeyValuePair<string, string>(path, null));
            candidates.Add(new KeyValuePair<string, string>(path + ".bak", "its automatic backup copy"));
            try
            {
                if (Directory.Exists(Paths.DataBackups))
                    foreach (var dir in Directory.GetDirectories(Paths.DataBackups).OrderByDescending(d => d, StringComparer.Ordinal))
                        candidates.Add(new KeyValuePair<string, string>(Path.Combine(dir, Path.GetFileName(path)), "the backup of " + Path.GetFileName(dir)));
            }
            catch { }

            bool originalFailed = false;
            foreach (var c in candidates)
            {
                if (!File.Exists(c.Key)) { if (c.Value == null) originalFailed = existed; continue; }
                try
                {
                    var text = File.ReadAllText(c.Key, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(text)) throw new FormatException("empty file");
                    var result = parse(text);
                    if (result != null)
                    {
                        if (c.Value != null && existed)
                        {
                            recoveredFrom = c.Value;
                            Log.App("Recovered " + Path.GetFileName(path) + " from " + c.Key);
                            try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
                        }
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    if (c.Value == null) { originalFailed = true; Log.App("Could not read " + c.Key + ": " + ex.Message); }
                }
            }
            if (originalFailed)
            {
                recoveredFrom = "";   // damaged and no backup: caller starts fresh, original kept aside
                try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
            }
            return null;
        }

        /// <summary>Once a day, copy the data files into backups\_launcher\yyyy-MM-dd (keeps the last 10 days).</summary>
        public static void DailySnapshot()
        {
            try
            {
                string dir = Path.Combine(Paths.DataBackups, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                if (Directory.Exists(dir)) return;
                bool any = false;
                foreach (var f in new[] { Paths.Settings, Paths.Playtime, Paths.Sessions })
                {
                    if (!File.Exists(f)) continue;
                    Directory.CreateDirectory(dir);
                    File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                    any = true;
                }
                if (!any) return;
                foreach (var old in Directory.GetDirectories(Paths.DataBackups).OrderByDescending(d => d, StringComparer.Ordinal).Skip(10))
                    try { Directory.Delete(old, true); } catch { }
            }
            catch (Exception ex) { Log.Error("DailySnapshot", ex); }
        }
    }

    public static class Fmt
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Duration(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, sec));
            if (t.TotalHours >= 1) return string.Format(Inv, "{0}h {1:00}m", (int)Math.Floor(t.TotalHours), t.Minutes);
            if (t.TotalMinutes >= 1) return string.Format(Inv, "{0} min", (int)Math.Floor(t.TotalMinutes));
            return sec >= 1 ? "< 1 min" : "0 min";
        }

        public static string Clock(TimeSpan t)
        {
            return string.Format(Inv, "{0}:{1:00}:{2:00}", (int)Math.Floor(t.TotalHours), t.Minutes, t.Seconds);
        }

        public static DateTime? ParseIso(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTime d;
            if (DateTime.TryParse(s, Inv, DateTimeStyles.RoundtripKind, out d)) return d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        public static string Ago(DateTime? when)
        {
            if (when == null) return null;
            int days = (DateTime.Now.Date - when.Value.Date).Days;
            if (days <= 0) return "today";
            if (days == 1) return "yesterday";
            if (days < 30) return days + " days ago";
            return when.Value.ToString("d MMM yyyy", Inv);
        }

        public static string Iso(DateTime d) { return d.ToString("o", Inv); }

        public static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }
    }

    public sealed class CatalogEntry
    {
        public string Title, Short, Color;
        public CatalogEntry(string t, string s, string c) { Title = t; Short = s; Color = c; }
    }

    public static class Catalog
    {
        public static readonly Dictionary<string, CatalogEntry> Games = new Dictionary<string, CatalogEntry>
        {
            { "th06",  new CatalogEntry("The Embodiment of Scarlet Devil", "EoSD", "#B3243A") },
            { "th07",  new CatalogEntry("Perfect Cherry Blossom", "PCB", "#C8648F") },
            { "th075", new CatalogEntry("Immaterial and Missing Power", "IaMP", "#8A5A9E") },
            { "th08",  new CatalogEntry("Imperishable Night", "IN", "#3B4C9B") },
            { "th09",  new CatalogEntry("Phantasmagoria of Flower View", "PoFV", "#4F9A55") },
            { "th095", new CatalogEntry("Shoot the Bullet", "StB", "#4F8FBF") },
            { "th10",  new CatalogEntry("Mountain of Faith", "MoF", "#C9822B") },
            { "th105", new CatalogEntry("Scarlet Weather Rhapsody", "SWR", "#3E8FB0") },
            { "th11",  new CatalogEntry("Subterranean Animism", "SA", "#8A4A2E") },
            { "th12",  new CatalogEntry("Undefined Fantastic Object", "UFO", "#2E7D74") },
            { "th123", new CatalogEntry("Hisoutensoku", "Soku", "#4A6FA5") },
            { "th125", new CatalogEntry("Double Spoiler", "DS", "#A64D79") },
            { "th128", new CatalogEntry("Fairy Wars", "GFW", "#3FA7C9") },
            { "th13",  new CatalogEntry("Ten Desires", "TD", "#6B5FB5") },
            { "th135", new CatalogEntry("Hopeless Masquerade", "HM", "#8C6D3F") },
            { "th14",  new CatalogEntry("Double Dealing Character", "DDC", "#9C2F5C") },
            { "th143", new CatalogEntry("Impossible Spell Card", "ISC", "#C24F3A") },
            { "th145", new CatalogEntry("Urban Legend in Limbo", "ULiL", "#5C5C8A") },
            { "th15",  new CatalogEntry("Legacy of Lunatic Kingdom", "LoLK", "#3A5BA0") },
            { "th155", new CatalogEntry("Antinomy of Common Flowers", "AoCF", "#B05F8A") },
            { "th16",  new CatalogEntry("Hidden Star in Four Seasons", "HSiFS", "#3E9C6B") },
            { "th165", new CatalogEntry("Violet Detector", "VD", "#7E57C2") },
            { "th17",  new CatalogEntry("Wily Beast and Weakest Creature", "WBaWC", "#B5462E") },
            { "th175", new CatalogEntry("Sunken Fossil World", "SFW", "#D08A2E") },
            { "th18",  new CatalogEntry("Unconnected Marketeers", "UM", "#C79A2B") },
            { "th185", new CatalogEntry("100th Black Market", "100BM", "#5A5A66") },
            { "th19",  new CatalogEntry("Unfinished Dream of All Living Ghost", "UDoALG", "#2F6FA8") },
            { "th20",  new CatalogEntry("Fossilized Wonders", "FW", "#8B6B45") },
        };

        public static bool Has(string id) { return id != null && Games.ContainsKey(id); }
        public static string Title(string id) { CatalogEntry e; return id != null && Games.TryGetValue(id, out e) ? e.Title : id; }
        public static string Short(string id) { CatalogEntry e; return id != null && Games.TryGetValue(id, out e) ? e.Short : id; }
        public static string ColorHex(string id) { CatalogEntry e; return id != null && Games.TryGetValue(id, out e) ? e.Color : "#555566"; }

        /// <summary>th06 -> 6, th143 -> 14.3, th20 -> 20.</summary>
        public static double Number(string id)
        {
            if (id == null || !id.StartsWith("th", StringComparison.OrdinalIgnoreCase)) return 999;
            string d = new string(id.Skip(2).TakeWhile(char.IsDigit).ToArray());
            if (d.Length == 0) return 999;
            if (d.Length >= 3) return double.Parse(d.Substring(0, 2) + "." + d.Substring(2), Fmt.Inv);
            return double.Parse(d, Fmt.Inv);
        }

        public static string NumText(string id)
        {
            double n = Number(id);
            return n >= 999 ? id : n.ToString(Fmt.Inv);
        }
    }

    public static class Langs
    {
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "English" }, { "fr", "Français" }, { "de", "Deutsch" }, { "es", "Español" }, { "it", "Italiano" },
            { "pt", "Português" }, { "pt-br", "Português (Brasil)" }, { "ru", "Русский" }, { "uk", "Українська" },
            { "pl", "Polski" }, { "cs", "Čeština" }, { "hu", "Magyar" }, { "nl", "Nederlands" }, { "sv", "Svenska" },
            { "fi", "Suomi" }, { "tr", "Türkçe" }, { "ja", "日本語" }, { "ko", "한국어" }, { "zh-hans", "简体中文" },
            { "zh-hant", "繁體中文" }, { "zh", "中文" }, { "vi", "Tiếng Việt" }, { "id", "Bahasa Indonesia" }, { "th", "ไทย" },
            { "ar", "العربية" }, { "he", "עברית" }, { "el", "Ελληνικά" }, { "ro", "Română" },
        };

        public static string Label(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            string n;
            if (Names.TryGetValue(code, out n)) return n;
            var m = System.Text.RegularExpressions.Regex.Match(code, @"^([a-zA-Z]{2,3})[-_](.+)$");
            if (m.Success && Names.TryGetValue(m.Groups[1].Value, out n)) return n + " (" + m.Groups[2].Value + ")";
            return code;
        }
    }
}
