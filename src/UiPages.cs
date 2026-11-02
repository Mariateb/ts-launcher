// TS Launcher - Stats, Settings and Help pages, and the first-run setup.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TSLauncher
{
    public sealed partial class LauncherWindow
    {
        // ---------------------------------------------------------------- page building blocks
        void PageHeader(string title, string sub)
        {
            StatsPanel.Children.Clear();
            StatsPanel.Children.Add(TB(title, 28, "#F0EEF8", FontWeights.SemiBold));
            if (sub != null) StatsPanel.Children.Add(TB(sub, 12, "#6E6C86"));
        }

        void Section(string title)
        {
            var tb = TB(title, 17, "#F0EEF8", FontWeights.SemiBold);
            tb.Margin = StatsPanel.Children.Count <= 2 ? Th(0, 22, 0, 12) : Th(0, 30, 0, 12);
            StatsPanel.Children.Add(tb);
        }

        void Para(string text)
        {
            var t = TB(text, 13, "#D2D0E6");
            t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 860; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = Th(0, 0, 0, 8); t.LineHeight = 20;
            StatsPanel.Children.Add(t);
        }

        void SettingRow(string title, string desc, params UIElement[] controls)
        {
            var card = new Border { Background = Br("#191A2E"), BorderBrush = Br("#272A44"), BorderThickness = Th(1, 1, 1, 1), CornerRadius = new CornerRadius(2), Padding = Th(18, 13, 18, 13), Margin = Th(0, 0, 0, 8) };
            var dp = new DockPanel();
            if (controls != null && controls.Length > 0)
            {
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = Th(16, 0, 0, 0) };
                foreach (var c in controls) right.Children.Add(c);
                DockPanel.SetDock(right, Dock.Right);
                dp.Children.Add(right);
            }
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(TB(title, 14, "#F0EEF8", FontWeights.SemiBold));
            if (desc != null) { var d = TB(desc, 12, "#8E8CA6"); d.TextWrapping = TextWrapping.Wrap; d.Margin = Th(0, 3, 0, 0); left.Children.Add(d); }
            dp.Children.Add(left);
            card.Child = dp;
            StatsPanel.Children.Add(card);
        }

        CheckBox Switch(bool on, Action<bool> changed)
        {
            var c = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, LayoutTransform = new ScaleTransform(1.4, 1.4), IsEnabled = !SafeMode };
            c.Click += (s, e) => Safe("setting", () => changed(c.IsChecked ?? false));
            return c;
        }

        Border Tile(string label, string value, string sub)
        {
            var b = new Border { Background = Br("#191A2E"), BorderBrush = Br("#272A44"), BorderThickness = Th(1, 1, 1, 1), CornerRadius = new CornerRadius(2), Padding = Th(18, 16, 18, 16), Margin = Th(0, 0, 12, 0) };
            var sp = new StackPanel();
            sp.Children.Add(TB(label.ToUpperInvariant(), 11, "#8E8CA6", FontWeights.SemiBold));
            var v = TB(value, 26, "#F0EEF8", FontWeights.SemiBold); v.Margin = Th(0, 6, 0, 2); v.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(v);
            var s = TB(sub, 12, "#8E8CA6"); s.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(s);
            b.Child = sp;
            return b;
        }

        // ---------------------------------------------------------------- stats
        void BuildStatsPage()
        {
            PageHeader("Your stats", "Esc or the library button below to go back");
            var all = Stats.All;
            double total = all.Values.Sum(x => x.Seconds);
            int launches = all.Values.Sum(x => x.Launches);
            int played = Lib.Games.Count(g => { var st = Stats.Get(g.Id); return st != null && st.Played; });
            var top = all.Where(kv => kv.Value.Seconds >= 1).OrderByDescending(kv => kv.Value.Seconds).Select(kv => kv.Key).FirstOrDefault();
            var log = SessionLog.Read();
            double avg = log.Count > 0 ? log.Average(x => x.Seconds) : launches > 0 ? total / launches : 0;

            var tiles = new UniformGrid { Columns = 4, Margin = Th(0, 22, -12, 0) };
            tiles.Children.Add(Tile("Total play time", Fmt.Duration(total), "across " + all.Values.Count(x => x.Seconds >= 1) + " games"));
            tiles.Children.Add(Tile("Launches", launches.ToString(), "about " + Fmt.Duration(avg) + " per session"));
            tiles.Children.Add(Tile("Games played", played + " / " + Lib.Games.Count, "of the games you own"));
            tiles.Children.Add(top != null ? Tile("Most played", Catalog.Short(top), Fmt.Duration(all[top].Seconds) + "  ·  " + Catalog.Title(top)) : Tile("Most played", "—", "play something!"));
            StatsPanel.Children.Add(tiles);

            var ranked = all.Where(kv => kv.Value.Seconds >= 1).OrderByDescending(kv => kv.Value.Seconds).Take(12).ToList();
            if (ranked.Count > 0)
            {
                Section("Most played");
                double max = ranked[0].Value.Seconds;
                foreach (var kv in ranked)
                {
                    var row = new Grid { Margin = Th(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    var name = TB("TH" + Catalog.NumText(kv.Key) + "   " + Catalog.Title(kv.Key), 13, "#E8E6F0");
                    name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; name.Margin = Th(0, 0, 14, 0);
                    row.Children.Add(name);
                    var track = new Grid();
                    double frac = Math.Max(0.01, kv.Value.Seconds / max);
                    track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
                    track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac + 0.0001, GridUnitType.Star) });
                    track.Children.Add(new Border { Height = 14, CornerRadius = new CornerRadius(2), Background = Br(Catalog.Has(kv.Key) ? Catalog.ColorHex(kv.Key) : accentHex) });
                    Grid.SetColumn(track, 1); row.Children.Add(track);
                    var tt = TB(Fmt.Duration(kv.Value.Seconds), 13, "#F0EEF8", FontWeights.SemiBold); tt.HorizontalAlignment = HorizontalAlignment.Right; tt.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(tt, 2); row.Children.Add(tt);
                    var ll = TB(Fmt.Plural(kv.Value.Launches, "launch", "launches"), 12, "#8E8CA6"); ll.HorizontalAlignment = HorizontalAlignment.Right; ll.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(ll, 3); row.Children.Add(ll);
                    StatsPanel.Children.Add(row);
                }
            }

            Section("Last 12 weeks");
            var today = DateTime.Now.Date;
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var weeks = Enumerable.Range(0, 12).Reverse().Select(w =>
            {
                var from = monday.AddDays(-7 * w); var to = from.AddDays(7);
                return Tuple.Create(from, log.Where(x => x.Start >= from && x.Start < to).Sum(x => x.Seconds));
            }).ToList();
            double maxW = weeks.Max(x => x.Item2);
            if (maxW <= 0) StatsPanel.Children.Add(TB("Nothing recorded yet. Every session you play from now on shows up here.", 13, "#8E8CA6"));
            else
            {
                var chart = new Grid { Height = 200 };
                chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                chart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < weeks.Count; i++)
                {
                    var wk = weeks[i];
                    chart.ColumnDefinitions.Add(new ColumnDefinition());
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = Th(5, 0, 5, 0) };
                    if (wk.Item2 >= 60)
                    {
                        double hrs = wk.Item2 / 3600;
                        var v = TB(hrs >= 1 ? hrs.ToString("0.#", Fmt.Inv) + "h" : ((int)(wk.Item2 / 60)) + "m", 11, "#B8B6CC");
                        v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = Th(0, 0, 0, 4);
                        stack.Children.Add(v);
                    }
                    stack.Children.Add(new Border
                    {
                        Height = Math.Max(3, 150 * wk.Item2 / maxW), CornerRadius = new CornerRadius(2, 2, 0, 0),
                        Background = Br(wk.Item1 == monday ? accentLightHex : wk.Item2 > 0 ? accentDarkHex : "#272A44"),
                    });
                    Grid.SetColumn(stack, i); chart.Children.Add(stack);
                    var d = TB(wk.Item1 == monday ? "This week" : wk.Item1.ToString("d MMM", Fmt.Inv), 11, "#8E8CA6");
                    d.HorizontalAlignment = HorizontalAlignment.Center; d.Margin = Th(0, 6, 0, 0);
                    Grid.SetRow(d, 1); Grid.SetColumn(d, i); chart.Children.Add(d);
                }
                StatsPanel.Children.Add(chart);
            }

            if (log.Count > 0)
            {
                Section("Recent sessions");
                foreach (var s in log.OrderByDescending(x => x.Start).Take(10))
                {
                    var row = new DockPanel { Margin = Th(0, 0, 0, 6) };
                    var dur = TB(Fmt.Duration(s.Seconds), 13, "#F0EEF8", FontWeights.SemiBold);
                    DockPanel.SetDock(dur, Dock.Right); row.Children.Add(dur);
                    var ago = Fmt.Ago(s.Start);
                    var when = TB(char.ToUpperInvariant(ago[0]) + ago.Substring(1) + ", " + s.Start.ToString("HH:mm", Fmt.Inv), 13, "#8E8CA6");
                    when.Width = 170; DockPanel.SetDock(when, Dock.Left); row.Children.Add(when);
                    row.Children.Add(TB("Touhou " + Catalog.NumText(s.Id) + "  ·  " + Catalog.Title(s.Id), 13, "#E8E6F0"));
                    StatsPanel.Children.Add(row);
                }
            }
        }

        // ---------------------------------------------------------------- settings
        void BuildSettingsPage()
        {
            PageHeader("Settings", SafeMode ? "Safe mode: changes are disabled until you restart normally." : "Esc or the library button below to go back");

            Section("Library");
            SettingRow("thcrap folder", Lib.Thcrap != null ? Lib.Thcrap.Root : "Not found yet",
                FlatBtn("Open", () => { if (Lib.Thcrap != null) OpenFolder(Lib.Thcrap.Root); }),
                FlatBtn("Change…", () => { if (PickThcrap()) Reload(); }));
            SettingRow("Refresh library", "Rescan thcrap for new games, shortcuts and language configurations.",
                FlatBtn("Refresh", () => Reload(() => StatusText.Text += "  ·  refreshed")));
            SettingRow("Show games I don't own", "Adds the missing games as greyed-out cards that link to the Steam store.",
                Switch(S.ShowUnowned, v => { S.ShowUnowned = v; S.Save(); RebuildCards(); if (v) StartCoverDownload(false, true); }));

            BuildLanguageSection();
            ThpracSettings();

            Section("Playing");
            SettingRow("Close after launch", "Hide in the system tray while you play. Play time is still counted; click the tray icon to see it.",
                Switch(S.CloseAfterLaunch, v => { S.CloseAfterLaunch = v; S.Save(); }));

            Section("Appearance");
            var sw = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var k in ThemeKeys)
            {
                var key = k;
                var dot = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = Th(6, 0, 0, 0), Background = Br(ThemeHex[key]), Cursor = Cursors.Hand,
                    BorderBrush = Br(S.Theme == key ? "#FFFFFF" : "#33FFFFFF"), BorderThickness = Th(2.5, 2.5, 2.5, 2.5), ToolTip = ThemeLabel[key],
                };
                if (!SafeMode) dot.MouseLeftButtonUp += (s, e) => Safe("theme", () => SetTheme(key));
                sw.Children.Add(dot);
            }
            var per = FlatBtn("Per game", () => SetTheme("game"));
            per.Margin = Th(12, 0, 0, 0); per.IsEnabled = !SafeMode;
            if (S.Theme == "game") { per.Background = Br(accentHex); per.BorderBrush = Br(accentLightHex); }
            sw.Children.Add(per);
            string themeName;
            if (!ThemeLabel.TryGetValue(S.Theme ?? "", out themeName)) themeName = "Purple";
            SettingRow("Theme", "Accent colour: " + themeName + ". \"Per game\" takes the colour of the game you point at (the background tint follows it).", sw);

            var slider = new Slider { Minimum = 0.6, Maximum = 1.6, Width = 220, Value = S.CardScale, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            var pct = TB(string.Format(Fmt.Inv, "{0:0}%", S.CardScale * 100), 13, "#D2D0E6"); pct.Width = 48; pct.TextAlignment = TextAlignment.Right; pct.VerticalAlignment = VerticalAlignment.Center;
            slider.ValueChanged += (s, e) => { SetCardScale(slider.Value); pct.Text = string.Format(Fmt.Inv, "{0:0}%", S.CardScale * 100); };
            slider.LostMouseCapture += (s, e) => { S.Save(); SharpenCoversLater(); };
            SettingRow("Card size", "How big the game covers are. Ctrl + mouse wheel works too.", slider, pct);

            Section("Sounds");
            SettingRow("Menu sounds", "Little blips when you move around, open a game and launch it.",
                Switch(S.SoundsOn, v => { S.SoundsOn = v; Sounds.Enabled = v; S.Save(); Sounds.Play("select"); }));
            var vol = new Slider { Minimum = 0, Maximum = 100, Width = 220, Value = S.SoundVolume, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !SafeMode };
            var vpct = TB(S.SoundVolume + "%", 13, "#D2D0E6"); vpct.Width = 48; vpct.TextAlignment = TextAlignment.Right; vpct.VerticalAlignment = VerticalAlignment.Center;
            vol.ValueChanged += (s, e) => { S.SoundVolume = (int)vol.Value; vpct.Text = S.SoundVolume + "%"; };
            vol.LostMouseCapture += (s, e) => { S.Save(); int v = S.SoundVolume; Task.Run(() => Sounds.Init(v)).ContinueWith(t => Sounds.Play("select")); };
            SettingRow("Volume", "Applied when you release the slider (you will hear a preview).", vol, vpct);

            Section("Controller");
            SettingRow("Xbox-compatible controllers",
                (SafeMode ? "Off in safe mode. " : padIndex >= 0 ? "Controller connected (slot " + (padIndex + 1) + "). " : "No controller detected right now. ") +
                "Works automatically while the window is active; Help lists the buttons.");

            Section("Covers (SteamGridDB)");
            SettingRow("API key", string.IsNullOrEmpty(S.SgdbKey) ? "No key yet, so covers cannot be downloaded. The key is free." : "Key saved. Covers for new games download automatically.",
                FlatBtn(string.IsNullOrEmpty(S.SgdbKey) ? "Set key…" : "Change key…", () => { if (AskApiKey()) { StartCoverDownload(false, false); BuildSettingsPage(); } }));
            var who = S.SgdbAuthor == AppSettings.TelanusId ? "telanus" : !string.IsNullOrEmpty(S.SgdbAuthor) ? S.SgdbAuthor : "none (top-rated covers)";
            SettingRow("Preferred artist", "Their covers are used first: " + who,
                FlatBtn("Change…", () => { if (AskAuthor()) { StatusText.Text = "Artist saved. Use \"Re-download all\" to apply it to existing covers."; BuildSettingsPage(); } }));
            var dl = FlatBtn("Download missing", () => { S.CoverMisses.Clear(); StartCoverDownload(false, false); });
            var re = FlatBtn("Re-download all", () => StartCoverDownload(true, false));
            dl.IsEnabled = coverRun == null && !SafeMode; re.IsEnabled = coverRun == null && !SafeMode && !string.IsNullOrEmpty(S.SgdbKey);
            SettingRow("Cover images", "You can also drop your own th06.png / th17.jpg (and thXX_hero.png banners) in the covers folder.",
                dl, re, FlatBtn("Open folder", () => { Directory.CreateDirectory(Paths.Covers); OpenFolder(Paths.Covers); }));

            Section("Backups");
            BackupSettings();

            Section("Your data");
            SettingRow("Launcher folder", "Settings, play time, session history, save backups and covers are stored next to TSLauncher.exe. The settings and play-time files are written safely and backed up every day automatically.",
                FlatBtn("Open", () => OpenFolder(Paths.Root)),
                FlatBtn("Backups", () => { Directory.CreateDirectory(Paths.Backups); OpenFolder(Paths.Backups); }),
                FlatBtn("Logs", () => OpenFolder(Paths.Logs)));
            var setup = FlatBtn("Run setup", ShowSetup); setup.IsEnabled = !SafeMode;
            SettingRow("First-run setup", "Go through the welcome steps again (thcrap folder, covers, preferences).", setup);

            Section("About");
            var check = FlatBtn("Check now", () => CheckForUpdate(true)); check.IsEnabled = !SafeMode;
            SettingRow("Updates", AppInfo.Name + " " + AppInfo.Version + ". Checks GitHub for a new version when it starts, and offers to install it for you.",
                Switch(S.CheckUpdates, v => { S.CheckUpdates = v; S.Save(); }),
                check,
                FlatBtn("Release notes", () => { try { Process.Start(Updater.ReleasesPage); } catch { } }));
            StatsPanel.Children.Add(TB(AppInfo.Name + " " + AppInfo.Version + "  ·  game and language picker for thcrap  ·  github.com/" + Updater.Repo, 13, "#8E8CA6"));
        }

        // ---------------------------------------------------------------- language manager
        bool thcrapOpened;

        void BuildLanguageSection()
        {
            Section("Languages");
            var thcrapExe = Library.ThcrapExe(Lib.Thcrap);
            var open = FlatBtn("Open thcrap", OpenThcrap);
            open.IsEnabled = thcrapExe != null && !SafeMode;
            SettingRow("Add a language", "Languages are thcrap configurations. Open thcrap, create a configuration with the patches you want, and TS Launcher picks it up when you come back.",
                open, FlatBtn("Refresh", () => Reload(() => StatusText.Text += "  ·  refreshed")));

            if (Lib.AllLangs.Count == 0)
            {
                Para(Lib.Thcrap == null ? "Set your thcrap folder above to see your languages." : "No configurations yet: create one in thcrap.");
                return;
            }
            foreach (var li in Lib.AllLangs)
            {
                var info = li;
                string custom;
                bool renamed = S.LangNames.TryGetValue(info.Code, out custom) && !string.IsNullOrWhiteSpace(custom);
                var desc = new List<string> { info.Code };
                if (renamed) desc.Add("automatic name: " + Langs.AutoLabel(info.Code));
                if (info.Hidden) desc.Add("hidden");
                else if (info.OnlyFor != null)
                {
                    var names = info.OnlyFor.OrderBy(Catalog.Number).Select(id => Catalog.Has(id) ? Catalog.Short(id) : id).ToList();
                    desc.Add("only in " + (names.Count <= 4 ? string.Join(", ", names) : Fmt.Plural(names.Count, "game", "games")));
                }
                else desc.Add("in " + Fmt.Plural(info.Games, "game", "games") + " (all)");
                if (info.Config == null) desc.Add("from shortcuts only");

                var rename = FlatBtn("Rename…", () => RenameLang(info.Code));
                var games = FlatBtn("Games…", () => ChooseLangGames(info.Code, null));
                games.IsEnabled = !SafeMode && Lib.Games.Count > 0;
                var hide = FlatBtn(info.Hidden ? "Show" : "Hide", () => ToggleLangHidden(info.Code));
                var del = FlatBtn("Delete…", () => DeleteLang(info));
                rename.IsEnabled = hide.IsEnabled = !SafeMode;
                games.ToolTip = "Choose which games offer this language (for a patch made for one game only)";
                del.IsEnabled = !SafeMode && info.Config != null && File.Exists(info.Config);
                if (info.Config == null) del.ToolTip = "This language comes from shortcuts, not from a thcrap configuration.";

                var title = Langs.Label(info.Code) + (info.Hidden ? "   (hidden)" : "");
                SettingRow(title, string.Join("   ·   ", desc), games, rename, hide, del);
                if (info.Hidden)
                {
                    var row = (Border)StatsPanel.Children[StatsPanel.Children.Count - 1];
                    row.Opacity = 0.6;
                }
            }
        }

        void OpenThcrap()
        {
            var exe = Library.ThcrapExe(Lib.Thcrap);
            if (exe == null) { ShowError("Couldn't find thcrap.exe in your thcrap folder."); return; }
            var p = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Lib.Thcrap.Root, UseShellExecute = true });
            thcrapOpened = true;   // refreshed when you come back to TS Launcher
            StatusText.Text = "thcrap is open: your library refreshes when you come back here.";
            if (p != null)
            {
                try
                {
                    p.EnableRaisingEvents = true;
                    p.Exited += (s, e) => W.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!thcrapOpened) return;
                        thcrapOpened = false;
                        Reload(() => StatusText.Text += "  ·  refreshed after thcrap");
                    }));
                }
                catch { }
            }
        }

        void ApplyLangNames()
        {
            Langs.Custom = new Dictionary<string, string>(S.LangNames, StringComparer.OrdinalIgnoreCase);
            S.Save();
            RebuildCards();
            if (View == "settings") BuildSettingsPage();
        }

        void RenameLang(string code)
        {
            var v = Ask("Name to show for \"" + code + "\" (only in TS Launcher; thcrap keeps its own name).\n\nLeave empty to go back to the automatic name: " + Langs.AutoLabel(code),
                "Rename language", Langs.Label(code));
            if (v == null) return;
            v = v.Trim();
            if (v.Length == 0 || v == Langs.AutoLabel(code)) S.LangNames.Remove(code); else S.LangNames[code] = v;
            ApplyLangNames();
        }

        /// <summary>ids null or covering every game = no restriction. Reopens 'reopen' in the game window afterwards.</summary>
        void SetLangGames(string code, IEnumerable<string> ids, Game reopen)
        {
            var set = ids == null ? null : new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            bool all = set == null || Lib.Games.All(g => set.Contains(g.Id));
            if (all) S.LangGames.Remove(code); else S.LangGames[code] = set;
            S.Save();
            Sounds.Play("toggle");
            if (reopen != null) HidePicker(true);
            ApplyLangPrefs(() =>
            {
                if (reopen == null) return;
                var fresh = Lib.Games.FirstOrDefault(x => x.Id == reopen.Id);
                if (fresh != null) ShowPicker(fresh);
            });
            if (set != null && set.Count == 0) StatusText.Text = Langs.Label(code) + " isn't offered in any game now (Settings > Languages to change it).";
        }

        void ChooseLangGames(string code, Game reopen)
        {
            HashSet<string> current;
            S.LangGames.TryGetValue(code, out current);
            var dlg = new Window
            {
                Title = "Games for " + Langs.Label(code), Owner = W, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 460, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(400, W.ActualHeight - 60), ResizeMode = ResizeMode.NoResize,
                Background = Br("#191A2E"), ShowInTaskbar = false, FontFamily = new FontFamily("Segoe UI"),
            };
            var root = new DockPanel { Margin = Th(22, 18, 22, 18) };
            var head = new StackPanel();
            head.Children.Add(TB(Langs.Label(code), 20, "#F0EEF8", FontWeights.SemiBold));
            var sub = TB("Choose the games that offer this language. Useful for a patch made for one game only.", 12, "#8E8CA6");
            sub.TextWrapping = TextWrapping.Wrap; sub.Margin = Th(0, 4, 0, 12);
            head.Children.Add(sub);
            var allBox = new CheckBox { IsChecked = current == null, Margin = Th(0, 0, 0, 10), Content = TB("All games (also new ones)", 14, "#F0EEF8", FontWeights.SemiBold) };
            head.Children.Add(allBox);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = Th(0, 14, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            var boxes = new List<CheckBox>();
            foreach (var g in Lib.Games)
            {
                var cb = new CheckBox
                {
                    Tag = g.Id, Margin = Th(0, 0, 0, 7), IsChecked = current == null || current.Contains(g.Id),
                    Content = TB("Touhou " + g.NumText + "   " + g.Title, 13, "#D2D0E6"),
                };
                boxes.Add(cb);
                list.Children.Add(cb);
            }
            Action sync = () => { foreach (var b in boxes) b.IsEnabled = !(allBox.IsChecked ?? false); };
            allBox.Click += (s, e) => { if (allBox.IsChecked ?? false) foreach (var b in boxes) b.IsChecked = true; sync(); };
            sync();
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 });

            var cancel = new Button { Content = "Cancel", Style = (Style)W.FindResource("FlatBtn"), IsCancel = true };
            var save = new Button { Content = "Save", Style = (Style)W.FindResource("AccentBtn"), IsDefault = true, Margin = Th(10, 0, 0, 0), Padding = Th(22, 8, 22, 8) };
            bool ok = false;
            save.Click += (s, e) => { ok = true; dlg.Close(); };
            buttons.Children.Add(cancel); buttons.Children.Add(save);
            dlg.Content = root;
            dlg.ShowDialog();
            if (!ok) return;
            if (allBox.IsChecked ?? false) SetLangGames(code, null, reopen);
            else SetLangGames(code, boxes.Where(b => b.IsChecked ?? false).Select(b => (string)b.Tag), reopen);
        }

        void ToggleLangHidden(string code)
        {
            if (!S.HiddenLangs.Remove(code)) S.HiddenLangs.Add(code);
            S.Save();
            Sounds.Play("toggle");
            ApplyLangPrefs();
        }

        void DeleteLang(LangInfo info)
        {
            if (info.Config == null || !File.Exists(info.Config)) return;
            if (!Confirm("Delete the thcrap configuration \"" + info.Code + "\" (" + Langs.Label(info.Code) + ")?\n\n" +
                         "Its file (" + Path.GetFileName(info.Config) + ") goes to the Recycle Bin, so it disappears from thcrap too but can be recovered. " +
                         "If you only want it out of TS Launcher, use Hide instead.", "Delete language")) return;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(info.Config, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Log.App("Deleted thcrap configuration " + info.Config);
            S.LangNames.Remove(info.Code); S.HiddenLangs.Remove(info.Code);
            foreach (var k in S.LastLang.Where(kv => string.Equals(kv.Value, info.Code, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList()) S.LastLang.Remove(k);
            Langs.Custom.Remove(info.Code);
            S.Save();
            Reload();
        }

        bool PickThcrap()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select your thcrap folder (the one containing thcrap.exe / bin\\thcrap_loader.exe)" })
            {
                dlg.SelectedPath = !string.IsNullOrEmpty(S.ThcrapPath) ? S.ThcrapPath : Paths.Root;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
                if (Library.Inspect(dlg.SelectedPath) == null) { ShowError("That folder doesn't look like a thcrap install (no thcrap_loader.exe found)."); return false; }
                S.ThcrapPath = dlg.SelectedPath; S.Save();
                return true;
            }
        }

        // ---------------------------------------------------------------- help
        void KeyRow(string[] keys, string desc)
        {
            var row = new DockPanel { Margin = Th(0, 0, 0, 8) };
            var caps = new StackPanel { Orientation = Orientation.Horizontal, Width = 230 };
            foreach (var k in keys)
                caps.Children.Add(new Border
                {
                    Background = Br("#272A44"), BorderBrush = Br("#383C5E"), BorderThickness = Th(1, 1, 1, 3), CornerRadius = new CornerRadius(2),
                    Padding = Th(9, 3, 9, 3), Margin = Th(0, 0, 6, 0), Child = TB(k, 12, "#F0EEF8", FontWeights.SemiBold),
                });
            DockPanel.SetDock(caps, Dock.Left); row.Children.Add(caps);
            var d = TB(desc, 13, "#D2D0E6"); d.VerticalAlignment = VerticalAlignment.Center; d.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(d);
            StatsPanel.Children.Add(row);
        }

        void BuildHelpPage()
        {
            PageHeader("Help", "Esc or the library button below to go back");
            Section("The basics");
            Para("TS Launcher reads your thcrap setup. Every game thcrap knows about shows up here, and every configuration you made in thcrap (en, fr, en-lol...) becomes a language you can pick. Games found through Steam are labelled \"Steam\", the others \"Local\".");
            Para("A game is missing? Open thcrap, let it search for games (or point it to the game's folder), then use Refresh in Settings. A language is missing? Settings > Languages > Open thcrap: create a configuration with the patches you want, and it appears in every game when you come back.");
            Para("Settings > Languages also lets you give a language a friendlier name (for example \"French (TSA)\" instead of base_tsa-MoFrance), choose which games offer it (Games…, handy for a patch made for one game only), hide the ones you don't use, or delete a configuration (it goes to the Recycle Bin).");
            Para("Quicker: right-click a language in a game's window and choose \"Only show in this game\" or \"Don't show in this game\".");
            Para("The filters above your games (Played, Never played, Steam, Local) narrow the library down; All shows everything again.");

            Section("Playing");
            Para("Click a game to choose its language. Right-click a game to start it straight away in the language you used last time. The big banner at the top always shows your last game with a Play button.");
            Para("In the game window, the big Play button replays your last language and the tiles start any other one. \"Game settings mode\" makes the tiles open the game's own options program (custom.exe) instead, \"Play original\" starts the game without any patch, \"Cover…\" shows every cover and banner available so you can pick one (or use your own picture), \"Replays\" lists your replays, \"Saves\" backs up or restores your progress, and the star adds the game to your favorites.");

            Section("Keyboard");
            KeyRow(new[] { "←", "→" }, "Move along a row of games");
            KeyRow(new[] { "↑", "↓" }, "Move between rows (up from the first row goes to the Play button)");
            KeyRow(new[] { "Enter" }, "Open the selected game. In the game window your last language is pre-selected, so Enter twice plays it.");
            KeyRow(new[] { "←", "→", "↑", "↓" }, "In the game window: move between the language tiles");
            KeyRow(new[] { "F" }, "Add / remove the selected game from your favorites");
            KeyRow(new[] { "Ctrl", "F" }, "Search");
            KeyRow(new[] { "Esc" }, "Close a window, clear the search, or come back to the library");
            KeyRow(new[] { "Wheel" }, "Scroll the page up and down");
            KeyRow(new[] { "Shift", "Wheel" }, "Scroll the row of games under the mouse sideways (or click the arrows on its edges)");
            KeyRow(new[] { "Ctrl", "Wheel" }, "Make the covers bigger or smaller");

            Section("Controller");
            Para("Any Xbox-compatible controller works while the TS Launcher window is active (it is ignored while you play, so the game never gets double input).");
            KeyRow(new[] { "D-pad", "Stick" }, "Move around (hold to repeat)");
            KeyRow(new[] { "A" }, "Open the game / play the selected language");
            KeyRow(new[] { "B" }, "Back");
            KeyRow(new[] { "X" }, "Play the game straight away in your last language");
            KeyRow(new[] { "Y" }, "Favorite");
            KeyRow(new[] { "LB", "RB" }, "Switch between Library, Stats, Settings and Help");
            KeyRow(new[] { "Start" }, "Settings");

            Section("Play time");
            Para("Time is counted while a game started from TS Launcher is running, whether the window is open or hidden in the tray (\"Close after launch\" in Settings). Closing the window during a game also sends it to the tray so nothing is lost. The session ends when the game closes; there is a short grace period in case the game restarts itself (Steam does this).");
            Para("Games started some other way are not counted. If a game's time stays at 0, tracking.log (Settings > Your data) shows what TS Launcher saw. The Stats page has your totals, a weekly chart and recent sessions.");

            Section("thprac");
            Para("thprac is the practice tool most Touhou players use (practice any stage, infinite lives, replay tools…). In a game's window, turn on the thprac button: from then on that game starts as usual (through thcrap, in your language) and thprac attaches itself a couple of seconds later. It works for the main games 6 to 20 and Great Fairy Wars, local or Steam copies.");
            Para("TS Launcher finds thprac.exe if it's next to TS Launcher, thcrap or your games; otherwise show it where it is in Settings > thprac. You can download thprac from its GitHub page (Settings > thprac > Download).");

            Section("Covers");
            Para("Covers and banners come from SteamGridDB and need a free API key (Settings > Covers). Covers by your preferred artist are used first, telanus by default. Cover… in a game's window shows all the options. You can also use your own pictures: name them th06.png, th17.jpg and so on (thXX_hero.png for the wide banner) and put them in the covers folder.");

            Section("Replays and save backups");
            Para("Replays in a game's window lists that game's replay files, newest first. For Touhou 10 and later it also shows the difficulty, character, result, player name and score. You can rename, back up or delete them (deleted replays go to the Recycle Bin), and add replay files someone shared with you.");
            Para("Each game's save (score.dat and its settings file) is copied to the backups folder when the game starts and when it closes. The last 10 different versions are kept. To go back to one, open the game, click Saves and pick a date: your current save is backed up first, so a restore can always be undone.");

            Section("Screenshots");
            Para("Screenshots in a game's window shows the pictures the game saved (its snapshot folder), newest first. Click one to open it; right-click to copy it (to paste in Discord or anywhere), show it in its folder or delete it.");

            Section("Backups on another PC, and Steam hours");
            Para("Settings > Backups > Backup folder lets you keep the backups in a folder of your choice, for example in OneDrive or Google Drive, so they follow you to every PC. Your existing backups are copied there.");
            Para("Import (same section) brings in backups from another PC or a copy: it adds the save and replay backups, can restore the newest save of each game (your current save is backed up first), and can bring back your play time and session history. When a save is restored, it goes where this PC keeps that game's save now, even if the backup was made elsewhere.");
            Para("Steam play time imports the hours Steam recorded for your Steam copies. For each game the higher number is kept, so time isn't counted twice.");

            Section("If something goes wrong");
            Para("If TS Launcher hits an error it shows what happened and writes a report in the logs folder. If it fails while starting, the next start offers safe mode: your settings stay untouched and covers, sounds and the controller are off, so you can still play. Your settings and play-time files are written safely (never half-written) and backed up every day; if one gets damaged it is restored from a backup automatically.");

            Section("Updates");
            Para("When it starts, TS Launcher checks GitHub for a new version. If there is one, a bar at the top offers to install it: it downloads the new TSLauncher.exe, replaces the old one and restarts, keeping all your settings and play time. You can turn the check off or check by hand in Settings > About.");

            Section("Privacy");
            Para("Everything stays on your computer, in TS Launcher's folder. The only things that go online are cover downloads from SteamGridDB (using your key), the update check on GitHub (which sends nothing about you), and Steam store pages you choose to open.");
        }

        // ---------------------------------------------------------------- first-run setup
        int setupStep;
        ThcrapInfo setupThcrap;

        void ShowSetup()
        {
            HidePicker(true);
            setupStep = 0;
            setupThcrap = Lib.Thcrap;
            SetupOverlay.Visibility = Visibility.Visible;
            BuildSetupStep();
        }

        void SetupTitle(string step, string title, string text)
        {
            SetupPanel.Children.Clear();
            if (step != null) SetupPanel.Children.Add(TB(step, 12, accentLightHex, FontWeights.SemiBold));
            var t = TB(title, 26, "#F0EEF8", FontWeights.SemiBold); t.Margin = Th(0, 6, 0, 10); t.TextWrapping = TextWrapping.Wrap;
            SetupPanel.Children.Add(t);
            if (text != null) { var p = TB(text, 14, "#D2D0E6"); p.TextWrapping = TextWrapping.Wrap; p.LineHeight = 21; p.Margin = Th(0, 0, 0, 16); SetupPanel.Children.Add(p); }
        }

        void SetupButtons(string primary, Action next, string secondary = null, Action other = null, string back = null, Action onBack = null)
        {
            var dp = new DockPanel { Margin = Th(0, 18, 0, 0) };
            if (back != null) { var b = FlatBtn(back, onBack); b.Margin = Th(0, 0, 0, 0); DockPanel.SetDock(b, Dock.Left); dp.Children.Add(b); }
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (secondary != null) right.Children.Add(FlatBtn(secondary, other));
            var p = new Button { Style = (Style)W.FindResource("AccentBtn"), Content = primary, Margin = Th(10, 0, 0, 0) };
            p.Click += (s, e) => Safe("setup", next);
            right.Children.Add(p);
            dp.Children.Add(right);
            SetupPanel.Children.Add(dp);
            W.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => p.Focus()));
        }

        void BuildSetupStep()
        {
            switch (setupStep)
            {
                case 0:
                    SetupTitle(null, "Welcome to TS Launcher", "All your Touhou games in one place, in whichever thcrap language you want. Three quick steps and you're ready to play; everything can be changed later in Settings.");
                    SetupButtons("Get started", () => { setupStep = 1; BuildSetupStep(); });
                    break;
                case 1:
                    if (setupThcrap != null)
                    {
                        SetupTitle("STEP 1 OF 3", "Found your thcrap folder", setupThcrap.Root);
                        var info = TB("TS Launcher reads its game list and language configurations from there.", 13, "#8E8CA6");
                        info.TextWrapping = TextWrapping.Wrap;
                        SetupPanel.Children.Add(info);
                        SetupButtons("Use this folder", () => { S.ThcrapPath = setupThcrap.Root; setupStep = 2; BuildSetupStep(); },
                            "Choose another…", () => { if (PickThcrap()) { setupThcrap = Library.Inspect(S.ThcrapPath); BuildSetupStep(); } },
                            "Back", () => { setupStep = 0; BuildSetupStep(); });
                    }
                    else
                    {
                        SetupTitle("STEP 1 OF 3", "Where is thcrap?", "TS Launcher uses thcrap (the Touhou Community Reliant Automatic Patcher) to find your games and languages. Point it to the folder that contains thcrap.exe.");
                        SetupButtons("Choose folder…", () => { if (PickThcrap()) { setupThcrap = Library.Inspect(S.ThcrapPath); BuildSetupStep(); } },
                            "Skip for now", () => { setupStep = 2; BuildSetupStep(); },
                            "Back", () => { setupStep = 0; BuildSetupStep(); });
                    }
                    break;
                case 2:
                    SetupTitle("STEP 2 OF 3", "Covers (optional)", "Game covers and banners come from SteamGridDB, which needs a free key: log in there with Steam, open Preferences > API, copy the key and paste it below.");
                    var box = new TextBox { Text = S.SgdbKey ?? "", FontSize = 14, Padding = Th(10, 8, 10, 8), Background = Br("#272A44"), Foreground = Br("#F0EEF8"), BorderBrush = Br("#383C5E"), CaretBrush = Br("#F0EEF8") };
                    SetupPanel.Children.Add(box);
                    var open = FlatBtn("Open the SteamGridDB key page", () => { try { Process.Start("https://www.steamgriddb.com/profile/preferences/api"); } catch { } });
                    open.Margin = Th(0, 10, 0, 0); open.HorizontalAlignment = HorizontalAlignment.Left;
                    SetupPanel.Children.Add(open);
                    SetupButtons("Continue", () =>
                    {
                        var key = box.Text.Trim();
                        if (key.Length > 0 && key != S.SgdbKey && !TrySetKey(key)) return;
                        setupStep = 3; BuildSetupStep();
                    }, "Skip", () => { setupStep = 3; BuildSetupStep(); }, "Back", () => { setupStep = 1; BuildSetupStep(); });
                    break;
                default:
                    SetupTitle("STEP 3 OF 3", "A few preferences", null);
                    var sw = new StackPanel { Orientation = Orientation.Horizontal, Margin = Th(0, 0, 0, 16) };
                    sw.Children.Add(new TextBlock { Text = "Accent colour", Foreground = Br("#D2D0E6"), FontSize = 14, Width = 160, VerticalAlignment = VerticalAlignment.Center });
                    foreach (var k in ThemeKeys.Concat(new[] { "game" }))
                    {
                        var key = k;
                        var dot = new Border
                        {
                            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = Th(0, 0, 8, 0), Cursor = Cursors.Hand, ToolTip = ThemeLabel[key],
                            Background = key == "game" ? (Brush)new LinearGradientBrush(Col("#B3243A"), Col("#3E9C6B"), 45) : Br(ThemeHex[key]),
                            BorderBrush = Br(S.Theme == key ? "#FFFFFF" : "#33FFFFFF"), BorderThickness = Th(2.5, 2.5, 2.5, 2.5),
                        };
                        dot.MouseLeftButtonUp += (s, e) => { S.Theme = key; InitTheme(); BuildSetupStep(); };
                        sw.Children.Add(dot);
                    }
                    SetupPanel.Children.Add(sw);
                    Func<string, string, bool, Action<bool>, UIElement> opt = (title, desc, on, set) =>
                    {
                        var c = new CheckBox { IsChecked = on, Margin = Th(0, 0, 0, 12), Foreground = Br("#F0EEF8") };
                        var sp = new StackPanel();
                        sp.Children.Add(TB(title, 14, "#F0EEF8", FontWeights.SemiBold));
                        var d = TB(desc, 12, "#8E8CA6"); d.TextWrapping = TextWrapping.Wrap; sp.Children.Add(d);
                        c.Content = sp;
                        c.Click += (s, e) => set(c.IsChecked ?? false);
                        return c;
                    };
                    SetupPanel.Children.Add(opt("Menu sounds", "Little blips when you move around and launch games.", S.SoundsOn, v => { S.SoundsOn = v; Sounds.Enabled = v; }));
                    SetupPanel.Children.Add(opt("Close after launch", "Hide in the system tray while you play (play time is still counted).", S.CloseAfterLaunch, v => S.CloseAfterLaunch = v));
                    SetupButtons("Start playing", () =>
                    {
                        S.SetupDone = true; S.Save();
                        SetupOverlay.Visibility = Visibility.Collapsed;
                        Sounds.Play("launch");
                        Reload(() => StartCoverDownload(false, true));
                    }, null, null, "Back", () => { setupStep = 2; BuildSetupStep(); });
                    break;
            }
        }
    }
}
