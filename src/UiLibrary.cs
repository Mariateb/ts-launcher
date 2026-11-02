// TS Launcher - library view: covers loaded in the background, cards, rows, Continue banner, sorting.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TSLauncher
{
    /// <summary>
    /// Decodes pictures on worker threads at the size they are shown, and keeps them in memory
    /// (keyed by file, size and modification time) so rebuilding the library is instant.
    /// </summary>
    public static class ImageCache
    {
        static readonly ConcurrentDictionary<string, BitmapSource> Cache = new ConcurrentDictionary<string, BitmapSource>();

        static string Key(string path, int width, bool gray)
        {
            long ticks = 0;
            try { ticks = File.GetLastWriteTimeUtc(path).Ticks; } catch { }
            return path + "|" + width + "|" + gray + "|" + ticks;
        }

        public static BitmapSource TryGet(string path, int width, bool gray = false)
        {
            if (path == null) return null;
            BitmapSource b;
            return Cache.TryGetValue(Key(path, width, gray), out b) ? b : null;
        }

        public static BitmapSource Decode(string path, int width, bool gray)
        {
            var key = Key(path, width, gray);
            BitmapSource cached;
            if (Cache.TryGetValue(key, out cached)) return cached;
            var bytes = File.ReadAllBytes(path);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            bi.DecodePixelWidth = width;
            bi.EndInit();
            bi.Freeze();
            BitmapSource result = bi;
            if (gray)
            {
                var f = new FormatConvertedBitmap();
                f.BeginInit(); f.Source = bi; f.DestinationFormat = PixelFormats.Gray8; f.EndInit();
                f.Freeze();
                result = f;
            }
            Cache[key] = result;
            return result;
        }

        /// <summary>Calls back on the UI thread once decoded (immediately if already in memory).</summary>
        public static void Load(Dispatcher ui, string path, int width, bool gray, Action<BitmapSource> done)
        {
            if (path == null) return;
            var hit = TryGet(path, width, gray);
            if (hit != null) { done(hit); return; }
            Task.Run(() =>
            {
                try { var b = Decode(path, width, gray); ui.BeginInvoke(new Action(() => done(b))); }
                catch (Exception ex) { Log.App("Could not load picture " + path + ": " + ex.Message); }
            });
        }

        public static void Forget(string name)
        {
            foreach (var k in Cache.Keys.Where(k => k.IndexOf(System.IO.Path.DirectorySeparatorChar + name + ".", StringComparison.OrdinalIgnoreCase) >= 0).ToList())
            {
                BitmapSource b; Cache.TryRemove(k, out b);
            }
        }

        static readonly ConcurrentDictionary<string, BitmapSource> Icons = new ConcurrentDictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        public static void LoadIcon(Dispatcher ui, string exe, Action<BitmapSource> done)
        {
            if (string.IsNullOrEmpty(exe)) return;
            BitmapSource hit;
            if (Icons.TryGetValue(exe, out hit)) { if (hit != null) done(hit); return; }
            Task.Run(() =>
            {
                BitmapSource src = null;
                try
                {
                    if (File.Exists(exe))
                        using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                        {
                            src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            src.Freeze();
                        }
                }
                catch { }
                Icons[exe] = src;
                if (src != null) ui.BeginInvoke(new Action(() => done(src)));
            });
        }
    }

    public sealed partial class LauncherWindow
    {
        sealed class Shelf { public string Name; public StackPanel Box, Row; public TextBlock Title; public ScrollViewer Scroll; }

        readonly Dictionary<string, Border> cards = new Dictionary<string, Border>();
        readonly Dictionary<string, TextBlock> cardTitles = new Dictionary<string, TextBlock>();
        readonly List<Shelf> shelves = new List<Shelf>();
        ScaleTransform cardScale;
        Border selCard;
        ScrollViewer hoverShelf;
        Game continueGame;
        Button playBtn, otherBtn;

        IEnumerable<Game> AllCardGames { get { return S.ShowUnowned ? Lib.Games.Concat(Lib.Unowned) : Lib.Games; } }
        string LastLang(Game g) { string l; return g != null && S.LastLang.TryGetValue(g.Id, out l) && g.Lang(l) != null ? l : null; }
        Session SessionOf(Game g) { return Track.Sessions.FirstOrDefault(x => x.Game.Id == g.Id && x.Detected); }

        string BadgeText(Game g)
        {
            var last = LastLang(g);
            if (last != null) return "▶  " + Langs.Label(last);
            int n = g.Langs.Count;
            return n == 0 ? "No language config" : n == 1 ? "1 language" : n + " languages";
        }

        string StatLine(Game g)
        {
            var st = Stats.Get(g.Id);
            return (st == null || !st.Played ? "Never played" : Fmt.Duration(st.Seconds)) + "  ·  " + g.Source;
        }

        /// <summary>Frozen brushes are shared read-only and skip WPF's change tracking.</summary>
        static T Frozen<T>(T b) where T : Freezable { b.Freeze(); return b; }

        // ---------------------------------------------------------------- cards
        Border NewCard(Game g)
        {
            var color = Col(Catalog.ColorHex(g.Id));
            bool unowned = g.Unowned;
            var coverPath = SafeMode ? null : Covers.Find(g.Id);
            bool hasCover = coverPath != null;
            bool playing = SessionOf(g) != null;
            bool fav = S.Favorites.Contains(g.Id);

            var card = new Border
            {
                Width = 200, Height = 300, CornerRadius = new CornerRadius(3), Margin = Th(4, 0, 4, 0),
                Cursor = Cursors.Hand, BorderThickness = Th(2, 2, 2, 2), Tag = g, ClipToBounds = false,
                RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = new Point(0.5, 0.5),
                Background = Frozen(new LinearGradientBrush(color, Mix(color, Colors.Black, 0.78), 65)),
                ToolTip = unowned ? "Touhou " + g.NumText + " - " + g.Title + "\nNot in your library - click to look it up on Steam"
                                  : "Touhou " + g.NumText + " - " + g.Title + "\nClick: choose language  ·  Right-click: replay last language  ·  F: favorite",
            };
            if (unowned) card.Opacity = 0.5;
            card.Uid = playing ? "#AA5BE08A" : "#22FFFFFF";
            card.BorderBrush = Br(card.Uid);

            var grid = new Grid();
            var art = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
            var artClip = new Border { CornerRadius = new CornerRadius(2), ClipToBounds = true, Child = art };
            grid.Children.Add(artClip);
            if (hasCover)
                ImageCache.Load(W.Dispatcher, coverPath, 440, unowned, bmp =>
                {
                    art.Source = bmp;
                    art.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(220))));
                });
            else
            {
                var kanji = TB("東方", 92, "#14FFFFFF", FontWeights.Bold);
                kanji.HorizontalAlignment = HorizontalAlignment.Right; kanji.VerticalAlignment = VerticalAlignment.Center;
                kanji.Margin = Th(0, 0, -18, 30); kanji.RenderTransform = new RotateTransform(90); kanji.RenderTransformOrigin = new Point(0.5, 0.5);
                grid.Children.Add(kanji);
                var num = TB(g.NumText, 62, "#F2FFFFFF", FontWeights.Black);
                num.Margin = Th(14, 4, 0, 0); num.HorizontalAlignment = HorizontalAlignment.Left; num.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(num);
                if (g.InCatalog)
                {
                    var sh = TB(Catalog.Short(g.Id), 13, "#B0FFFFFF", FontWeights.SemiBold);
                    sh.Margin = Th(17, 82, 0, 0); sh.HorizontalAlignment = HorizontalAlignment.Left; sh.VerticalAlignment = VerticalAlignment.Top;
                    grid.Children.Add(sh);
                }
                if (!unowned && !SafeMode)
                {
                    var icon = new Image { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = fav ? Th(0, 10, 42, 0) : Th(0, 10, 10, 0) };
                    RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                    grid.Children.Add(icon);
                    ImageCache.LoadIcon(W.Dispatcher, g.Exe, bmp => icon.Source = bmp);
                }
            }
            if (fav)
            {
                var starB = new Border { Background = Br("#B0000000"), CornerRadius = new CornerRadius(3), Width = 28, Height = 28, Margin = Th(0, 8, 8, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                var star = TB("★", 16, "#FFD75E"); star.HorizontalAlignment = HorizontalAlignment.Center; star.VerticalAlignment = VerticalAlignment.Center;
                starB.Child = star;
                grid.Children.Add(starB);
            }

            var strip = new Border { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 2, 2), Padding = Th(12, 26, 12, 12) };
            var sb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(235, 12, 11, 22), 0.45));
            strip.Background = Frozen(sb);
            var sp = new StackPanel();
            if (hasCover)
            {
                strip.Padding = Th(12, 22, 12, 10);
                var tt = TB(g.Title, 15, "#FFFFFFFF", FontWeights.SemiBold);
                tt.TextWrapping = TextWrapping.Wrap; tt.Margin = Th(0, 0, 0, 5); tt.Visibility = Visibility.Collapsed;
                tt.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 1 };
                sp.Children.Add(tt);
                cardTitles[g.Id] = tt;
            }
            else
            {
                sp.Children.Add(TB("TOUHOU " + g.NumText, 11, "#B8FFFFFF", FontWeights.SemiBold));
                var t = TB(g.Title, 15, "#FFFFFFFF", FontWeights.SemiBold);
                t.TextWrapping = TextWrapping.Wrap; t.Margin = Th(0, 2, 0, 6);
                sp.Children.Add(t);
            }
            sp.Children.Add(unowned ? TB("Not owned", 12, "#C6D4DF", FontWeights.SemiBold)
                          : playing ? TB("●  Playing now", 12, "#5BE08A", FontWeights.SemiBold)
                          : TB(BadgeText(g), 12, baseLightHex));
            var stat = TB(unowned ? "Find on Steam  ↗" : StatLine(g), 11, "#A8FFFFFF");
            stat.Margin = Th(0, 2, 0, 0);
            sp.Children.Add(stat);
            strip.Child = sp;
            grid.Children.Add(strip);
            card.Child = grid;

            card.MouseEnter += (s, e) => SetGlow(card, true);
            card.MouseLeave += (s, e) => { if (card != selCard) SetGlow(card, false); };
            card.MouseLeftButtonUp += (s, e) => Safe("open game", () => OpenCard(g));
            card.MouseRightButtonUp += (s, e) => Safe("play", () => { if (g.Unowned) OpenStore(g); else PlayLast(g); });
            return card;
        }

        static void Scale(UIElement el, double to)
        {
            var st = (ScaleTransform)el.RenderTransform;
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(140))) { EasingFunction = new QuadraticEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void SetGlow(Border card, bool on)
        {
            var g = (Game)card.Tag;
            TextBlock title;
            cardTitles.TryGetValue(g.Id, out title);
            if (on)
            {
                FollowGameTheme(g);
                Sounds.Play("move");
                card.Effect = new DropShadowEffect { Color = accentLight, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9 };
                var pulse = new SolidColorBrush(Colors.White);
                pulse.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.White, accentLight, new Duration(TimeSpan.FromMilliseconds(750))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                card.BorderBrush = pulse;
                card.BorderThickness = Th(3, 3, 3, 3);
                Scale(card, 1.06);
                if (title != null) title.Visibility = Visibility.Visible;
            }
            else
            {
                card.Effect = null;   // a blurred shadow on every card is costly to redraw; only the focused one glows
                card.BorderBrush = Br(card.Uid);
                card.BorderThickness = Th(2, 2, 2, 2);
                Scale(card, 1.0);
                if (title != null) title.Visibility = Visibility.Collapsed;
            }
        }

        // ---------------------------------------------------------------- rows
        Shelf NewShelf(string name)
        {
            var sh = new Shelf { Name = name, Box = new StackPanel { Margin = Th(0, 0, 0, 2) } };
            var head = new StackPanel { Margin = Th(24, 10, 24, 0) };
            sh.Title = TB(name, 12, "#8E8CA6", FontWeights.SemiBold);
            head.Children.Add(sh.Title);
            var lb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            lb.GradientStops.Add(new GradientStop(Color.FromArgb(110, accentLight.R, accentLight.G, accentLight.B), 0));
            lb.GradientStops.Add(new GradientStop(Color.FromArgb(0, accentLight.R, accentLight.G, accentLight.B), 0.7));
            head.Children.Add(new Rectangle { Height = 1, Margin = Th(0, 6, 0, 0), Fill = lb });
            sh.Box.Children.Add(head);

            var frame = new Grid();
            sh.Scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            sh.Row = new StackPanel { Orientation = Orientation.Horizontal, Margin = Th(20, 14, 20, 16), LayoutTransform = cardScale };
            sh.Scroll.Content = sh.Row;
            frame.Children.Add(sh.Scroll);

            var arrows = new List<Border>();
            foreach (int dir in new[] { -1, 1 })
            {
                var gb = new LinearGradientBrush { StartPoint = new Point(dir < 0 ? 0 : 1, 0), EndPoint = new Point(dir < 0 ? 1 : 0, 0) };
                gb.GradientStops.Add(new GradientStop(Color.FromArgb(235, 13, 13, 24), 0));
                gb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 13, 13, 24), 1));
                var chev = TB(dir < 0 ? "" : "", 24, "#F0EEF8");
                chev.FontFamily = new FontFamily("Segoe MDL2 Assets");
                chev.VerticalAlignment = VerticalAlignment.Center; chev.HorizontalAlignment = dir < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right; chev.Margin = Th(18, 0, 18, 0);
                var a = new Border { Width = 84, HorizontalAlignment = dir < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Cursor = Cursors.Hand, Background = gb, Child = chev, Visibility = Visibility.Collapsed };
                int d = dir; var sv = sh.Scroll;
                a.MouseEnter += (s, e) => chev.Foreground = Br(accentLightHex);
                a.MouseLeave += (s, e) => chev.Foreground = Br("#F0EEF8");
                a.MouseLeftButtonUp += (s, e) => { ScrollBy(sv, true, d * sv.ViewportWidth * 0.8); e.Handled = true; };
                frame.Children.Add(a);
                arrows.Add(a);
            }
            frame.MouseEnter += (s, e) =>
            {
                hoverShelf = sh.Scroll;
                var v = sh.Scroll.ScrollableWidth > 1 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var a in arrows) a.Visibility = v;
            };
            frame.MouseLeave += (s, e) => { hoverShelf = null; foreach (var a in arrows) a.Visibility = Visibility.Collapsed; };
            sh.Box.Children.Add(frame);
            return sh;
        }

        static readonly Dictionary<string, string> SortNames = new Dictionary<string, string>
        {
            { "release", "Release order" }, { "recent", "Last played" }, { "most", "Most played" }, { "name", "Name" },
        };

        List<Game> OrderedOwned()
        {
            Func<Game, int> fav = g => S.Favorites.Contains(g.Id) ? 0 : 1;
            Func<Game, long> last = g => { var st = Stats.Get(g.Id); var d = st == null ? null : st.LastPlayedDate; return d == null ? 0 : d.Value.Ticks; };
            Func<Game, double> secs = g => { var st = Stats.Get(g.Id); return st == null ? 0 : st.Seconds; };
            var q = Lib.Games.OrderBy(fav);
            switch (S.SortMode)
            {
                case "recent": return q.ThenByDescending(last).ThenBy(g => Catalog.Number(g.Id)).ToList();
                case "most": return q.ThenByDescending(secs).ThenBy(g => Catalog.Number(g.Id)).ToList();
                case "name": return q.ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                default: return q.ThenBy(g => Catalog.Number(g.Id)).ToList();
            }
        }

        void RebuildCards()
        {
            selCard = null;
            cards.Clear(); cardTitles.Clear();
            foreach (var g in AllCardGames) cards[g.Id] = NewCard(g);
            UpdateContinue();
            ApplySort();
        }

        void ApplySort()
        {
            string label;
            SortBtn.Content = "Sort: " + (SortNames.TryGetValue(S.SortMode, out label) ? label : "Release order") + "  ▾";
            foreach (var c in cards.Values) { var p = c.Parent as Panel; if (p != null) p.Children.Remove(c); }
            GamePanel.Children.Clear();
            shelves.Clear();
            var groups = new List<KeyValuePair<string, List<Game>>>
            {
                new KeyValuePair<string, List<Game>>("YOUR GAMES", OrderedOwned()),   // favorites already first
                new KeyValuePair<string, List<Game>>("NOT IN YOUR LIBRARY", S.ShowUnowned ? Lib.Unowned : new List<Game>()),
            };
            foreach (var grp in groups)
            {
                if (grp.Value.Count == 0) continue;
                var sh = NewShelf(grp.Key);
                foreach (var g in grp.Value) { Border c; if (cards.TryGetValue(g.Id, out c)) sh.Row.Children.Add(c); }
                GamePanel.Children.Add(sh.Box);
                shelves.Add(sh);
            }
            ApplyFilter();
        }

        // ---------------------------------------------------------------- quick filters
        static readonly string[] FilterKeys = { "all", "played", "never", "steam", "local" };
        static readonly Dictionary<string, string> FilterNames = new Dictionary<string, string>
        {
            { "all", "All" }, { "played", "Played" }, { "never", "Never played" }, { "steam", "Steam" }, { "local", "Local" },
        };
        readonly Dictionary<string, Button> filterChips = new Dictionary<string, Button>();

        void BuildFilterChips()
        {
            var bar = SortBtn.Parent as Panel;
            if (bar == null) return;
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = Th(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var k in FilterKeys)
            {
                var key = k;
                var b = new Button { Content = FilterNames[key], Style = (Style)W.FindResource("FlatBtn"), Padding = Th(12, 5, 12, 5), Margin = Th(0, 0, 6, 0), FontSize = 12, Focusable = false };
                b.Click += (s, e) => { S.Filter = key; S.Save(); Sounds.Play("toggle"); UpdateFilterChips(); ApplyFilter(); };
                chips.Children.Add(b);
                filterChips[key] = b;
            }
            bar.Children.Insert(0, chips);
            UpdateFilterChips();
        }

        void UpdateFilterChips()
        {
            if (S == null || string.IsNullOrEmpty(accentHex)) return;
            foreach (var kv in filterChips)
            {
                bool on = kv.Key == (S.Filter ?? "all");
                kv.Value.Background = Br(on ? accentHex : "#272A44");
                kv.Value.BorderBrush = Br(on ? accentLightHex : "#383C5E");
                kv.Value.Foreground = Br(on ? "#FFFFFF" : "#D2D0E6");
            }
        }

        bool PassesFilter(Game g)
        {
            switch (S.Filter ?? "all")
            {
                case "played": { var st = Stats.Get(g.Id); return !g.Unowned && st != null && st.Played; }
                case "never": { var st = Stats.Get(g.Id); return !g.Unowned && (st == null || !st.Played); }
                case "steam": return !g.Unowned && g.Source == "Steam";
                case "local": return !g.Unowned && g.Source == "Local";
                default: return true;
            }
        }

        void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            SearchHint.Visibility = q.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            int shown = 0, shownUnowned = 0;
            foreach (var g in AllCardGames)
            {
                Border card;
                if (!cards.TryGetValue(g.Id, out card)) continue;
                bool show = true;
                if (q.Length > 0)
                {
                    CatalogEntry e;
                    var info = Catalog.Games.TryGetValue(g.Id, out e) ? e.Title + " " + e.Short : "";
                    var hay = g.Id + " touhou " + g.NumText + " " + info;
                    show = hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                show = show && PassesFilter(g);
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) { if (g.Unowned) shownUnowned++; else shown++; }
            }
            foreach (var sh in shelves)
            {
                int n = sh.Row.Children.Cast<UIElement>().Count(c => c.Visibility == Visibility.Visible);
                sh.Box.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
                sh.Title.Text = sh.Name + "   ·   " + n;
            }
            if (selCard != null && selCard.Visibility != Visibility.Visible) SetSelection(null);
            var label = q.Length > 0 ? Fmt.Plural(shown, "result", "results") + " for \"" + q + "\"" : Fmt.Plural(shown, "game", "games");
            if (shownUnowned > 0) label += "  ·  " + shownUnowned + " not owned";
            if ((S.Filter ?? "all") != "all") label += "  ·  showing " + FilterNames[S.Filter].ToLowerInvariant();
            if (Lib.Thcrap == null && q.Length == 0) label = "No games yet: TS Launcher needs to know where thcrap is (Settings > Library).";
            LibLabel.Text = label;
        }

        void UpdateCard(Game g)
        {
            Border old;
            cards.TryGetValue(g.Id, out old);
            var fresh = NewCard(g);
            var parent = old != null ? old.Parent as Panel : null;
            if (parent != null)
            {
                int i = parent.Children.IndexOf(old);
                parent.Children.RemoveAt(i); parent.Children.Insert(i, fresh);
                fresh.Visibility = old.Visibility;
            }
            cards[g.Id] = fresh;
            if (old != null && selCard == old) { selCard = null; SetSelection(fresh); }
            if (!g.Unowned) UpdateContinue();
            if ((S.Filter ?? "all") != "all") ApplyFilter();   // e.g. a first launch moves it out of "Never played"
        }

        void ShowSortMenu()
        {
            var m = new ContextMenu();
            foreach (var kv in SortNames)
            {
                var key = kv.Key;
                var mi = new MenuItem { Header = kv.Value, IsCheckable = true, IsChecked = S.SortMode == key };
                mi.Click += (s, e) => { S.SortMode = key; S.Save(); ApplySort(); };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            m.Items.Add(new MenuItem { Header = "Favorites always stay on the far left", IsEnabled = false });
            m.PlacementTarget = SortBtn; m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; m.IsOpen = true;
        }

        void ToggleFav(Game g)
        {
            if (g == null || g.Unowned) return;
            if (!S.Favorites.Remove(g.Id)) S.Favorites.Add(g.Id);
            S.Save();
            Sounds.Play("toggle");
            UpdateCard(g);
            var keep = selCard;
            ApplySort();
            if (keep != null && cards.ContainsValue(keep)) SetSelection(keep);
            if (Overlay.Visibility == Visibility.Visible && current != null && current.Id == g.Id) UpdateFavButton();
        }

        void OpenCard(Game g) { if (g.Unowned) OpenStore(g); else ShowPicker(g); }

        void PlayLast(Game g)
        {
            var last = LastLang(g);
            if (last != null) { current = g; CustomChk.IsChecked = false; LaunchLang(last); }
            else ShowPicker(g);
        }

        void OpenStore(Game g)
        {
            if (Confirm("You don't have Touhou " + g.NumText + " - " + g.Title + " yet.\n\nLook it up on the Steam store?", AppInfo.Name))
                System.Diagnostics.Process.Start("https://store.steampowered.com/search/?term=" + Uri.EscapeDataString("Touhou " + g.Title));
        }

        // ---------------------------------------------------------------- Continue banner
        void UpdateContinue()
        {
            Game best = null; DateTime bestT = DateTime.MinValue;
            foreach (var g in Lib.Games)
            {
                var st = Stats.Get(g.Id); var d = st == null ? null : st.LastPlayedDate;
                if (d != null && d.Value > bestT) { bestT = d.Value; best = g; }
            }
            continueGame = best;
            ContinueBanner.Child = null;
            if (best == null || SearchBox.Text.Trim().Length > 0) { ContinueBanner.Visibility = Visibility.Collapsed; return; }

            var c = Col(Catalog.ColorHex(best.Id));
            var grid = new Grid();
            var art = new Grid();
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.5));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            art.OpacityMask = mask;
            var artBg = new Border { Background = new LinearGradientBrush(c, Mix(c, Colors.Black, 0.8), 0) };
            art.Children.Add(artBg);
            var heroImg = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            art.Children.Add(heroImg);
            var heroPath = SafeMode ? null : Covers.Find(best.Id + "_hero");
            if (heroPath != null)
                ImageCache.Load(W.Dispatcher, heroPath, 1600, false, bmp => { heroImg.Source = bmp; heroImg.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(250)))); });
            var shade = new Border();
            var sb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(240, 14, 13, 26), 0));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(170, 14, 13, 26), 0.5));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(10, 14, 13, 26), 1));
            shade.Background = sb;
            art.Children.Add(shade);
            grid.Children.Add(art);

            if (heroPath == null && !SafeMode)
            {
                var coverPath = Covers.Find(best.Id);
                if (coverPath != null)
                {
                    var img = new Image { Height = 250, Margin = Th(0, 0, 70, 10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    ImageCache.Load(W.Dispatcher, coverPath, 440, false, bmp => img.Source = bmp);
                    grid.Children.Add(img);
                }
            }

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = Th(40, 0, 40, 10), HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(TB("CONTINUE PLAYING", 12, baseLightHex, FontWeights.SemiBold));
            var n = TB("TOUHOU " + best.NumText, 12, "#C8FFFFFF", FontWeights.SemiBold); n.Margin = Th(0, 8, 0, 0);
            sp.Children.Add(n);
            var t = TB(best.Title, 36, "#FFFFFFFF", FontWeights.SemiBold);
            t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 720;
            t.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 };
            sp.Children.Add(t);
            var stt = Stats.Get(best.Id);
            var parts = new List<string>();
            var ago = Fmt.Ago(stt.LastPlayedDate); if (ago != null) parts.Add("Last played " + ago);
            parts.Add(Fmt.Duration(stt.Seconds) + " played");
            var last = LastLang(best); if (last != null) parts.Add(Langs.Label(last));
            var info = TB(string.Join("   ·   ", parts), 13, "#D8FFFFFF"); info.Margin = Th(0, 6, 0, 0);
            sp.Children.Add(info);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = Th(0, 18, 0, 0) };
            playBtn = new Button { Style = (Style)W.FindResource("AccentBtn"), Content = last != null ? "▶   Play" : "▶   Play…" };
            var game = best;
            playBtn.Click += (s, e) => Safe("play", () => PlayLast(game));
            btns.Children.Add(playBtn);
            otherBtn = new Button { Style = (Style)W.FindResource("FlatBtn"), Content = "Choose language", Margin = Th(10, 0, 0, 0), Padding = Th(18, 10, 18, 10), Background = Br("#66000000"), BorderBrush = Br("#55FFFFFF") };
            otherBtn.Click += (s, e) => Safe("open game", () => ShowPicker(game));
            btns.Children.Add(otherBtn);
            sp.Children.Add(btns);
            grid.Children.Add(sp);

            ContinueBanner.Background = null;
            ContinueBanner.Child = grid;
            ContinueBanner.Visibility = Visibility.Visible;
        }

        // ---------------------------------------------------------------- keyboard selection
        List<List<Border>> CardRows()
        {
            var rows = new List<List<Border>>();
            foreach (var sh in shelves)
            {
                if (sh.Box.Visibility != Visibility.Visible) continue;
                var cs = sh.Row.Children.OfType<Border>().Where(c => c.Visibility == Visibility.Visible).ToList();
                if (cs.Count > 0) rows.Add(cs);
            }
            return rows;
        }

        void SetSelection(Border card)
        {
            var old = selCard;
            if (old != null && old != card) SetGlow(old, false);
            selCard = card;
            if (card != null) { SetGlow(card, true); EnsureVisible(card); }
        }

        // The centre of a card doesn't move when it is scaled up on focus (top-left maths would).
        double CenterX(Border c) { return c.TranslatePoint(new Point(c.ActualWidth / 2, c.ActualHeight / 2), MainScroll).X; }

        void FocusBanner()
        {
            SetSelection(null);
            ScrollTo(MainScroll, false, 0);
            if (ContinueBanner.Visibility == Visibility.Visible && playBtn != null) playBtn.Focus();
        }

        void MoveSelection(string dir)
        {
            var rows = CardRows();
            if (rows.Count == 0) return;
            int r = -1, i = -1;
            if (selCard != null) for (int k = 0; k < rows.Count; k++) { int j = rows[k].IndexOf(selCard); if (j >= 0) { r = k; i = j; break; } }
            if (r < 0) { if (dir == "Up") FocusBanner(); else SetSelection(rows[0][0]); return; }
            switch (dir)
            {
                case "Left": if (i > 0) SetSelection(rows[r][i - 1]); break;
                case "Right": if (i < rows[r].Count - 1) SetSelection(rows[r][i + 1]); break;
                default:
                    int t = dir == "Up" ? r - 1 : r + 1;
                    if (t < 0) { FocusBanner(); return; }
                    if (t >= rows.Count) return;
                    double x0 = CenterX(selCard);
                    SetSelection(rows[t].OrderBy(c => Math.Abs(CenterX(c) - x0)).First());
                    break;
            }
        }

        /// <summary>Glides the row and the page so the selected card is fully visible.</summary>
        void EnsureVisible(Border card)
        {
            double scale = S.CardScale;
            var row = card.Parent as FrameworkElement;
            var sv = row != null ? row.Parent as ScrollViewer : null;
            if (sv != null)
            {
                double cx = card.TranslatePoint(new Point(card.ActualWidth / 2, 0), sv).X, half = card.ActualWidth * scale / 2 + 40;
                if (cx - half < 0) ScrollTo(sv, true, sv.HorizontalOffset + cx - half);
                else if (cx + half > sv.ViewportWidth) ScrollTo(sv, true, sv.HorizontalOffset + cx + half - sv.ViewportWidth);
            }
            double top = card.TranslatePoint(new Point(0, 0), MainScroll).Y - 50, bottom = top + card.ActualHeight * scale + 100;
            if (top < 0) ScrollTo(MainScroll, false, MainScroll.VerticalOffset + top);
            else if (bottom > MainScroll.ViewportHeight) ScrollTo(MainScroll, false, MainScroll.VerticalOffset + bottom - MainScroll.ViewportHeight);
        }
    }
}
