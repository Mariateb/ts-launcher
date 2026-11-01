// TS Launcher - SteamGridDB client (covers = 600x900 grids, banners = heroes).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TSLauncher
{
    public sealed class SgdbImage
    {
        public string Id, Url, Thumb, AuthorName, AuthorSteam;
    }

    public sealed class SgdbUnauthorizedException : Exception
    {
        public SgdbUnauthorizedException() : base("SteamGridDB rejected the API key.") { }
    }

    public static class Sgdb
    {
        const string Api = "https://www.steamgriddb.com/api/v2/";

        static Sgdb()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
        }

        static Dictionary<string, object> Call(string path, string key)
        {
            var req = (HttpWebRequest)WebRequest.Create(Api + path);
            req.Headers["Authorization"] = "Bearer " + key;
            req.UserAgent = "TSLauncher/" + AppInfo.Version;
            req.Timeout = 20000; req.ReadWriteTimeout = 20000;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Json.ParseObject(sr.ReadToEnd());
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null && (int)r.StatusCode == 401) throw new SgdbUnauthorizedException();
                if (r != null && (int)r.StatusCode == 404) return new Dictionary<string, object> { { "success", false } };
                throw;
            }
        }

        public static void TestKey(string key) { Call("search/autocomplete/touhou", key); }

        static string Norm(string t) { return Regex.Replace(Regex.Replace(t ?? "", "[^A-Za-z0-9 ]", " "), @"\s+", " ").Trim().ToLowerInvariant(); }

        /// <summary>Finds the SteamGridDB game id, preferring a result whose name contains the English title.</summary>
        public static long? Resolve(string title, string key)
        {
            long? fallback = null;
            foreach (var term in new[] { "Touhou " + title, title })
            {
                var r = Call("search/autocomplete/" + Uri.EscapeDataString(term), key);
                if (!(Json.Bool(r, "success") ?? false)) continue;
                var want = Norm(title);
                foreach (var item in Json.Arr(Json.Get(r, "data")).Select(Json.Obj).Where(o => o != null))
                {
                    var id = Json.Num(item, "id");
                    if (id == null) continue;
                    if (Norm(Json.Str(item, "name")).Contains(want)) return (long)id.Value;
                    if (fallback == null) fallback = (long)id.Value;
                }
            }
            return fallback;
        }

        static List<SgdbImage> Images(string path, string key, string author)
        {
            var r = Call(path, key);
            var list = new List<SgdbImage>();
            foreach (var o in Json.Arr(Json.Get(r, "data")).Select(Json.Obj).Where(o => o != null))
            {
                var a = Json.Obj(Json.Get(o, "author"));
                list.Add(new SgdbImage
                {
                    Id = Json.Str(o, "id"), Url = Json.Str(o, "url"), Thumb = Json.Str(o, "thumb"),
                    AuthorName = Json.Str(a, "name"), AuthorSteam = Json.Str(a, "steam64"),
                });
            }
            // chosen artist first, then SteamGridDB's own order (by score)
            return list.Where(i => !string.IsNullOrEmpty(author) && i.AuthorSteam == author)
                       .Concat(list.Where(i => string.IsNullOrEmpty(author) || i.AuthorSteam != author)).ToList();
        }

        public static List<SgdbImage> Grids(long gameId, string key, string author)
        {
            return Images("grids/game/" + gameId + "?dimensions=600x900&types=static&mimes=image/png,image/jpeg&nsfw=false", key, author);
        }

        public static List<SgdbImage> Heroes(long gameId, string key, string author)
        {
            return Images("heroes/game/" + gameId + "?types=static&mimes=image/png,image/jpeg&nsfw=false", key, author);
        }

        /// <summary>Downloads to covers\name.ext, replacing any other picture with that name.</summary>
        public static void Save(SgdbImage img, string name)
        {
            Directory.CreateDirectory(Paths.Covers);
            var ext = Path.GetExtension(new Uri(img.Url).AbsolutePath).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") ext = ".png";
            var tmp = Path.Combine(Paths.Covers, name + ".download");
            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "TSLauncher/" + AppInfo.Version;
                wc.DownloadFile(img.Url, tmp);
            }
            Covers.RemoveVariants(name);
            File.Move(tmp, Path.Combine(Paths.Covers, name + ext));
        }
    }

    public static class Covers
    {
        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp" };

        public static string Find(string name)
        {
            foreach (var e in Exts) { var f = Path.Combine(Paths.Covers, name + e); if (File.Exists(f)) return f; }
            return null;
        }

        public static void RemoveVariants(string name)
        {
            foreach (var e in Exts) { var f = Path.Combine(Paths.Covers, name + e); if (File.Exists(f)) File.Delete(f); }
        }

        public static void UseOwn(string source, string name)
        {
            Directory.CreateDirectory(Paths.Covers);
            RemoveVariants(name);
            File.Copy(source, Path.Combine(Paths.Covers, name + Path.GetExtension(source).ToLowerInvariant()), true);
        }
    }
}
