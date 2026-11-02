// TS Launcher - update notice: checks the latest GitHub release and swaps the exe in place.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TSLauncher
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag, PageUrl, ZipUrl, Notes;
        public string VersionText { get { return Version.ToString(3); } }
    }

    public static class Updater
    {
        public const string Repo = "Mariateb/ts-launcher";
        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases"; } }

        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        static string OldExe { get { return ExePath + ".old"; } }

        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
        }

        /// <summary>Parses "v1.2.3" / "1.2" into a Version with 3 parts.</summary>
        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(tag, @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            Func<int, int> part = i => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
            return new Version(part(1), part(2), part(3));
        }

        public static bool IsNewer(Version candidate, Version current)
        {
            if (candidate == null) return false;
            var cur = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            return candidate > cur;
        }

        /// <summary>Returns the latest release if it is newer than this exe, else null. Never throws.</summary>
        public static UpdateInfo Check()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                req.UserAgent = "TSLauncher/" + AppInfo.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000; req.ReadWriteTimeout = 10000;
                string text;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = sr.ReadToEnd();
                var info = Parse(text);
                Log.App("Update check: latest release " + (info == null ? "none" : info.Tag) + ", running " + AppInfo.Version);
                return info != null && IsNewer(info.Version, Current) && info.ZipUrl != null ? info : null;
            }
            catch (Exception ex) { Log.App("Update check failed: " + ex.Message); return null; }
        }

        /// <summary>Release notes (Markdown) as plain text for a message box: no **, `, links; bullets as •.</summary>
        public static string PlainNotes(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "";
            var lines = new List<string>();
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var l = raw;
                if (l.TrimStart().StartsWith("**Install:**")) break;                      // the install hint is for the web page
                l = Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1");                     // [text](url) -> text
                l = l.Replace("**", "").Replace("__", "").Replace("`", "");
                l = Regex.Replace(l, @"(?<![\w*])\*([^*\n]+)\*(?![\w*])", "$1");          // *italic* -> italic
                var m = Regex.Match(l, @"^(\s*)[-*] (.*)$");
                if (m.Success) l = (m.Groups[1].Value.Length >= 2 ? "      ◦ " : "• ") + m.Groups[2].Value;
                lines.Add(l.TrimEnd());
            }
            return string.Join("\n", lines).Trim();
        }

        /// <summary>Reads GitHub's "latest release" JSON (null for drafts and pre-releases).</summary>
        public static UpdateInfo Parse(string json)
        {
            var j = Json.ParseObject(json);
            if (Json.Bool(j, "draft") == true || Json.Bool(j, "prerelease") == true) return null;
            var info = new UpdateInfo { Tag = Json.Str(j, "tag_name"), PageUrl = Json.Str(j, "html_url"), Notes = Json.Str(j, "body") ?? "" };
            info.Version = ParseTag(info.Tag);
            foreach (var a in Json.Arr(Json.Get(j, "assets")).Select(Json.Obj).Where(a => a != null))
            {
                var name = Json.Str(a, "name") ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { info.ZipUrl = Json.Str(a, "browser_download_url"); break; }
            }
            return info.Version == null ? null : info;
        }

        /// <summary>Removes the previous exe left behind by an update (it may still be closing, so retry a few times).</summary>
        public static void CleanupOld()
        {
            if (!File.Exists(OldExe)) return;
            Task.Run(async () =>
            {
                for (int i = 0; i < 10 && File.Exists(OldExe); i++)
                {
                    try { File.Delete(OldExe); } catch { await Task.Delay(1000); }
                }
            });
        }

        /// <summary>
        /// Downloads the release zip, takes TSLauncher.exe out of it and swaps it with the running exe
        /// (Windows allows renaming a running exe). Rolls back if anything fails. Runs on a worker thread.
        /// </summary>
        public static void Install(UpdateInfo info)
        {
            var dir = Path.Combine(Path.GetTempPath(), "TSLauncher-update");
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "TSLauncher-" + info.VersionText + ".zip");
            var fresh = Path.Combine(dir, "TSLauncher.exe");
            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "TSLauncher/" + AppInfo.Version;
                wc.DownloadFile(info.ZipUrl, zip);
            }
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("TSLauncher.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new InvalidDataException("The download doesn't contain TSLauncher.exe.");
                entry.ExtractToFile(fresh, true);
            }
            var head = new byte[2];
            using (var fs = File.OpenRead(fresh)) fs.Read(head, 0, 2);
            if (head[0] != 'M' || head[1] != 'Z' || new FileInfo(fresh).Length < 50 * 1024) throw new InvalidDataException("The downloaded TSLauncher.exe looks damaged.");

            if (File.Exists(OldExe)) File.Delete(OldExe);
            File.Move(ExePath, OldExe);
            try { File.Copy(fresh, ExePath, false); }
            catch { File.Move(OldExe, ExePath); throw; }
            try { File.Delete(zip); File.Delete(fresh); } catch { }
            Log.App("Updated to " + info.VersionText);
        }
    }
}
