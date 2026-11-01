// TS Launcher - game window (languages, cover gallery, replays, saves), launching, play tracking, covers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TSLauncher
{
    public sealed partial class LauncherWindow
    {
        Game current;
        string pickerMode = "langs";
        readonly List<Button> langButtons = new List<Button>();
        int langSel = -1, langCols = 3;

        // ---------------------------------------------------------------- game window
        void FitDialog()
        {
            if (W.ActualWidth > 0) Dialog.Width = Math.Max(460, Math.Min(900, W.ActualWidth - 80));
            if (W.ActualHeight > 0) Dialog.MaxHeight = Math.Max(380, Math.Min(820, W.ActualHeight - 50));
        }

        void SetPickerHeader(Game g)
        {
            var heroPath = SafeMode ? null : Covers.Find(g.Id + "_hero");
            var c = Col(Catalog.ColorHex(g.Id));
            DlgHeader.Background = new LinearGradientBrush(c, Mix(c, Colors.Black, 0.65), 0);
            DlgShade.Visibility = Visibility.Collapsed;
            DlgHeaderGrid.Height = double.NaN;
            if (heroPath != null)
            {
                DlgHeaderGrid.Height = 230;
                DlgShade.Visibility = Visibility.Visible;
                var id = g.Id;
                ImageCache.Load(W.Dispatcher, heroPath, 1100, false, bmp =>
                {
                    if (current != null && current.Id == id) DlgHeader.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                });
            }
        }

        void UpdateFavButton()
        {
            bool fav = current != null && S.Favorites.Contains(current.Id);
            DlgFav.Content = fav ? "★" : "☆";
            DlgFav.Foreground = Br(fav ? "#FFD75E" : "#E8E6F0");
        }

        void UpdatePickerMode()
        {
            var g = current;
            if (g == null) return;
            bool custom = CustomChk.IsChecked ?? false;
            LangHint.Text = g.Langs.Count == 0 ? "No thcrap configuration found for this game. Open thcrap once, pick some patches, then use Refresh in Settings."
                          : custom ? "OPEN THE GAME SETTINGS (custom.exe) IN…" : "PLAY IN…";
            var last = LastLang(g);
            if (last != null)
            {
                DlgPlay.Visibility = Visibility.Visible;
                DlgPlay.Content = (custom ? "⚙   Settings  ·  " : "▶   Play  ·  ") + Langs.Label(last);
            }
            else DlgPlay.Visibility = Visibility.Collapsed;
        }

        void ShowLangMode()
        {
            pickerMode = "langs";
            CoverScroll.Visibility = Visibility.Collapsed;
            LangScroll.Visibility = Visibility.Visible;
            DlgFooter.Visibility = Visibility.Visible;
        }

        Button NewLangTile(LangEntry l, bool isLast)
        {
            var btn = new Button { Style = (Style)W.FindResource("TileBtn"), Tag = l.Code };
            var label = Langs.Label(l.Code);
            var sp = new StackPanel();
            var t = TB(label, 16, "#F0EEF8", FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(t);
            var subText = label != l.Code ? l.Code : "thcrap configuration";
            if (isLast) subText = "★  Last played   ·   " + subText;
            var sub = TB(subText, 12, isLast ? "#E2D6FF" : "#7C7A94"); sub.Margin = Th(0, 3, 0, 0); sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(sub);
            btn.Content = sp;
            if (isLast)
            {
                var gb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                gb.GradientStops.Add(new GradientStop(Mix(accent, Colors.Black, 0.18), 0));
                gb.GradientStops.Add(new GradientStop(Mix(accent, Col("#15152A"), 0.72), 1));
                btn.Background = gb;
                btn.BorderBrush = Br(accentHex);
            }
            btn.ToolTip = label != l.Code ? label + " (" + l.Code + ")" : l.Code;
            var code = l.Code;
            btn.Click += (s, e) => Safe("play", () => LaunchLang(code));
            btn.GotKeyboardFocus += (s, e) => langSel = langButtons.IndexOf(btn);
            return btn;
        }

        void ShowPicker(Game g)
        {
            current = g;
            FollowGameTheme(g);
            FitDialog();
            ShowLangMode();
            SetPickerHeader(g);
            DlgTitle.Text = g.Title;
            var st = Stats.Get(g.Id);
            if (st == null || !st.Played) DlgStats.Text = "Never played";
            else
            {
                var parts = new List<string> { Fmt.Duration(st.Seconds) + " played", "launched " + Fmt.Plural(st.Launches, "time", "times") };
                var ago = Fmt.Ago(st.LastPlayedDate); if (ago != null) parts.Add("last played " + ago);
                DlgStats.Text = string.Join("   ·   ", parts);
            }
            DlgSub.Text = "TOUHOU " + g.NumText + "   ·   " + g.Source;
            DlgIcon.Visibility = Visibility.Collapsed;
            if (!SafeMode) ImageCache.LoadIcon(W.Dispatcher, g.Exe, bmp => { if (current == g) { DlgIcon.Source = bmp; DlgIcon.Visibility = Visibility.Visible; } });

            CustomChk.IsChecked = false;
            CustomChk.Visibility = g.HasCustom ? Visibility.Visible : Visibility.Collapsed;
            VanillaBtn.Visibility = g.ExeExists ? Visibility.Visible : Visibility.Collapsed;
            CoverBtn.Visibility = g.InCatalog && !SafeMode ? Visibility.Visible : Visibility.Collapsed;
            UpdateFavButton();

            LangPanel.Children.Clear();
            langButtons.Clear(); langSel = -1;
            var last = LastLang(g);
            var ordered = g.Langs.Where(l => l.Code == last).Concat(g.Langs.Where(l => l.Code != last)).ToList();
            LangPanel.Columns = Math.Max(1, Math.Min(3, ordered.Count));
            langCols = LangPanel.Columns;
            foreach (var l in ordered)
            {
                var b = NewLangTile(l, l.Code == last);
                LangPanel.Children.Add(b);
                langButtons.Add(b);
            }
            UpdatePickerMode();
            LangScroll.ScrollToTop();
            Overlay.Visibility = Visibility.Visible;
            langSel = 0;
            W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SetLangSel(0, true)));
            Sounds.Play("select");
        }

        void HidePicker(bool silent = false)
        {
            if (!silent && Overlay.Visibility == Visibility.Visible) Sounds.Play("back");
            Overlay.Visibility = Visibility.Collapsed;
        }

        void SetLangSel(int i, bool quiet = false)
        {
            if (langButtons.Count == 0) return;
            i = Math.Max(0, Math.Min(langButtons.Count - 1, i));
            if (!quiet && i != langSel) Sounds.Play("move");
            langSel = i;
            langButtons[i].Focus();
            langButtons[i].BringIntoView();
        }

        // ---------------------------------------------------------------- launching
        void LaunchLang(string code)
        {
            var g = current;
            var l = g.Lang(code);
            if (l == null) return;
            bool custom = CustomChk.IsChecked ?? false;
            var since = DateTime.Now;
            Process proc;
            try
            {
                var lnk = custom ? l.CustomShortcut : l.Shortcut;
                if (lnk != null && File.Exists(lnk)) proc = Launcher.Start(lnk, null, null);
                else if (l.Config != null && Lib.Thcrap != null)
                    proc = Launcher.Start(Lib.Thcrap.Loader, string.Format("\"{0}\" {1}", l.Config, custom ? g.Id + "_custom" : g.Id), Lib.Thcrap.Root);
                else throw new InvalidOperationException("No shortcut or thcrap configuration found for '" + code + "'.");
            }
            catch (Exception ex) { ShowError("Couldn't launch the game:\n" + ex.Message); return; }
            if (custom) { HidePicker(); return; }
            S.LastLang[g.Id] = code; S.Save();
            CompleteLaunch(g, proc, since);
        }

        void LaunchOriginal()
        {
            var g = current;
            bool custom = CustomChk.IsChecked ?? false;
            var exe = custom && !string.IsNullOrEmpty(g.CustomExe) ? g.CustomExe : g.Exe;
            var since = DateTime.Now;
            Process proc;
            try { proc = Launcher.Start(exe, null, Path.GetDirectoryName(exe)); }
            catch (Exception ex) { ShowError("Couldn't launch the game:\n" + ex.Message); return; }
            if (custom) HidePicker(); else CompleteLaunch(g, proc, since);
        }

        void CompleteLaunch(Game g, Process proc, DateTime since)
        {
            if (!SafeMode) Task.Run(() => Saves.Backup(g));
            Stats.Update(g.Id, 0, 1);
            Track.Begin(g, proc, since);
            UpdateNowPlaying();
            trackTimer.Start();
            UpdateCard(g);
            if (S.SortMode == "recent") ApplySort();
            HidePicker(true);
            Sounds.Play("launch");
            if (S.CloseAfterLaunch) EnterTray();
        }

        // ---------------------------------------------------------------- play tracking
        void TrackTick()
        {
            var r = Track.Tick();
            foreach (var s in r.Detected) UpdateCard(s.Game);
            foreach (var s in r.Ended)
            {
                double sec = s.Elapsed.TotalSeconds;
                Log.Track(string.Format(Fmt.Inv, "{0}: session ended, {1:N0} s counted", s.Game.Id, sec));
                Stats.Update(s.Game.Id, sec, 0);
                SessionLog.Add(s.Game.Id, s.Start, sec);
                var game = s.Game;
                if (!SafeMode) Task.Run(() => Saves.Backup(game));
                UpdateCard(s.Game);
                if (S.SortMode == "recent" || S.SortMode == "most") ApplySort();
            }
            foreach (var s in r.Lost)
                StatusText.Text = "Couldn't detect Touhou " + s.Game.NumText + " running, so its play time wasn't counted (see tracking.log).";
            UpdateNowPlaying();
            if (Track.Sessions.Count == 0)
            {
                trackTimer.Stop();
                if (exitWhenIdle) QuitApp();
            }
        }

        void UpdateNowPlaying()
        {
            NowPlayingPanel.Children.Clear();
            var active = Track.Sessions.ToList();
            NowPlaying.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var tip = new List<string>();
            foreach (var s in active)
            {
                var g = s.Game;
                var row = new DockPanel { Margin = Th(0, 3, 0, 3) };
                var img = new Image { Height = 54, MaxWidth = 54, Margin = Th(0, 0, 14, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var cover = SafeMode ? null : Covers.Find(g.Id);
                if (cover != null) ImageCache.Load(W.Dispatcher, cover, 440, false, bmp => img.Source = bmp);
                else ImageCache.LoadIcon(W.Dispatcher, g.Exe, bmp => img.Source = bmp);
                DockPanel.SetDock(img, Dock.Left);
                row.Children.Add(img);

                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(right, Dock.Right);
                var st = Stats.Get(g.Id);
                double total = st == null ? 0 : st.Seconds;
                if (s.Detected)
                {
                    var el = s.Elapsed;
                    var clock = TB(Fmt.Clock(el), 26, "#5BE08A", FontWeights.SemiBold); clock.HorizontalAlignment = HorizontalAlignment.Right;
                    right.Children.Add(clock);
                    var tt = TB("Total " + Fmt.Duration(total + el.TotalSeconds), 11, "#9FD8B4"); tt.HorizontalAlignment = HorizontalAlignment.Right;
                    right.Children.Add(tt);
                    tip.Add("TH" + g.NumText + "  " + Fmt.Clock(el));
                }
                else { right.Children.Add(TB("Starting…", 15, "#9FD8B4")); tip.Add("TH" + g.NumText + "  starting…"); }
                row.Children.Add(right);

                var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                mid.Children.Add(TB("●  NOW PLAYING", 11, "#5BE08A", FontWeights.SemiBold));
                var t = TB("Touhou " + g.NumText + "  ·  " + g.Title, 16, "#F0EEF8", FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis;
                mid.Children.Add(t);
                row.Children.Add(mid);
                NowPlayingPanel.Children.Add(row);
            }
            if (tray != null)
            {
                var txt = tip.Count > 0 ? AppInfo.Name + " - " + string.Join(" / ", tip) : AppInfo.Name;
                tray.Text = txt.Length > 63 ? txt.Substring(0, 63) : txt;
            }
        }

        // ---------------------------------------------------------------- cover downloads (background)
        sealed class CoverJob { public string Id, Title; public long? SgdbId; public bool NeedGrid, NeedHero; }
        sealed class CoverRun
        {
            public volatile string Status = "Downloading covers…";
            public volatile bool Done; public volatile string Error;
            public int Got;
            public readonly Dictionary<string, long> Ids = new Dictionary<string, long>();
            public List<CoverJob> Jobs;
        }
        CoverRun coverRun;

        bool AskApiKey()
        {
            if (MessageBox.Show(W, "Covers come from SteamGridDB, which needs a free API key.\n\n1. Log in on steamgriddb.com with your Steam account\n2. Go to Preferences > API and copy your key\n\nClick OK to open that page, then paste the key in the next window.",
                AppInfo.Name + " - covers", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return false;
            try { Process.Start("https://www.steamgriddb.com/profile/preferences/api"); } catch { }
            var key = (Ask("Paste your SteamGridDB API key:", AppInfo.Name, S.SgdbKey) ?? "").Trim();
            return key.Length > 0 && TrySetKey(key);
        }

        bool TrySetKey(string key)
        {
            W.Cursor = Cursors.Wait;
            try { Sgdb.TestKey(key); }
            catch (SgdbUnauthorizedException) { ShowError("SteamGridDB rejected that API key. Check that you copied it fully."); return false; }
            catch (Exception ex) { ShowError("Couldn't reach SteamGridDB:\n" + ex.Message); return false; }
            finally { W.Cursor = null; }
            S.SgdbKey = key; S.CoverMisses.Clear(); S.Save();
            return true;
        }

        bool AskAuthor()
        {
            var txt = Ask("Covers by this SteamGridDB artist are used first (others are used when they have none).\n\nPaste their profile link (steamgriddb.com/profile/...) or Steam ID.\nLeave empty to just use the top-rated covers.",
                "Preferred cover artist", S.SgdbAuthor);
            if (txt == null) return false;
            txt = txt.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(txt, @"\d{17}");
            if (txt.Length > 0 && !m.Success) { ShowError("That doesn't contain a Steam ID (a 17-digit number)."); return false; }
            S.SgdbAuthor = txt.Length > 0 ? m.Value : ""; S.Save();
            return true;
        }

        void StartCoverDownload(bool all, bool auto)
        {
            if (SafeMode || coverRun != null) return;
            if (string.IsNullOrEmpty(S.SgdbKey)) { if (auto || !AskApiKey()) return; }
            var jobs = new List<CoverJob>();
            foreach (var g in AllCardGames)
            {
                if (!g.InCatalog || (auto && S.CoverMisses.ContainsKey(g.Id) && S.CoverMisses[g.Id])) continue;
                bool needGrid = all || Covers.Find(g.Id) == null, needHero = all || Covers.Find(g.Id + "_hero") == null;
                long sid;
                if (needGrid || needHero) jobs.Add(new CoverJob { Id = g.Id, Title = g.Title, SgdbId = S.SgdbIds.TryGetValue(g.Id, out sid) ? sid : (long?)null, NeedGrid = needGrid, NeedHero = needHero });
            }
            if (jobs.Count == 0) { if (!auto) StatusText.Text = "All covers are already downloaded."; return; }
            if (all) S.CoverChoice.Clear();
            var run = new CoverRun { Jobs = jobs };
            coverRun = run;
            string key = S.SgdbKey, author = S.SgdbAuthor;
            Task.Run(() =>
            {
                int i = 0;
                foreach (var j in jobs)
                {
                    run.Status = string.Format("Downloading covers {0}/{1}  ·  {2}", ++i, jobs.Count, j.Title);
                    try
                    {
                        var gid = j.SgdbId ?? Sgdb.Resolve(j.Title, key);
                        if (gid == null) continue;
                        lock (run.Ids) run.Ids[j.Id] = gid.Value;
                        if (j.NeedGrid) { var gr = Sgdb.Grids(gid.Value, key, author); if (gr.Count > 0) { Sgdb.Save(gr[0], j.Id); Interlocked.Increment(ref run.Got); } }
                        if (j.NeedHero) { var he = Sgdb.Heroes(gid.Value, key, author); if (he.Count > 0) Sgdb.Save(he[0], j.Id + "_hero"); }
                    }
                    catch (SgdbUnauthorizedException) { run.Error = "SteamGridDB rejected the API key."; break; }
                    catch (Exception ex) { Log.App("Cover download failed for " + j.Id + ": " + ex.Message); }
                }
                run.Done = true;
            });
            coverTimer.Start();
        }

        void CoverTick()
        {
            var run = coverRun;
            if (run == null) { coverTimer.Stop(); return; }
            StatusText.Text = run.Status;
            if (!run.Done) return;
            coverTimer.Stop();
            coverRun = null;
            lock (run.Ids) foreach (var kv in run.Ids) S.SgdbIds[kv.Key] = kv.Value;
            foreach (var j in run.Jobs) S.CoverMisses[j.Id] = Covers.Find(j.Id) == null || Covers.Find(j.Id + "_hero") == null;
            S.Save();
            Reload(() =>
            {
                if (run.Error != null) ShowError(run.Error + "\nChange the key in Settings > Covers.");
                else if (run.Got > 0) StatusText.Text += "  ·  " + run.Got + " new covers";
            });
        }

        // ---------------------------------------------------------------- cover gallery
        List<SgdbImage> galleryGrids = new List<SgdbImage>(), galleryHeroes = new List<SgdbImage>();

        void ShowCoverGallery()
        {
            var g = current;
            if (g == null) return;
            if (string.IsNullOrEmpty(S.SgdbKey) && !AskApiKey()) return;
            string key = S.SgdbKey, author = S.SgdbAuthor;
            long known;
            long? sid = S.SgdbIds.TryGetValue(g.Id, out known) ? known : (long?)null;
            W.Cursor = Cursors.Wait;
            CoverPanel.Children.Clear();
            CoverPanel.Children.Add(TB("Loading the covers from SteamGridDB…", 13, "#8E8CA6"));
            pickerMode = "covers";
            LangScroll.Visibility = Visibility.Collapsed; DlgFooter.Visibility = Visibility.Collapsed; CoverScroll.Visibility = Visibility.Visible;
            Sounds.Play("select");
            Task.Run(() =>
            {
                var gid = sid ?? Sgdb.Resolve(g.Title, key);
                if (gid == null) throw new InvalidOperationException("Couldn't find this game on SteamGridDB.");
                return Tuple.Create(gid.Value, Sgdb.Grids(gid.Value, key, author), Sgdb.Heroes(gid.Value, key, author));
            }).ContinueWith(t =>
            {
                W.Cursor = null;
                if (pickerMode != "covers" || current != g) return;
                if (t.IsFaulted)
                {
                    var ex = t.Exception.GetBaseException();
                    ShowLangMode();
                    ShowError(ex is SgdbUnauthorizedException ? "SteamGridDB rejected the API key. Change it in Settings > Covers." : "Couldn't load the covers:\n" + ex.Message);
                    return;
                }
                S.SgdbIds[g.Id] = t.Result.Item1; S.Save();
                galleryGrids = t.Result.Item2; galleryHeroes = t.Result.Item3;
                BuildGallery();
                CoverScroll.ScrollToTop();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        FrameworkElement GalleryTile(SgdbImage item, string kind, double w, double h)
        {
            string chosen;
            bool inUse = S.CoverChoice.TryGetValue(current.Id + "|" + kind, out chosen) && chosen == item.Id;
            bool mine = !string.IsNullOrEmpty(S.SgdbAuthor) && item.AuthorSteam == S.SgdbAuthor;
            var outer = new StackPanel { Width = w, Margin = Th(0, 0, 10, 14) };
            var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(2), BorderThickness = Th(2, 2, 2, 2), Background = Br("#222440"), Cursor = Cursors.Hand, ClipToBounds = true };
            b.Uid = inUse ? accentLightHex : "#22FFFFFF";
            b.BorderBrush = Br(b.Uid);
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri(string.IsNullOrEmpty(item.Thumb) ? item.Url : item.Thumb); bi.DecodePixelWidth = (int)(w * 1.5); bi.EndInit();
                b.Child = new Image { Source = bi, Stretch = Stretch.UniformToFill };
            }
            catch { }
            b.MouseEnter += (s, e) => b.BorderBrush = Br(accentHex);
            b.MouseLeave += (s, e) => b.BorderBrush = Br(b.Uid);
            b.MouseLeftButtonUp += (s, e) => Safe("cover", () => ApplyGalleryChoice(item, kind));
            outer.Children.Add(b);
            var who = string.IsNullOrEmpty(item.AuthorName) ? "unknown artist" : item.AuthorName;
            var l = TB(inUse ? "✓ In use   ·   " + who : mine ? "★ " + who : who, 11, inUse || mine ? accentLightHex : "#8E8CA6");
            l.Margin = Th(2, 5, 0, 0); l.TextTrimming = TextTrimming.CharacterEllipsis;
            outer.Children.Add(l);
            return outer;
        }

        DockPanel PanelTopBar(params Button[] right)
        {
            var top = new DockPanel { Margin = Th(0, 0, 10, 12) };
            var back = FlatBtn("←   Back to languages", () => { ShowLangMode(); Sounds.Play("back"); W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SetLangSel(langSel, true))); });
            back.Margin = Th(0, 0, 0, 0);
            DockPanel.SetDock(back, Dock.Left);
            top.Children.Add(back);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in right) acts.Children.Add(b);
            top.Children.Add(acts);
            return top;
        }

        void BuildGallery()
        {
            CoverPanel.Children.Clear();
            CoverPanel.Children.Add(PanelTopBar(FlatBtn("My own cover…", () => UseOwnImage("grid")), FlatBtn("My own banner…", () => UseOwnImage("hero"))));
            foreach (var sec in new[] { Tuple.Create("COVERS", "grid", galleryGrids, 132.0, 198.0), Tuple.Create("BANNERS", "hero", galleryHeroes, 262.0, 85.0) })
            {
                var h = TB(sec.Item1 + "   ·   " + sec.Item3.Count, 12, "#8E8CA6", FontWeights.SemiBold); h.Margin = Th(0, 8, 0, 8);
                CoverPanel.Children.Add(h);
                if (sec.Item3.Count == 0)
                {
                    var n = TB("Nothing on SteamGridDB yet. You can still use your own picture.", 12, "#6E6C86"); n.Margin = Th(0, 0, 0, 10);
                    CoverPanel.Children.Add(n); continue;
                }
                var wrap = new WrapPanel();
                foreach (var item in sec.Item3.Take(80)) wrap.Children.Add(GalleryTile(item, sec.Item2, sec.Item4, sec.Item5));
                CoverPanel.Children.Add(wrap);
            }
            var who = S.SgdbAuthor == AppSettings.TelanusId ? "telanus" : !string.IsNullOrEmpty(S.SgdbAuthor) ? "your preferred artist" : null;
            if (who != null)
            {
                var f = TB("★ = by " + who + " (shown first). Click any picture to use it.", 11, "#6E6C86"); f.Margin = Th(0, 4, 0, 6);
                CoverPanel.Children.Add(f);
            }
        }

        void ApplyGalleryChoice(SgdbImage item, string kind)
        {
            var g = current;
            var name = kind == "grid" ? g.Id : g.Id + "_hero";
            W.Cursor = Cursors.Wait;
            Task.Run(() => Sgdb.Save(item, name)).ContinueWith(t =>
            {
                W.Cursor = null;
                if (t.IsFaulted) { ShowError("Couldn't download that picture:\n" + t.Exception.GetBaseException().Message); return; }
                ImageCache.Forget(name);
                S.CoverChoice[g.Id + "|" + kind] = item.Id; S.Save();
                UpdateCard(g); SetPickerHeader(g);
                if (pickerMode == "covers") BuildGallery();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void UseOwnImage(string kind)
        {
            var g = current;
            string file;
            // WinForms dialog on purpose: the WPF one has a different base class in newer .NET versions.
            using (var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = kind == "grid" ? "Choose a cover (portrait, 2:3 looks best)" : "Choose a banner (wide, about 3:1)",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
            })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                file = dlg.FileName;
            }
            var name = kind == "grid" ? g.Id : g.Id + "_hero";
            try { Covers.UseOwn(file, name); }
            catch (Exception ex) { ShowError("Couldn't copy that picture:\n" + ex.Message); return; }
            ImageCache.Forget(name);
            S.CoverChoice.Remove(g.Id + "|" + kind); S.Save();
            UpdateCard(g); SetPickerHeader(g); BuildGallery();
        }

        // ---------------------------------------------------------------- replays
        void ShowReplays()
        {
            pickerMode = "replays";
            LangScroll.Visibility = Visibility.Collapsed; DlgFooter.Visibility = Visibility.Collapsed; CoverScroll.Visibility = Visibility.Visible;
            BuildReplays();
            CoverScroll.ScrollToTop();
            Sounds.Play("select");
        }

        Button SmallBtn(string text, Action a)
        {
            var b = FlatBtn(text, a);
            b.Padding = Th(10, 5, 10, 5); b.FontSize = 12; b.Margin = Th(6, 0, 0, 0);
            return b;
        }

        void BuildReplays()
        {
            var g = current;
            var dir = Saves.SubFolder(g, "replay");
            CoverPanel.Children.Clear();
            var buttons = new List<Button> { FlatBtn("Add replay…", AddReplay) };
            if (dir != null) { buttons.Add(FlatBtn("Back up all", () => BackupReplays(null))); buttons.Add(FlatBtn("Open folder", () => OpenFolder(dir))); }
            CoverPanel.Children.Add(PanelTopBar(buttons.ToArray()));

            var files = dir == null ? new List<FileInfo>() : new DirectoryInfo(dir).GetFiles("*.rpy").OrderByDescending(f => f.LastWriteTime).ToList();
            var h = TB("REPLAYS   ·   " + files.Count, 12, "#8E8CA6", FontWeights.SemiBold); h.Margin = Th(0, 4, 0, 8);
            CoverPanel.Children.Add(h);
            if (files.Count == 0)
            {
                var n = TB("No replays yet. Save one at the end of a run in the game, or add a replay file someone shared with you.", 13, "#8E8CA6");
                n.TextWrapping = TextWrapping.Wrap;
                CoverPanel.Children.Add(n);
                return;
            }
            foreach (var f in files)
            {
                var file = f;
                var info = Replays.Read(f.FullName);
                var row = new Border { Background = Br("#1F2036"), BorderBrush = Br("#2C2E4C"), BorderThickness = Th(1, 1, 1, 1), CornerRadius = new CornerRadius(2), Padding = Th(14, 9, 10, 9), Margin = Th(0, 0, 10, 6) };
                var dp = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                btns.Children.Add(SmallBtn("Rename", () => RenameReplay(file.FullName)));
                btns.Children.Add(SmallBtn("Back up", () => BackupReplays(file.FullName)));
                btns.Children.Add(SmallBtn("Delete", () => DeleteReplay(file.FullName)));
                DockPanel.SetDock(btns, Dock.Right);
                dp.Children.Add(btns);
                var left = new StackPanel();
                var title = info == null ? "" : string.Join("   ·   ", new[] { info.Rank, info.Chara, info.Stage }.Where(x => !string.IsNullOrEmpty(x)));
                if (title.Length == 0) title = Path.GetFileNameWithoutExtension(f.Name);
                var t = TB(title, 14, "#F0EEF8", FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis;
                left.Children.Add(t);
                var sub = new List<string>();
                if (info != null && !string.IsNullOrEmpty(info.Name)) sub.Add(info.Name);
                sub.Add(f.LastWriteTime.ToString("d MMM yyyy, HH:mm", Fmt.Inv));
                long score;
                if (info != null && long.TryParse(info.Score, out score)) sub.Add("Score " + score.ToString("N0", Fmt.Inv));
                sub.Add(f.Name);
                var sl = TB(string.Join("   ·   ", sub), 12, "#8E8CA6"); sl.Margin = Th(0, 2, 0, 0); sl.TextTrimming = TextTrimming.CharacterEllipsis;
                left.Children.Add(sl);
                dp.Children.Add(left);
                row.Child = dp;
                CoverPanel.Children.Add(row);
            }
            var foot = TB("Games only list replays named like " + g.Id + "_01.rpy … " + g.Id + "_25.rpy in their own menu; other names stay safe here but are hidden in-game.", 11, "#6E6C86");
            foot.Margin = Th(0, 6, 0, 6); foot.TextWrapping = TextWrapping.Wrap;
            CoverPanel.Children.Add(foot);
        }

        void RenameReplay(string path)
        {
            var old = Path.GetFileNameWithoutExtension(path);
            var v = (Ask("New name for this replay (without .rpy).\n\nTip: the game only shows names like " + current.Id + "_01 … " + current.Id + "_25.", "Rename replay", old) ?? "").Trim();
            if (v.Length == 0 || v == old) return;
            if (v.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { ShowError("That name contains characters Windows does not allow."); return; }
            var dest = Path.Combine(Path.GetDirectoryName(path), v + ".rpy");
            if (File.Exists(dest)) { ShowError("A replay with that name already exists."); return; }
            File.Move(path, dest);
            BuildReplays();
        }

        void BackupReplays(string only)
        {
            var g = current;
            var dir = Saves.SubFolder(g, "replay");
            if (dir == null) return;
            var dest = Path.Combine(Paths.Backups, g.Id, "replays", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", Fmt.Inv));
            Directory.CreateDirectory(dest);
            var list = only != null ? new[] { only } : Directory.GetFiles(dir, "*.rpy");
            foreach (var f in list) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            StatusText.Text = Fmt.Plural(list.Length, "replay", "replays") + " backed up.";
            Sounds.Play("toggle");
        }

        void DeleteReplay(string path)
        {
            if (!Confirm("Move " + Path.GetFileName(path) + " to the Recycle Bin?", "Delete replay")) return;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            BuildReplays();
        }

        void AddReplay()
        {
            var g = current;
            string[] picked;
            using (var dlg = new System.Windows.Forms.OpenFileDialog { Title = "Add a replay file", Filter = "Touhou replays|*.rpy", Multiselect = true })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                picked = dlg.FileNames;
            }
            var dir = Saves.SubFolder(g, "replay");
            if (dir == null)
            {
                var baseDir = Saves.Dirs(g).FirstOrDefault();
                if (baseDir == null) { ShowError("Couldn't find this game's save folder. Start the game once first."); return; }
                dir = Path.Combine(baseDir, "replay");
                Directory.CreateDirectory(dir);
            }
            foreach (var src in picked)
            {
                var dest = Path.Combine(dir, Path.GetFileName(src));
                if (File.Exists(dest) && !Confirm(Path.GetFileName(src) + " already exists. Replace it?", "Add replay")) continue;
                File.Copy(src, dest, true);
            }
            BuildReplays();
        }

        // ---------------------------------------------------------------- saves menu
        void ShowSavesMenu()
        {
            var g = current;
            if (g == null) return;
            var m = new ContextMenu();
            Func<ItemsControl, string, Action, bool, MenuItem> add = (parent, header, action, enabled) =>
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                if (action != null) mi.Click += (s, e) => Safe("saves", action);
                parent.Items.Add(mi);
                return mi;
            };
            add(m, "Back up now", () =>
            {
                var r = Saves.Backup(g);
                StatusText.Text = r == Saves.Result.Unchanged ? "No changes since the last backup."
                                : r == Saves.Result.Saved ? g.Title + ": save backed up."
                                : r == Saves.Result.NoSave ? "No save file found for this game yet (play it once first)." : "The backup failed (see tracking.log).";
            }, true);
            var backups = Saves.List(g);
            var sub = add(m, backups.Count > 0 ? "Restore a backup" : "Restore a backup (none yet)", null, backups.Count > 0);
            foreach (var b in backups)
            {
                var folder = b.FullName; var when = Saves.Pretty(b.Name);
                add(sub, when, () =>
                {
                    if (Track.Sessions.Any(x => x.Game.Id == g.Id)) { ShowError("Close the game before restoring a save."); return; }
                    if (!Confirm("Restore the " + g.Title + " save from " + when + "?\n\nYour current save is backed up first, so you can undo this.", "Restore save")) return;
                    Saves.Backup(g);
                    Saves.Restore(folder);
                    Log.Track(g.Id + ": save restored from " + when);
                    StatusText.Text = g.Title + ": save restored from " + when + ".";
                }, true);
            }
            m.Items.Add(new Separator());
            var saveDir = Saves.Files(g).Select(f => f.DirectoryName).FirstOrDefault();
            add(m, "Open save folder", () => OpenFolder(saveDir), saveDir != null);
            var rep = Saves.SubFolder(g, "replay");
            add(m, "Open replays folder", () => OpenFolder(rep), rep != null);
            var snap = Saves.SubFolder(g, "snapshot");
            add(m, "Open screenshots folder", () => OpenFolder(snap), snap != null);
            var bdir = Path.Combine(Paths.Backups, g.Id);
            add(m, "Open backups folder", () => OpenFolder(bdir), backups.Count > 0);
            m.PlacementTarget = SavesBtn; m.Placement = PlacementMode.Top; m.IsOpen = true;
        }
    }
}
