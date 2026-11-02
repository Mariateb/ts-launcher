// TS Launcher - save backups, replay files, menu sounds.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Security.Cryptography;
using System.Text;

namespace TSLauncher
{
    /// <summary>
    /// Touhou keeps clears/unlocks in score*.dat: next to the game for older titles, in
    /// %APPDATA%\ShanghaiAlice\thXX for newer ones. Copies go to backups\thXX\yyyy-MM-dd_HH-mm-ss.
    /// </summary>
    public static class Saves
    {
        public const int Keep = 10;

        public static List<string> Dirs(Game g)
        {
            var dirs = new List<string>();
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appdata)) dirs.Add(Path.Combine(appdata, "ShanghaiAlice", g.Id));
            if (!string.IsNullOrEmpty(g.Exe)) { try { dirs.Add(Path.GetDirectoryName(g.Exe)); } catch { } }
            return dirs.Where(Directory.Exists).ToList();
        }

        public static List<FileInfo> Files(Game g)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<FileInfo>();
            foreach (var d in Dirs(g))
            {
                FileInfo[] fs;
                try { fs = new DirectoryInfo(d).GetFiles(); } catch { continue; }
                foreach (var f in fs)
                {
                    bool isSave = f.Name.StartsWith("score", StringComparison.OrdinalIgnoreCase) && f.Extension.Equals(".dat", StringComparison.OrdinalIgnoreCase);
                    if ((isSave || f.Extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase)) && seen.Add(f.Name)) list.Add(f);
                }
            }
            return list;
        }

        public static string SubFolder(Game g, string name)
        {
            foreach (var d in Dirs(g)) { var p = Path.Combine(d, name); if (Directory.Exists(p)) return p; }
            return null;
        }

        public static List<DirectoryInfo> List(Game g)
        {
            var d = new DirectoryInfo(Path.Combine(Paths.Backups, g.Id));
            if (!d.Exists) return new List<DirectoryInfo>();
            return d.GetDirectories().Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Name, @"^\d{4}-\d\d-\d\d_\d\d-\d\d-\d\d$"))
                    .OrderByDescending(x => x.Name, StringComparer.Ordinal).ToList();
        }

        public static string Pretty(string folderName)
        {
            DateTime d;
            return DateTime.TryParseExact(folderName, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) : folderName;
        }

        static string Md5(string path)
        {
            using (var md5 = MD5.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return BitConverter.ToString(md5.ComputeHash(fs));
        }

        public enum Result { NoSave, Unchanged, Saved, Failed }

        public static Result Backup(Game g)
        {
            try
            {
                var files = Files(g);
                if (!files.Any(f => f.Name.StartsWith("score", StringComparison.OrdinalIgnoreCase))) return Result.NoSave;
                var latest = List(g).FirstOrDefault();
                if (latest != null && files.All(f =>
                {
                    var b = Path.Combine(latest.FullName, f.Name);
                    return File.Exists(b) && Md5(b) == Md5(f.FullName);
                })) return Result.Unchanged;
                var dest = Path.Combine(Paths.Backups, g.Id, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(dest);
                var map = new List<string>();
                foreach (var f in files)
                {
                    using (var src = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var dst = File.Create(Path.Combine(dest, f.Name))) src.CopyTo(dst);
                    map.Add(f.Name + "|" + f.FullName);
                }
                File.WriteAllLines(Path.Combine(dest, "origin.txt"), map, Encoding.UTF8);
                foreach (var old in List(g).Skip(Keep)) { try { old.Delete(true); } catch { } }
                Log.Track(g.Id + ": save backed up (" + map.Count + " files)");
                return Result.Saved;
            }
            catch (Exception ex) { Log.Track(g.Id + ": backup failed: " + ex.Message); return Result.Failed; }
        }

        /// <summary>
        /// Puts a backup back. Files go where this PC keeps them now (so a backup made on another PC, or before the
        /// game moved folder, still lands in the right place): an existing save with that name, else AppData for
        /// files that came from AppData, else the game's folder.
        /// </summary>
        public static int Restore(string folder, Game g)
        {
            var origin = Path.Combine(folder, "origin.txt");
            var lines = File.Exists(origin) ? File.ReadAllLines(origin, Encoding.UTF8).ToList()
                      : Directory.GetFiles(folder).Select(Path.GetFileName).Where(f => f != "origin.txt").Select(f => f + "|").ToList();
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var existing = Files(g).ToDictionary(f => f.Name, f => f.FullName, StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var line in lines)
            {
                var p = line.Split(new[] { '|' }, 2);
                var name = p[0];
                var src = Path.Combine(folder, name);
                if (name.Length == 0 || !File.Exists(src)) continue;
                var was = p.Length > 1 ? p[1] : "";
                string target;
                if (!existing.TryGetValue(name, out target))
                {
                    if (was.IndexOf("\\ShanghaiAlice\\", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(appdata))
                        target = Path.Combine(appdata, "ShanghaiAlice", g.Id, name);
                    else if (!string.IsNullOrEmpty(g.Exe)) target = Path.Combine(Path.GetDirectoryName(g.Exe), name);
                    else if (was.Length > 0) target = was;
                    else continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(src, target, true);
                n++;
            }
            return n;
        }
    }

    public sealed class ReplayInfo
    {
        public string Name, Date, Chara, Rank, Stage, Score;
    }

    public static class Replays
    {
        /// <summary>th10+ replays end with a plain-text USER block (Shift-JIS): Name / Date / Chara / Rank / Stage / Score.</summary>
        public static ReplayInfo Read(string path)
        {
            try
            {
                var b = File.ReadAllBytes(path);
                if (b.Length < 32) return null;
                int off = (int)BitConverter.ToUInt32(b, 12);
                bool ok = off > 0 && off < b.Length - 12 && Encoding.ASCII.GetString(b, off, 4) == "USER";
                if (!ok)
                {
                    off = -1;
                    for (int i = b.Length - 16; i >= 16; i--)
                        if (b[i] == 0x55 && b[i + 1] == 0x53 && b[i + 2] == 0x45 && b[i + 3] == 0x52) { off = i; break; }
                    if (off < 0) return null;
                }
                int size = (int)BitConverter.ToUInt32(b, off + 4);
                int len = Math.Min(Math.Max(0, size - 12), b.Length - off - 12);
                if (len <= 0) return null;
                var txt = Encoding.GetEncoding(932).GetString(b, off + 12, len);
                var info = new ReplayInfo(); bool any = false;
                foreach (var raw in txt.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(raw.TrimEnd('\r', '\0'), @"^\s*(Name|Date|Chara|Rank|Stage|Score)\s+(.+?)\s*$");
                    if (!m.Success) continue;
                    var v = m.Groups[2].Value.Trim('\0');
                    switch (m.Groups[1].Value)
                    {
                        case "Name": info.Name = v; break;
                        case "Date": info.Date = v; break;
                        case "Chara": info.Chara = v; break;
                        case "Rank": info.Rank = v; break;
                        case "Stage": info.Stage = v; break;
                        case "Score": info.Score = v; break;
                    }
                    any = true;
                }
                return any ? info : null;
            }
            catch { return null; }
        }
    }

    /// <summary>Tiny synthesized blips, no sound files needed.</summary>
    public static class Sounds
    {
        static readonly Dictionary<string, int[][]> Notes = new Dictionary<string, int[][]>
        {
            { "move",   new[] { new[] { 1320, 26 } } },
            { "select", new[] { new[] { 880, 45 }, new[] { 1320, 75 } } },
            { "back",   new[] { new[] { 1100, 45 }, new[] { 740, 75 } } },
            { "launch", new[] { new[] { 660, 70 }, new[] { 880, 70 }, new[] { 1320, 170 } } },
            { "toggle", new[] { new[] { 990, 40 } } },
        };
        static readonly Dictionary<string, SoundPlayer> Players = new Dictionary<string, SoundPlayer>();
        static DateTime last = DateTime.MinValue;
        public static bool Enabled = true;

        public static byte[] Wav(int[][] notes, double volume)
        {
            const int rate = 22050;
            var samples = new List<short>();
            double amp = 32767 * 0.32 * Math.Max(0, Math.Min(1, volume));
            foreach (var n in notes)
            {
                double f = n[0]; int count = rate * n[1] / 1000, attack = (int)(rate * 0.004);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / rate;
                    double env = i < attack ? (double)i / attack : Math.Exp(-5.0 * (i - attack) / count);
                    samples.Add((short)((Math.Sin(2 * Math.PI * f * t) * 0.8 + Math.Sin(4 * Math.PI * f * t) * 0.2) * env * amp));
                }
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int data = samples.Count * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
                foreach (var s in samples) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static void Init(int volumePercent)
        {
            lock (Players)
            {
                Players.Clear();
                foreach (var kv in Notes)
                {
                    try
                    {
                        var p = new SoundPlayer(new MemoryStream(Wav(kv.Value, volumePercent / 100.0)));
                        p.Load();
                        Players[kv.Key] = p;
                    }
                    catch (Exception ex) { Log.Error("Sounds.Init", ex); }
                }
            }
        }

        public static void Play(string name)
        {
            if (!Enabled) return;
            var now = DateTime.Now;
            if (name == "move" && (now - last).TotalMilliseconds < 40) return;
            last = now;
            SoundPlayer p;
            lock (Players) { if (!Players.TryGetValue(name, out p)) return; }
            try { p.Play(); } catch { }
        }
    }
}
