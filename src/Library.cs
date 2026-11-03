// TS Launcher - finds thcrap, its games (games.js), languages (run configs) and thXX (lang) shortcuts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TSLauncher
{
    public sealed class LangEntry
    {
        public string Code, Config, Shortcut, CustomShortcut;
    }

    public sealed class Game
    {
        public string Id, Exe, CustomExe;
        public bool Unowned;
        public readonly List<LangEntry> Langs = new List<LangEntry>();

        public string Title { get { return Catalog.Title(Id); } }
        public string NumText { get { return Catalog.NumText(Id); } }
        public bool InCatalog { get { return Catalog.Has(Id); } }
        public LangEntry Lang(string code) { return code == null ? null : Langs.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)); }
        public bool ExeExists { get { return !string.IsNullOrEmpty(Exe) && File.Exists(Exe); } }
        public bool HasCustom { get { return !string.IsNullOrEmpty(CustomExe) || Langs.Any(l => l.CustomShortcut != null); } }
        public string Source { get { return Exe != null && Exe.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase) >= 0 ? "Steam" : "Local"; } }
    }

    public sealed class ThcrapInfo
    {
        public string Root, Loader, ConfigDir;
    }

    /// <summary>One language as listed in the Languages tab.</summary>
    public sealed class LangInfo
    {
        public string Code, Config;   // Config is null for shortcut-only languages
        public int Games;
        public bool Hidden;
        public HashSet<string> OnlyFor;   // null = every game
    }

    public sealed class LibraryResult
    {
        public List<LangInfo> AllLangs = new List<LangInfo>();
        public List<KeyValuePair<string, string>> Configs = new List<KeyValuePair<string, string>>();
        public LibraryResult Raw;   // the unfiltered scan this was made from
        public ThcrapInfo Thcrap;
        public List<Game> Games = new List<Game>();
        public List<Game> Unowned = new List<Game>();
        public int ConfigCount;
    }

    public static class Library
    {
        static readonly Regex ShortcutName = new Regex(@"^(?<id>th\d+)(?<custom>_custom)?\s*\((?<lang>[^)]+)\)$", RegexOptions.IgnoreCase);

        sealed class Shortcut { public string Path, Id, Lang, Target; public bool Custom; }

        /// <summary>TS Launcher's own folders: thousands of backup files, never any game shortcut.</summary>
        static bool IsOwnDataFolder(string dir)
        {
            if (Paths.Root == null) return false;
            foreach (var own in new[] { Paths.Covers, Paths.Backups, Paths.DefaultBackups, Paths.Logs })
                if (string.Equals(dir.TrimEnd('\\', '/'), own, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static IEnumerable<string> FilesUpTo(string dir, int depth)
        {
            var result = new List<string>();
            try { result.AddRange(Directory.GetFiles(dir)); } catch { }
            if (depth > 0)
            {
                string[] subs = new string[0];
                try { subs = Directory.GetDirectories(dir); } catch { }
                foreach (var s in subs)
                {
                    if (IsOwnDataFolder(s)) continue;
                    try { if ((new DirectoryInfo(s).Attributes & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    result.AddRange(FilesUpTo(s, depth - 1));
                }
            }
            return result;
        }

        static List<Shortcut> FindShortcuts(string dir, int depth)
        {
            var list = new List<Shortcut>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
            foreach (var f in FilesUpTo(dir, depth))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".lnk" && ext != ".exe") continue;
                var m = ShortcutName.Match(Path.GetFileNameWithoutExtension(f));
                if (!m.Success) continue;
                string target = f;
                if (ext == ".lnk") { var li = ShellLinks.Resolve(f); if (li != null && !string.IsNullOrEmpty(li.Target)) target = li.Target; }
                list.Add(new Shortcut { Path = f, Id = m.Groups["id"].Value.ToLowerInvariant(), Custom = m.Groups["custom"].Success, Lang = m.Groups["lang"].Value.Trim(), Target = target });
            }
            return list;
        }

        public static ThcrapInfo Inspect(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            string loader = new[] { Path.Combine(dir, "bin", "thcrap_loader.exe"), Path.Combine(dir, "thcrap_loader.exe") }.FirstOrDefault(File.Exists);
            if (loader == null) return null;
            string cfg = Path.Combine(dir, "config");
            if (!File.Exists(Path.Combine(cfg, "games.js")) && File.Exists(Path.Combine(dir, "games.js"))) cfg = dir;
            return new ThcrapInfo { Root = Path.GetFullPath(dir), Loader = loader, ConfigDir = cfg };
        }

        static string RootFromLoader(string loaderPath)
        {
            var d = Path.GetDirectoryName(loaderPath);
            if (string.Equals(Path.GetFileName(d), "bin", StringComparison.OrdinalIgnoreCase)) d = Path.GetDirectoryName(d);
            return d;
        }

        /// <summary>thcrap's own program (configurator), if present.</summary>
        public static string ThcrapExe(ThcrapInfo thc)
        {
            if (thc == null) return null;
            return new[] { Path.Combine(thc.Root, "thcrap.exe"), Path.Combine(thc.Root, "bin", "thcrap_configure_v3.exe"), Path.Combine(thc.Root, "bin", "thcrap_configure.exe") }
                .FirstOrDefault(File.Exists);
        }

        /// <summary>The saved folder first, then next to / above the launcher, then shortcuts and a shallow search.</summary>
        public static ThcrapInfo Find(string savedPath, string root)
        {
            var cands = new List<string>();
            if (!string.IsNullOrEmpty(savedPath)) cands.Add(savedPath);
            cands.Add(root); cands.Add(Path.Combine(root, "thcrap"));
            var parent = Path.GetDirectoryName(root);
            if (!string.IsNullOrEmpty(parent)) { cands.Add(parent); cands.Add(Path.Combine(parent, "thcrap")); }
            foreach (var c in cands) { var i = Inspect(c); if (i != null) return i; }
            foreach (var s in FindShortcuts(root, 3))
                if (s.Target != null && s.Target.EndsWith("thcrap_loader.exe", StringComparison.OrdinalIgnoreCase)) { var i = Inspect(RootFromLoader(s.Target)); if (i != null) return i; }
            foreach (var where in new[] { root, parent })
            {
                if (string.IsNullOrEmpty(where)) continue;
                var hit = FilesUpTo(where, 3).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "thcrap_loader.exe", StringComparison.OrdinalIgnoreCase));
                if (hit != null) { var i = Inspect(RootFromLoader(hit)); if (i != null) return i; }
            }
            return null;
        }

        static Dictionary<string, object> ReadJson(string path)
        {
            try { return Json.ParseObject(File.ReadAllText(path)); } catch { return null; }
        }

        /// <summary>Scans everything (runs on a background thread).</summary>
        public static LibraryResult Build(ThcrapInfo thc, string root, ICollection<string> hidden = null, IDictionary<string, HashSet<string>> onlyFor = null)
        {
            return Apply(BuildRaw(thc, root), hidden, onlyFor);
        }

        /// <summary>Scans the disk (runs on a background thread). No language preferences applied.</summary>
        public static LibraryResult BuildRaw(ThcrapInfo thc, string root)
        {
            var r = new LibraryResult { Thcrap = thc };
            if (thc == null) return r;
            var games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            Func<string, Game> get = id =>
            {
                Game g;
                if (!games.TryGetValue(id, out g)) { g = new Game { Id = id.ToLowerInvariant() }; games[id] = g; }
                return g;
            };

            // Run configurations = languages: any .js in config/ that has a "patches" list.
            var configs = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var f in Directory.GetFiles(thc.ConfigDir, "*.js").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    var n = Path.GetFileName(f).ToLowerInvariant();
                    if (n == "games.js" || n == "config.js") continue;
                    var j = ReadJson(f);
                    if (j != null && j.ContainsKey("patches")) configs.Add(new KeyValuePair<string, string>(Path.GetFileNameWithoutExtension(f), f));
                }
            }
            catch (Exception ex) { Log.Error("Library configs", ex); }

            // Games thcrap knows about (includes Steam copies).
            var gj = ReadJson(Path.Combine(thc.ConfigDir, "games.js"));
            if (gj != null)
                foreach (var kv in gj)
                {
                    var exe = Convert.ToString(kv.Value);
                    if (string.IsNullOrEmpty(exe)) continue;
                    exe = exe.Replace('/', '\\');
                    var name = kv.Key.ToLowerInvariant();
                    if (name.EndsWith("_custom")) get(name.Substring(0, name.Length - 7)).CustomExe = exe;
                    else get(name).Exe = exe;
                }
            foreach (var g in games.Values)
                foreach (var c in configs) g.Langs.Add(new LangEntry { Code = c.Key, Config = c.Value });

            // Shortcuts next to the launcher and in the thcrap folder.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in FindShortcuts(root, 3).Concat(FindShortcuts(thc.Root, 0)))
            {
                if (!seen.Add(s.Path)) continue;
                var g = get(s.Id);
                var l = g.Lang(s.Lang);
                if (l == null)
                {
                    l = new LangEntry { Code = s.Lang, Config = configs.Where(c => string.Equals(c.Key, s.Lang, StringComparison.OrdinalIgnoreCase)).Select(c => c.Value).FirstOrDefault() };
                    g.Langs.Add(l);
                }
                if (s.Custom) l.CustomShortcut = s.Path; else l.Shortcut = s.Path;
            }

            r.Configs = configs.ToList();
            r.Games = games.Values.OrderBy(g => Catalog.Number(g.Id)).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
            return r;
        }

        /// <summary>
        /// Applies the language preferences (hidden, per-game) to a raw scan, in memory: changing them
        /// doesn't need to touch the disk again. Returns new Game objects; the raw scan is left as is.
        /// </summary>
        public static LibraryResult Apply(LibraryResult raw, ICollection<string> hidden, IDictionary<string, HashSet<string>> onlyFor)
        {
            var r = new LibraryResult { Thcrap = raw.Thcrap, Configs = raw.Configs, Raw = raw };
            var games = raw.Games.Select(g =>
            {
                var c = new Game { Id = g.Id, Exe = g.Exe, CustomExe = g.CustomExe };
                foreach (var l in g.Langs)
                {
                    HashSet<string> ids;
                    if (onlyFor != null && onlyFor.TryGetValue(l.Code, out ids) && ids.Count > 0 && !ids.Contains(g.Id)) continue;
                    c.Langs.Add(new LangEntry { Code = l.Code, Config = l.Config, Shortcut = l.Shortcut, CustomShortcut = l.CustomShortcut });
                }
                return c;
            }).ToList();

            // Inventory of every language (for the language manager), then drop the hidden ones.
            var inv = new Dictionary<string, LangInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in raw.Configs) inv[c.Key] = new LangInfo { Code = c.Key, Config = c.Value };
            foreach (var g in games)
                foreach (var l in g.Langs)
                {
                    LangInfo li;
                    if (!inv.TryGetValue(l.Code, out li)) { li = new LangInfo { Code = l.Code, Config = l.Config }; inv[l.Code] = li; }
                    li.Games++;
                }
            foreach (var li in inv.Values)
            {
                li.Hidden = hidden != null && hidden.Contains(li.Code);
                HashSet<string> ids;
                if (onlyFor != null && onlyFor.TryGetValue(li.Code, out ids) && ids.Count > 0) li.OnlyFor = ids;
            }
            r.AllLangs = inv.Values.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
            r.ConfigCount = r.AllLangs.Count(x => !x.Hidden && x.Config != null);
            if (hidden != null && hidden.Count > 0)
                foreach (var g in games) g.Langs.RemoveAll(l => hidden.Contains(l.Code));

            r.Games = games.Where(g => g.Exe != null || g.Langs.Count > 0).ToList();
            var have = new HashSet<string>(r.Games.Select(g => g.Id));
            r.Unowned = Catalog.Games.Keys.Where(id => !have.Contains(id)).OrderBy(Catalog.Number)
                .Select(id => new Game { Id = id, Unowned = true }).ToList();
            return r;
        }
    }
}
