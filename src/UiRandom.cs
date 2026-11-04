// TS Launcher - "Give me a game to play": a random game, weighted towards the ones you've played least.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TSLauncher
{
    public sealed partial class LauncherWindow
    {
        static readonly Random Rng = new Random();
        DispatcherTimer randomDelay;
        Game randomPending;

        /// <summary>The language a game would be launched in by the random button, or null if it has none to use.</summary>
        string RandomLangFor(Game g)
        {
            if (S.RandomLang != null && g.Lang(S.RandomLang) != null) return S.RandomLang;
            var last = LastLang(g);
            if (last != null) return last;
            return g.Langs.Count == 1 ? g.Langs[0].Code : null;   // only one choice: no guessing involved
        }

        /// <summary>
        /// How likely a game is to be picked: 1 / (1 + hours played). A game you've never played counts 1,
        /// one with 1 hour 0.5, with 9 hours 0.1. The previous pick counts a quarter, so it rarely comes up twice in a row.
        /// </summary>
        double RandomWeight(Game g)
        {
            var st = Stats.Get(g.Id);
            double hours = st == null ? 0 : st.Seconds / 3600.0;
            double w = 1.0 / (1.0 + hours);
            if (g.Id == S.LastRandom) w *= 0.25;
            return w;
        }

        /// <summary>The games in the draw: owned, not left out in Settings, not running, and with a language to start in.</summary>
        List<Game> RandomPool()
        {
            return Lib.Games.Where(g => !g.Unowned && !S.RandomExclude.Contains(g.Id) && SessionOf(g) == null && RandomLangFor(g) != null).ToList();
        }

        /// <summary>Each game in the draw with its chance (0..1).</summary>
        Dictionary<string, double> RandomChances(IEnumerable<Game> pool)
        {
            var list = pool.ToList();
            double total = list.Sum(RandomWeight);
            return list.ToDictionary(g => g.Id, g => total > 0 ? RandomWeight(g) / total : 0);
        }

        void GiveMeAGame()
        {
            if (randomPending != null) return;
            var pool = RandomPool();
            if (pool.Count == 0)
            {
                bool none = Lib.Games.Count(g => !g.Unowned) == 0;
                ShowNotice(none ? "There are no games to pick from yet: set your thcrap folder in Settings."
                                : "No game can be picked: every game is left out of the draw, running, or has never been started in a language. Settings > Random game.",
                           none ? null : "Open settings", none ? (Action)null : () => ShowView("settings"));
                return;
            }
            double total = pool.Sum(RandomWeight), roll = Rng.NextDouble() * total;
            var pick = pool[pool.Count - 1];
            foreach (var g in pool) { roll -= RandomWeight(g); if (roll <= 0) { pick = g; break; } }

            // Show what came up for a moment (its card highlighted), then start it. Esc cancels.
            randomPending = pick;
            var lang = RandomLangFor(pick);
            Sounds.Play("select");
            Border card;
            if (cards.TryGetValue(pick.Id, out card) && card.IsVisible) SetSelection(card);
            StatusText.Text = "Picked Touhou " + pick.NumText + " - " + pick.Title + " in " + Langs.Label(lang) + ". Starting… (Esc to cancel)";
            if (randomDelay == null)
            {
                randomDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
                randomDelay.Tick += (s, e) =>
                {
                    randomDelay.Stop();
                    var g = randomPending;
                    randomPending = null;
                    if (g == null) return;
                    var code = RandomLangFor(g);
                    if (code == null) return;
                    S.LastRandom = g.Id; S.Save();
                    Safe("random game", () => { current = g; CustomChk.IsChecked = false; LaunchLang(code); });
                };
            }
            randomDelay.Start();
        }

        /// <summary>Esc during the short reveal: don't start the game.</summary>
        bool CancelRandom()
        {
            if (randomPending == null) return false;
            randomDelay.Stop();
            randomPending = null;
            StatusText.Text = "Random game cancelled.";
            Sounds.Play("back");
            return true;
        }

        // ---------------------------------------------------------------- settings
        void RandomSettings()
        {
            Section("Random game");
            var langName = S.RandomLang == null ? "Each game's last language" : Langs.Label(S.RandomLang);
            var pickLang = FlatBtn(langName + "  ▾", () => { });
            pickLang.IsEnabled = !SafeMode;
            pickLang.Click += (s, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = pickLang, Placement = PlacementMode.Bottom };
                Action<string, string> add = (header, code) =>
                {
                    var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = S.RandomLang == code };
                    mi.Click += (s2, e2) => Safe("setting", () => { S.RandomLang = code; S.Save(); BuildSettingsPage(); });
                    menu.Items.Add(mi);
                };
                add("Each game's last language", null);
                menu.Items.Add(new Separator());
                foreach (var li in Lib.AllLangs.Where(x => !x.Hidden)) add(Langs.Label(li.Code), li.Code);
                menu.IsOpen = true;
            };
            SettingRow("Language", S.RandomLang == null
                    ? "Games start in the language you last played them in."
                    : "Games that don't offer " + Langs.Label(S.RandomLang) + " start in the language you last played them in.",
                pickLang);

            var owned = Lib.Games.Where(g => !g.Unowned).ToList();
            int inDraw = owned.Count(g => !S.RandomExclude.Contains(g.Id));
            int noLang = owned.Count(g => !S.RandomExclude.Contains(g.Id) && RandomLangFor(g) == null);
            var desc = inDraw + " of " + owned.Count + " games. The less you've played a game, the more likely it comes up.";
            if (noLang > 0) desc += " " + Fmt.Plural(noLang, "game is", "games are") + " skipped until you've started " + (noLang == 1 ? "it" : "them") + " once in a language.";
            var choose = FlatBtn("Choose games…", ChooseRandomGames);
            choose.IsEnabled = !SafeMode && owned.Count > 0;
            SettingRow("Games in the draw", desc, choose);
        }

        void ChooseRandomGames()
        {
            var owned = Lib.Games.Where(g => !g.Unowned).ToList();
            var dlg = new Window
            {
                Title = "Games in the draw", Owner = W, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 500, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(400, W.ActualHeight - 60), ResizeMode = ResizeMode.NoResize,
                Background = Br("#191A2E"), ShowInTaskbar = false, FontFamily = new FontFamily("Tahoma"),
            };
            var root = new DockPanel { Margin = Th(22, 18, 22, 18) };
            var head = new StackPanel();
            head.Children.Add(TB("Games in the draw", 20, "#F0EEF8", FontWeights.SemiBold));
            var sub = TB("Untick the games \"Give me a game to play\" should never pick. The percentage is each game's chance right now: the less you've played it, the higher.", 12, "#8E8CA6");
            sub.TextWrapping = TextWrapping.Wrap; sub.Margin = Th(0, 4, 0, 12);
            head.Children.Add(sub);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var buttons = new DockPanel { Margin = Th(0, 14, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            var boxes = new List<CheckBox>();
            var chance = new Dictionary<string, TextBlock>();
            Action refresh = () =>
            {
                var picked = owned.Where(g => boxes.Any(b => (string)b.Tag == g.Id && (b.IsChecked ?? false)) && SessionOf(g) == null && RandomLangFor(g) != null);
                var odds = RandomChances(picked);
                foreach (var g in owned)
                {
                    double p;
                    var tb = chance[g.Id];
                    if (odds.TryGetValue(g.Id, out p)) tb.Text = p >= 0.995 ? "100%" : p < 0.01 ? "<1%" : Math.Round(p * 100) + "%";
                    else tb.Text = RandomLangFor(g) == null ? "no language yet" : SessionOf(g) != null ? "running" : "";
                }
            };
            foreach (var g in owned)
            {
                var row = new DockPanel { Margin = Th(0, 0, 0, 7) };
                var pct = TB("", 12, "#8E8CA6");
                pct.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(pct, Dock.Right);
                row.Children.Add(pct);
                chance[g.Id] = pct;
                var st = Stats.Get(g.Id);
                var played = st != null && st.Played ? "   " + Fmt.Duration(st.Seconds) : "";
                var cb = new CheckBox { Tag = g.Id, IsChecked = !S.RandomExclude.Contains(g.Id), Content = TB("Touhou " + g.NumText + "   " + g.Title + played, 13, "#D2D0E6") };
                cb.Click += (s, e) => refresh();
                boxes.Add(cb);
                row.Children.Add(cb);
                list.Children.Add(row);
            }
            refresh();
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 440 });

            var all = new Button { Content = "All", Style = (Style)W.FindResource("FlatBtn") };
            var none = new Button { Content = "None", Style = (Style)W.FindResource("FlatBtn"), Margin = Th(8, 0, 0, 0) };
            all.Click += (s, e) => { foreach (var b in boxes) b.IsChecked = true; refresh(); };
            none.Click += (s, e) => { foreach (var b in boxes) b.IsChecked = false; refresh(); };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(all); left.Children.Add(none);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = new Button { Content = "Cancel", Style = (Style)W.FindResource("FlatBtn"), IsCancel = true };
            var save = new Button { Content = "Save", Style = (Style)W.FindResource("AccentBtn"), IsDefault = true, Margin = Th(10, 0, 0, 0), Padding = Th(22, 8, 22, 8) };
            bool ok = false;
            save.Click += (s, e) => { ok = true; dlg.Close(); };
            right.Children.Add(cancel); right.Children.Add(save);
            DockPanel.SetDock(right, Dock.Right);
            buttons.Children.Add(right);
            buttons.Children.Add(left);
            dlg.Content = root;
            dlg.ShowDialog();
            if (!ok) return;
            foreach (var b in boxes)
            {
                var id = (string)b.Tag;
                if (b.IsChecked ?? false) S.RandomExclude.Remove(id); else S.RandomExclude.Add(id);
            }
            S.Save();
            Sounds.Play("toggle");
            BuildSettingsPage();
        }
    }
}
