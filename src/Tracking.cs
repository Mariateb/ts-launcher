// TS Launcher - starting games and timing them. Follows the launch chain (thcrap loader, Steam
// relaunching the game, a game restarting itself) and allows a short grace period between processes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TSLauncher
{
    public sealed class Session
    {
        public Game Game;
        public DateTime Since;              // when we started looking (launch time, or when the last process exited)
        public List<int> Roots = new List<int>();
        public int Pid;                     // 0 while not found yet / between processes
        public DateTime? Start;             // when the game itself started (null until detected)
        public DateTime? ExitedAt;          // set while waiting for a replacement process
        public object Thumb;                // cached thumbnail for the "Now playing" bar

        public bool Detected { get { return Start != null; } }
        public TimeSpan Elapsed
        {
            get
            {
                if (Start == null) return TimeSpan.Zero;
                var end = ExitedAt != null && Pid == 0 ? ExitedAt.Value : DateTime.Now;
                var t = end - Start.Value;
                return t < TimeSpan.Zero ? TimeSpan.Zero : t;
            }
        }
    }

    public static class Launcher
    {
        /// <summary>Starts a file and returns the process (a .lnk is resolved first so we know what really runs).</summary>
        public static Process Start(string file, string args, string workDir)
        {
            if (string.Equals(Path.GetExtension(file), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var li = ShellLinks.Resolve(file);
                if (li != null && !string.IsNullOrEmpty(li.Target) && File.Exists(li.Target))
                {
                    file = li.Target; args = li.Arguments;
                    workDir = string.IsNullOrEmpty(li.WorkingDirectory) ? Path.GetDirectoryName(li.Target) : li.WorkingDirectory;
                }
                else
                {
                    Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                    return null;
                }
            }
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
            if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir)) psi.WorkingDirectory = workDir;
            return Process.Start(psi);
        }
    }

    public sealed class Tracker
    {
        public readonly List<Session> Sessions = new List<Session>();

        static readonly string[] Ignored = { "thcrap", "conhost", "steam", "gameoverlay", "werfault", "explorer", "cmd", "powershell", "tslauncher" };

        public Session Begin(Game g, Process proc, DateTime since)
        {
            var s = new Session { Game = g, Since = since };
            if (proc != null) { try { s.Roots.Add(proc.Id); } catch { } }
            Log.Track(string.Format("{0}: launched {1} (pid {2})", g.Id, proc != null ? SafeName(proc) : "shortcut", proc != null ? SafePid(proc) : "?"));
            Sessions.Add(s);
            return s;
        }

        static string SafeName(Process p) { try { return p.ProcessName; } catch { return "?"; } }
        static string SafePid(Process p) { try { return p.Id.ToString(); } catch { return "?"; } }

        ProcInfo Find(Session s)
        {
            var g = s.Game;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { g.Id };
            if (!string.IsNullOrEmpty(g.Exe)) names.Add(Path.GetFileNameWithoutExtension(g.Exe));
            string dir = null;
            if (!string.IsNullOrEmpty(g.Exe)) { try { dir = Path.GetDirectoryName(g.Exe).TrimEnd('\\') + "\\"; } catch { } }
            var taken = new HashSet<int>(Sessions.Where(x => x != s && x.Pid != 0).Select(x => x.Pid));
            var procs = Procs.CreatedSince(s.Since.AddSeconds(-5));

            // everything started (directly or not) by the process we launched / the previous game process
            var chain = new HashSet<int>(s.Roots);
            var queue = new Queue<int>(s.Roots);
            while (queue.Count > 0)
            {
                int x = queue.Dequeue();
                foreach (var p in procs) if (p.ParentPid == x && chain.Add(p.Pid)) queue.Enqueue(p.Pid);
            }

            ProcInfo best = null; int bestScore = 0; string why = "";
            foreach (var p in procs)
            {
                if (taken.Contains(p.Pid)) continue;
                var n = Path.GetFileNameWithoutExtension(p.Name ?? "").ToLowerInvariant();
                if (Ignored.Any(i => n.StartsWith(i)) || n == "custom" || n == "vpatch") continue;
                int score = 0; var r = new List<string>();
                if (!string.IsNullOrEmpty(g.Exe) && p.Path != null && string.Equals(p.Path, g.Exe, StringComparison.OrdinalIgnoreCase)) { score += 8; r.Add("exact exe"); }
                if (names.Contains(n)) { score += 4; r.Add("name"); }
                if (dir != null && p.Path != null && p.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) { score += 2; r.Add("game folder"); }
                if (chain.Contains(p.Pid)) { score += 3; r.Add("started by launch"); }
                if (score > bestScore) { best = p; bestScore = score; why = string.Join(", ", r); }
            }
            if (best != null) Log.Track(string.Format("{0}: tracking {1} (pid {2}, {3}) {4}", g.Id, best.Name, best.Pid, why, best.Path));
            return best;
        }

        public sealed class TickResult
        {
            public List<Session> Ended = new List<Session>();
            public List<Session> Detected = new List<Session>();
            public List<Session> Lost = new List<Session>();
        }

        /// <summary>Called about once a second while something is being timed.</summary>
        public TickResult Tick()
        {
            var res = new TickResult();
            foreach (var s in Sessions.ToList())
            {
                if (s.Pid != 0)
                {
                    if (Procs.IsAlive(s.Pid)) continue;
                    Log.Track(string.Format("{0}: pid {1} exited, waiting for a replacement", s.Game.Id, s.Pid));
                    s.ExitedAt = DateTime.Now;
                    s.Roots = new List<int> { s.Pid };
                    s.Since = s.ExitedAt.Value.AddSeconds(-2);
                    s.Pid = 0;
                    continue;
                }
                var p = Find(s);
                if (p != null)
                {
                    if (s.Start == null)
                    {
                        var st = p.Created ?? DateTime.Now;
                        if (st < s.Since.AddSeconds(-15)) st = s.Since;   // was already running: count from our launch
                        s.Start = st;
                        res.Detected.Add(s);
                    }
                    s.Pid = p.Pid; s.ExitedAt = null;
                }
                else if (s.ExitedAt != null)
                {
                    if ((DateTime.Now - s.ExitedAt.Value).TotalSeconds > 20) { Sessions.Remove(s); res.Ended.Add(s); }
                }
                else if ((DateTime.Now - s.Since).TotalSeconds > 90)
                {
                    Log.Track(s.Game.Id + ": game process never found, nothing counted");
                    Sessions.Remove(s); res.Lost.Add(s);
                }
            }
            return res;
        }
    }
}
