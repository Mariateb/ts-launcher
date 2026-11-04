// TS Launcher - persistent data: settings, play stats, session history. Same files as the old launcher.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TSLauncher
{
    public sealed class AppSettings
    {
        public const string TelanusId = "76561198029739532";

        public string ThcrapPath;
        public bool CloseAfterLaunch = true;
        public Dictionary<string, string> LastLang = new Dictionary<string, string>();
        public string SgdbKey;
        public string SgdbAuthor = TelanusId;
        public Dictionary<string, long> SgdbIds = new Dictionary<string, long>();
        public Dictionary<string, bool> CoverMisses = new Dictionary<string, bool>();
        public Dictionary<string, string> CoverChoice = new Dictionary<string, string>();
        public double CardScale = 1.0;
        public string SortMode = "release";
        public HashSet<string> Favorites = new HashSet<string>();
        public bool ShowUnowned;
        public string Theme = "purple";
        public bool SoundsOn = true;
        public int SoundVolume = 35;
        public bool SetupDone;
        public Dictionary<string, string> LangNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> HiddenLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Language code -> the only games it is offered for (no entry = every game).</summary>
        public Dictionary<string, HashSet<string>> LangGames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        public string Filter = "all";
        public double? WinLeft, WinTop, WinWidth, WinHeight;
        public bool WinMaximized;
        public bool CheckUpdates = true;
        public string ThpracPath;
        public HashSet<string> ThpracGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string BackupDir;   // null = backups folder next to the exe
        /// <summary>Picture behind the library: "focus" (the game you're on), "none", or "game:thXX" (always that game).</summary>
        public string Background = "focus";
        /// <summary>"Give me a game to play": language to launch in (null = each game's last language),
        /// games left out of the draw, and the last pick (less likely to come up twice in a row).</summary>
        public string RandomLang;
        public HashSet<string> RandomExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string LastRandom;

        /// <summary>When true (safe mode) nothing is ever written back to disk.</summary>
        public bool ReadOnly;

        public static AppSettings Load(out string notice)
        {
            notice = null;
            bool existed = File.Exists(Paths.Settings);
            string from;
            var s = SafeFile.LoadWithRecovery(Paths.Settings, text => FromJson(text), out from);
            if (s == null)
            {
                s = new AppSettings();
                if (from == "") notice = "Your settings file was damaged and no backup could be read, so TS Launcher started with default settings. The damaged file was kept next to it.";
            }
            else if (from != null) notice = "Your settings file was damaged, so it was restored from " + from + ".";
            if (existed) s.SetupDone = true;   // people coming from the old launcher already set things up
            return s;
        }

        static AppSettings FromJson(string text)
        {
            var j = Json.ParseObject(text);
            var s = new AppSettings();
            s.ThcrapPath = Json.Str(j, "ThcrapPath");
            s.CloseAfterLaunch = Json.Bool(j, "CloseAfterLaunch") ?? true;
            foreach (var kv in Dict(j, "LastLang")) if (kv.Value != null) s.LastLang[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            s.SgdbKey = Json.Str(j, "SgdbKey");
            var author = Json.Get(j, "SgdbAuthor");
            if (author != null) s.SgdbAuthor = Convert.ToString(author, CultureInfo.InvariantCulture);
            foreach (var kv in Dict(j, "SgdbIds")) { try { s.SgdbIds[kv.Key] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture); } catch { } }
            foreach (var kv in Dict(j, "CoverMisses")) if (kv.Value is bool) s.CoverMisses[kv.Key] = (bool)kv.Value;
            foreach (var kv in Dict(j, "CoverChoice")) if (kv.Value != null) s.CoverChoice[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            s.CardScale = Math.Max(0.6, Math.Min(1.6, Json.Num(j, "CardScale") ?? 1.0));
            var sort = Json.Str(j, "SortMode");
            if (sort == "release" || sort == "recent" || sort == "most" || sort == "name") s.SortMode = sort;
            foreach (var kv in Dict(j, "Favorites")) if (kv.Value is bool && (bool)kv.Value) s.Favorites.Add(kv.Key);
            s.ShowUnowned = Json.Bool(j, "ShowUnowned") ?? false;
            s.Theme = Json.Str(j, "Theme") ?? "purple";
            s.SoundsOn = Json.Bool(j, "SoundsOn") ?? true;
            s.SoundVolume = (int)Math.Max(0, Math.Min(100, Json.Num(j, "SoundVolume") ?? 35));
            s.SetupDone = Json.Bool(j, "SetupDone") ?? true;
            foreach (var kv in Dict(j, "LangNames")) if (kv.Value != null) s.LangNames[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            foreach (var kv in Dict(j, "HiddenLangs")) if (kv.Value is bool && (bool)kv.Value) s.HiddenLangs.Add(kv.Key);
            foreach (var kv in Dict(j, "LangGames"))
            {
                var ids = new HashSet<string>(Json.Arr(kv.Value).Where(x => x != null).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture).ToLowerInvariant()));
                if (ids.Count > 0) s.LangGames[kv.Key] = ids;
            }
            var filter = Json.Str(j, "Filter");
            if (filter == "folder") filter = "local";   // renamed in 1.2.1
            if (filter == "all" || filter == "played" || filter == "never" || filter == "steam" || filter == "local") s.Filter = filter;
            var win = Json.Obj(Json.Get(j, "Window"));
            if (win != null)
            {
                s.WinLeft = Json.Num(win, "Left"); s.WinTop = Json.Num(win, "Top");
                s.WinWidth = Json.Num(win, "Width"); s.WinHeight = Json.Num(win, "Height");
                s.WinMaximized = Json.Bool(win, "Maximized") ?? false;
            }
            s.CheckUpdates = Json.Bool(j, "CheckUpdates") ?? true;
            s.ThpracPath = Json.Str(j, "ThpracPath");
            foreach (var kv in Dict(j, "ThpracGames")) if (kv.Value is bool && (bool)kv.Value) s.ThpracGames.Add(kv.Key);
            s.RandomLang = Json.Str(j, "RandomLang");
            if (string.IsNullOrWhiteSpace(s.RandomLang)) s.RandomLang = null;
            foreach (var kv in Dict(j, "RandomExclude")) if (kv.Value is bool && (bool)kv.Value) s.RandomExclude.Add(kv.Key);
            s.LastRandom = Json.Str(j, "LastRandom");
            var bg = Json.Str(j, "Background");
            if (bg == "focus" || bg == "none" || (bg != null && bg.StartsWith("game:") && bg.Length > 5)) s.Background = bg;
            s.BackupDir = Json.Str(j, "BackupDir");
            if (string.IsNullOrWhiteSpace(s.BackupDir)) s.BackupDir = null;
            return s;
        }

        static IEnumerable<KeyValuePair<string, object>> Dict(Dictionary<string, object> j, string key)
        {
            var d = Json.Obj(Json.Get(j, key));
            return d ?? Enumerable.Empty<KeyValuePair<string, object>>();
        }

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                var o = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    { "ThcrapPath", ThcrapPath },
                    { "CloseAfterLaunch", CloseAfterLaunch },
                    { "LastLang", new SortedDictionary<string, string>(LastLang) },
                    { "SgdbKey", SgdbKey },
                    { "SgdbAuthor", SgdbAuthor ?? "" },
                    { "SgdbIds", new SortedDictionary<string, long>(SgdbIds) },
                    { "CoverMisses", new SortedDictionary<string, bool>(CoverMisses) },
                    { "CoverChoice", new SortedDictionary<string, string>(CoverChoice) },
                    { "CardScale", CardScale },
                    { "SortMode", SortMode },
                    { "Favorites", Favorites.OrderBy(x => x).ToDictionary(x => x, x => true) },
                    { "ShowUnowned", ShowUnowned },
                    { "Theme", Theme },
                    { "SoundsOn", SoundsOn },
                    { "SoundVolume", SoundVolume },
                    { "SetupDone", SetupDone },
                    { "LangNames", new SortedDictionary<string, string>(LangNames, StringComparer.OrdinalIgnoreCase) },
                    { "HiddenLangs", HiddenLangs.OrderBy(x => x).ToDictionary(x => x, x => true) },
                    { "LangGames", new SortedDictionary<string, List<string>>(LangGames.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(x => x).ToList()), StringComparer.OrdinalIgnoreCase) },
                    { "Filter", Filter },
                    { "CheckUpdates", CheckUpdates },
                    { "ThpracPath", ThpracPath },
                    { "ThpracGames", ThpracGames.OrderBy(x => x).ToDictionary(x => x, x => true) },
                    { "BackupDir", BackupDir },
                    { "Background", Background },
                    { "RandomLang", RandomLang },
                    { "RandomExclude", RandomExclude.OrderBy(x => x).ToDictionary(x => x, x => true) },
                    { "LastRandom", LastRandom },
                };
                if (WinWidth != null && WinHeight != null)
                    o["Window"] = new SortedDictionary<string, object>
                    {
                        { "Left", Math.Round(WinLeft ?? 0) }, { "Top", Math.Round(WinTop ?? 0) },
                        { "Width", Math.Round(WinWidth.Value) }, { "Height", Math.Round(WinHeight.Value) }, { "Maximized", WinMaximized },
                    };
                SafeFile.WriteAllText(Paths.Settings, Json.Serialize(o));
            }
            catch (Exception ex) { Log.Error("Settings.Save", ex); }
        }
    }

    public sealed class GameStat
    {
        public double Seconds;
        public int Launches;
        public string LastPlayed;
        public DateTime? LastPlayedDate { get { return Fmt.ParseIso(LastPlayed); } }
        public bool Played { get { return Launches > 0 || Seconds >= 1; } }
    }

    /// <summary>
    /// playtime.json. Always re-read before writing so that two launcher windows (one hidden in the
    /// tray timing a game) can't overwrite each other's numbers.
    /// </summary>
    public sealed class StatsStore
    {
        public Dictionary<string, GameStat> All = new Dictionary<string, GameStat>();
        public bool ReadOnly;
        public string Notice;

        public GameStat Get(string id) { GameStat s; return id != null && All.TryGetValue(id, out s) ? s : null; }

        public static Dictionary<string, GameStat> Parse(string text)
        {
            var j = Json.ParseObject(text);
            var d = new Dictionary<string, GameStat>();
            foreach (var kv in j)
            {
                var g = Json.Obj(kv.Value);
                if (g == null) continue;
                d[kv.Key] = new GameStat
                {
                    Seconds = Json.Num(g, "Seconds") ?? 0,
                    Launches = (int)(Json.Num(g, "Launches") ?? 0),
                    LastPlayed = Json.Str(g, "LastPlayed"),
                };
            }
            return d;
        }

        public void Load()
        {
            string from;
            var d = SafeFile.LoadWithRecovery(Paths.Playtime, Parse, out from);
            All = d ?? new Dictionary<string, GameStat>();
            if (from == "") Notice = "Your play-time file was damaged and had no readable backup, so play time starts again from zero. The damaged file was kept.";
            else if (from != null) Notice = "Your play-time file was damaged, so it was restored from " + from + ".";
        }

        /// <summary>Raises a game's numbers to at least these values (for imports: nothing gets counted twice).</summary>
        public void RaiseTo(Dictionary<string, GameStat> incoming)
        {
            if (ReadOnly || incoming.Count == 0) return;
            lock (this)
            {
                try
                {
                    string ignored;
                    var fresh = File.Exists(Paths.Playtime) ? SafeFile.LoadWithRecovery(Paths.Playtime, Parse, out ignored) : null;
                    if (fresh != null) All = fresh;
                    foreach (var kv in incoming)
                    {
                        GameStat g;
                        if (!All.TryGetValue(kv.Key, out g)) { g = new GameStat(); All[kv.Key] = g; }
                        g.Seconds = Math.Max(g.Seconds, kv.Value.Seconds);
                        g.Launches = Math.Max(g.Launches, kv.Value.Launches);
                        var a = g.LastPlayedDate; var b = kv.Value.LastPlayedDate;
                        if (b != null && (a == null || b.Value > a.Value)) g.LastPlayed = kv.Value.LastPlayed;
                    }
                    Write();
                }
                catch (Exception ex) { Log.Error("Stats.RaiseTo", ex); }
            }
        }

        void Write()
        {
            var o = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in All)
                o[kv.Key] = new SortedDictionary<string, object> { { "Seconds", Math.Round(kv.Value.Seconds, 1) }, { "Launches", kv.Value.Launches }, { "LastPlayed", kv.Value.LastPlayed ?? "" } };
            SafeFile.WriteAllText(Paths.Playtime, Json.Serialize(o));
        }

        public void Update(string id, double addSeconds, int addLaunches)
        {
            if (ReadOnly) return;
            lock (this)
            {
                try
                {
                    string ignored;
                    var fresh = File.Exists(Paths.Playtime) ? SafeFile.LoadWithRecovery(Paths.Playtime, Parse, out ignored) : null;
                    if (fresh != null) All = fresh;
                    GameStat g;
                    if (!All.TryGetValue(id, out g)) { g = new GameStat(); All[id] = g; }
                    g.Seconds += Math.Max(0, addSeconds);
                    g.Launches += addLaunches;
                    g.LastPlayed = Fmt.Iso(DateTime.Now);
                    var o = new SortedDictionary<string, object>(StringComparer.Ordinal);
                    foreach (var kv in All)
                        o[kv.Key] = new SortedDictionary<string, object> { { "Seconds", Math.Round(kv.Value.Seconds, 1) }, { "Launches", kv.Value.Launches }, { "LastPlayed", kv.Value.LastPlayed ?? "" } };
                    SafeFile.WriteAllText(Paths.Playtime, Json.Serialize(o));
                }
                catch (Exception ex) { Log.Error("Stats.Update", ex); }
            }
        }
    }

    public sealed class SessionRecord
    {
        public DateTime Start;
        public string Id;
        public double Seconds;
    }

    /// <summary>sessions.csv: one line per play session ("start;id;seconds"), appended only.</summary>
    public static class SessionLog
    {
        public static bool ReadOnly;

        public static void Add(string id, DateTime? start, double seconds)
        {
            if (ReadOnly || seconds < 1 || start == null) return;
            try { File.AppendAllText(Paths.Sessions, string.Format(Fmt.Inv, "{0};{1};{2}", Fmt.Iso(start.Value), id, (int)seconds) + Environment.NewLine, new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("SessionLog.Add", ex); }
        }

        /// <summary>Adds the sessions of another sessions.csv that aren't in ours yet. Returns how many were added.</summary>
        public static int MergeFrom(string file)
        {
            if (ReadOnly || !File.Exists(file)) return 0;
            var mine = File.Exists(Paths.Sessions) ? new HashSet<string>(File.ReadAllLines(Paths.Sessions, Encoding.UTF8).Select(l => l.Trim('\uFEFF', ' '))) : new HashSet<string>();
            var add = File.ReadAllLines(file, Encoding.UTF8).Select(l => l.Trim('\uFEFF', ' ')).Where(l => l.Split(';').Length >= 3 && !mine.Contains(l)).ToList();
            if (add.Count > 0) File.AppendAllLines(Paths.Sessions, add, new UTF8Encoding(false));
            return add.Count;
        }

        public static List<SessionRecord> Read()
        {
            var list = new List<SessionRecord>();
            try
            {
                if (!File.Exists(Paths.Sessions)) return list;
                foreach (var line in File.ReadAllLines(Paths.Sessions, Encoding.UTF8))
                {
                    var p = line.Split(';');
                    if (p.Length < 3) continue;
                    var d = Fmt.ParseIso(p[0].Trim('﻿'));
                    double sec;
                    if (d == null || !double.TryParse(p[2], NumberStyles.Float, Fmt.Inv, out sec)) continue;
                    list.Add(new SessionRecord { Start = d.Value, Id = p[1], Seconds = sec });
                }
            }
            catch (Exception ex) { Log.Error("SessionLog.Read", ex); }
            return list;
        }
    }
}
