// TS Launcher - thprac, Steam play time, moving / importing backups.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TSLauncher
{
    /// <summary>
    /// thprac (github.com/touhouworldcup/thprac), the practice tool. TS Launcher starts the game the usual way
    /// (thcrap, chosen language), and once the game's process is found runs "thprac.exe --attach &lt;pid&gt;".
    /// </summary>
    public static class Thprac
    {
        public const string Page = "https://github.com/touhouworldcup/thprac/releases";

        /// <summary>thprac covers the main games (6 to 20) and Great Fairy Wars.</summary>
        public static bool Supports(string id)
        {
            if (id == "th128") return true;
            double n = Catalog.Number(id);
            return n >= 6 && n <= 20 && Math.Abs(n - Math.Floor(n)) < 0.001;
        }

        static bool IsThprac(string file)
        {
            var n = Path.GetFileName(file);
            return n.StartsWith("thprac", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The saved path, else a thprac*.exe near TS Launcher, thcrap or the games (newest first).</summary>
        public static string Find(string saved, ThcrapInfo thc, IEnumerable<Game> games)
        {
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;
            var dirs = new List<string> { Paths.Root, Path.GetDirectoryName(Paths.Root) };
            if (thc != null) { dirs.Add(thc.Root); dirs.Add(Path.GetDirectoryName(thc.Root)); }
            foreach (var g in games) if (!string.IsNullOrEmpty(g.Exe)) { try { dirs.Add(Path.GetDirectoryName(g.Exe)); } catch { } }
            var found = new List<FileInfo>();
            foreach (var d in dirs.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in Directory.GetFiles(d, "thprac*.exe")) found.Add(new FileInfo(f));
                    foreach (var sub in Directory.GetDirectories(d))
                    {
                        if (Path.GetFileName(sub).IndexOf("thprac", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        foreach (var f in Directory.GetFiles(sub, "thprac*.exe")) found.Add(new FileInfo(f));
                    }
                }
                catch { }
            }
            var best = found.Where(f => IsThprac(f.FullName)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            return best == null ? null : best.FullName;
        }

        /// <summary>Attaches thprac to a running game. Returns null on success, else a message.</summary>
        public static string Attach(string thprac, int pid)
        {
            if (string.IsNullOrEmpty(thprac) || !File.Exists(thprac)) return "thprac.exe wasn't found (Settings > thprac).";
            try
            {
                Process.Start(new ProcessStartInfo(thprac, "--attach " + pid.ToString(CultureInfo.InvariantCulture))
                {
                    WorkingDirectory = Path.GetDirectoryName(thprac),
                    UseShellExecute = false,
                });
                Log.Track("thprac: attaching " + thprac + " to pid " + pid);
                return null;
            }
            catch (Exception ex) { Log.Track("thprac: could not start: " + ex.Message); return ex.Message; }
        }
    }

    /// <summary>Minimal reader for Valve's KeyValues text files (.vdf / .acf).</summary>
    public static class Vdf
    {
        public static Dictionary<string, object> Parse(string text)
        {
            int i = 0;
            return ReadBlock(text, ref i);
        }

        static Dictionary<string, object> ReadBlock(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var key = Token(s, ref i);
                if (key == null || key == "}") return d;
                var val = Token(s, ref i);
                if (val == null) return d;
                if (val == "{") d[key] = ReadBlock(s, ref i);
                else d[key] = val;
            }
        }

        static string Token(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
            if (i >= s.Length) return null;
            if (s[i] == '{' || s[i] == '}') return s[i++].ToString();
            if (s[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] == 'n' ? '\n' : s[i] == 't' ? '\t' : s[i]); }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}') i++;
            return s.Substring(start, i - start);
        }

        public static object Path(Dictionary<string, object> d, params string[] keys)
        {
            object cur = d;
            foreach (var k in keys)
            {
                var dict = cur as Dictionary<string, object>;
                if (dict == null || !dict.TryGetValue(k, out cur)) return null;
            }
            return cur;
        }
    }

    public sealed class SteamTime
    {
        public Game Game;
        public string AppId;
        public double Seconds;
        public DateTime? LastPlayed;
    }

    /// <summary>Reads the play time Steam recorded for your Steam copies (userdata\*\config\localconfig.vdf).</summary>
    public static class SteamPlaytime
    {
        public static string SteamRoot()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var p = k == null ? null : k.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p.Replace('/', '\\');
                }
            }
            catch { }
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var guess = string.IsNullOrEmpty(pf) ? null : System.IO.Path.Combine(pf, "Steam");
            return guess != null && Directory.Exists(guess) ? guess : null;
        }

        /// <summary>Steam app id of a game from steamapps\appmanifest_*.acf ("installdir" = its folder).</summary>
        public static string AppIdFor(string exe, Dictionary<string, Dictionary<string, string>> manifestCache)
        {
            if (string.IsNullOrEmpty(exe)) return null;
            int at = exe.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            var steamapps = exe.Substring(0, at + 10);
            var rest = exe.Substring(at + 11);
            if (!rest.StartsWith("common\\", StringComparison.OrdinalIgnoreCase)) return null;
            var installDir = rest.Substring(7).Split('\\')[0];
            Dictionary<string, string> byDir;
            if (!manifestCache.TryGetValue(steamapps, out byDir))
            {
                byDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var acf in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
                    {
                        try
                        {
                            var v = Vdf.Parse(File.ReadAllText(acf));
                            var st = Vdf.Path(v, "AppState") as Dictionary<string, object>;
                            var dir = st == null ? null : Vdf.Path(st, "installdir") as string;
                            var id = st == null ? null : Vdf.Path(st, "appid") as string;
                            if (dir != null && id != null) byDir[dir] = id;
                        }
                        catch { }
                    }
                }
                catch { }
                manifestCache[steamapps] = byDir;
            }
            string appId;
            return byDir.TryGetValue(installDir, out appId) ? appId : null;
        }

        /// <summary>Play time for each Steam game, taking the highest value across Steam accounts on this PC.</summary>
        public static List<SteamTime> Read(IEnumerable<Game> games, out string problem, string steamRoot = null)
        {
            problem = null;
            var result = new List<SteamTime>();
            var cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var wanted = new Dictionary<string, Game>();
            foreach (var g in games.Where(x => x.Source == "Steam"))
            {
                var id = AppIdFor(g.Exe, cache);
                if (id != null) wanted[id] = g;
            }
            if (wanted.Count == 0) { problem = "None of your games were recognised as Steam copies."; return result; }

            var root = steamRoot ?? SteamRoot();
            var userdata = root == null ? null : System.IO.Path.Combine(root, "userdata");
            if (userdata == null || !Directory.Exists(userdata)) { problem = "Couldn't find Steam's userdata folder on this PC."; return result; }

            var best = new Dictionary<string, SteamTime>();
            foreach (var user in Directory.GetDirectories(userdata))
            {
                var file = System.IO.Path.Combine(user, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                Dictionary<string, object> v;
                try { v = Vdf.Parse(File.ReadAllText(file, Encoding.UTF8)); }
                catch (Exception ex) { Log.App("Could not read " + file + ": " + ex.Message); continue; }
                var apps = Vdf.Path(v, "UserLocalConfigStore", "Software", "Valve", "Steam", "apps") as Dictionary<string, object>;
                if (apps == null) continue;
                foreach (var kv in wanted)
                {
                    var app = Vdf.Path(apps, kv.Key) as Dictionary<string, object>;
                    if (app == null) continue;
                    double minutes;
                    var pt = Vdf.Path(app, "Playtime") as string;
                    if (pt == null || !double.TryParse(pt, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) || minutes <= 0) continue;
                    DateTime? last = null;
                    long unix;
                    var lp = Vdf.Path(app, "LastPlayed") as string;
                    if (lp != null && long.TryParse(lp, out unix) && unix > 0) last = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime();
                    SteamTime cur;
                    if (!best.TryGetValue(kv.Key, out cur) || minutes * 60 > cur.Seconds)
                        best[kv.Key] = new SteamTime { Game = kv.Value, AppId = kv.Key, Seconds = minutes * 60, LastPlayed = last };
                }
            }
            if (best.Count == 0) problem = "Steam's files on this PC don't contain play time for your Touhou games.";
            return best.Values.OrderBy(x => Catalog.Number(x.Game.Id)).ToList();
        }
    }

    public static class BackupTools
    {
        static void CopyTree(string src, string dst, bool overwrite, ref int files)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var to = System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f));
                if (!overwrite && File.Exists(to)) continue;
                File.Copy(f, to, true);
                files++;
            }
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)), overwrite, ref files);
        }

        /// <summary>Copies every backup from one folder to another (keeps what's already there). Returns files copied.</summary>
        public static int Merge(string from, string to)
        {
            int files = 0;
            if (!Directory.Exists(from)) return 0;
            CopyTree(from, to, false, ref files);
            return files;
        }

        /// <summary>Finds the backups root inside a chosen folder: the folder itself, or a "backups" folder in it.</summary>
        public static string FindRoot(string chosen)
        {
            Func<string, bool> looksLike = d => Directory.Exists(d) && Directory.GetDirectories(d).Any(x =>
            {
                var n = System.IO.Path.GetFileName(x);
                return n == "_launcher" || Regex.IsMatch(n, @"^th\d+$", RegexOptions.IgnoreCase);
            });
            if (looksLike(chosen)) return chosen;
            var sub = System.IO.Path.Combine(chosen, "backups");
            return looksLike(sub) ? sub : null;
        }

        /// <summary>Newest daily copy of TS Launcher's own data (playtime.json, sessions.csv) in a backups root.</summary>
        public static string NewestLauncherSnapshot(string root)
        {
            var d = System.IO.Path.Combine(root, "_launcher");
            if (!Directory.Exists(d)) return null;
            return Directory.GetDirectories(d).OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault(x => File.Exists(System.IO.Path.Combine(x, "playtime.json")));
        }
    }
}
