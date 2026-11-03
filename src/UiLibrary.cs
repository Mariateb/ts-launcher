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

        public static BitmapSource Decode(string path, int width, bool gray, bool diskThumb = true)
        {
            var key = Key(path, width, gray);
            BitmapSource cached;
            if (Cache.TryGetValue(key, out cached)) return cached;
            BitmapSource result = diskThumb ? LoadThumb(path, width) : DecodeBytes(File.ReadAllBytes(path), width);
            if (gray)
            {
                var f = new FormatConvertedBitmap();
                f.BeginInit(); f.Source = result; f.DestinationFormat = PixelFormats.Gray8; f.EndInit();
                f.Freeze();
                result = f;
            }
            Cache[key] = result;
            return result;
        }

        public static BitmapImage DecodeBytes(byte[] bytes, int width)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            if (width > 0) bi.DecodePixelWidth = width;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        /// <summary>
        /// Full-size covers (often 1-2 MB PNGs) are decoded once, then a copy at the displayed size is kept in
        /// covers\.thumbs, so later starts only decode a small file. The copy is tied to the source's date.
        /// </summary>
        static BitmapSource LoadThumb(string path, int width)
        {
            long ticks = 0;
            try { ticks = File.GetLastWriteTimeUtc(path).Ticks; } catch { }
            var baseName = System.IO.Path.GetFileNameWithoutExtension(path) + "_" + width + "_";
            var thumb = System.IO.Path.Combine(Paths.Thumbs, baseName + ticks.ToString("x") + ".png");
            try { if (File.Exists(thumb)) return DecodeBytes(File.ReadAllBytes(thumb), 0); }
            catch { try { File.Delete(thumb); } catch { } }

            var bi = DecodeBytes(File.ReadAllBytes(path), width);
            if (bi.PixelWidth < width * 0.9 || new FileInfo(path).Length < 150 * 1024) return bi;   // small already: no copy needed
            try
            {
                Directory.CreateDirectory(Paths.Thumbs);
                var tmp = thumb + ".tmp";
                BitmapEncoder enc = new PngBitmapEncoder();
                if (!bi.Format.ToString().Contains("a") && !bi.Format.ToString().Contains("A")) enc = new JpegBitmapEncoder { QualityLevel = 92 };
                enc.Frames.Add(BitmapFrame.Create(bi));
                using (var fs = File.Create(tmp)) enc.Save(fs);
                if (File.Exists(thumb)) File.Delete(thumb);
                File.Move(tmp, thumb);
                foreach (var old in Directory.GetFiles(Paths.Thumbs, baseName + "*.png"))
                    if (!string.Equals(old, thumb, StringComparison.OrdinalIgnoreCase)) try { File.Delete(old); } catch { }
            }
            catch (Exception ex) { Log.App("Could not save thumbnail for " + path + ": " + ex.Message); }
            return bi;
        }

        /// <summary>Calls back on the UI thread once decoded (immediately if already in memory).</summary>
        /// <param name="diskThumb">false for one-off pictures (screenshots): don't keep a small copy on disk.</param>
        public static void Load(Dispatcher ui, string path, int width, bool gray, Action<BitmapSource> done, bool diskThumb = true)
        {
            if (path == null) return;
            var hit = TryGet(path, width, gray);
            if (hit != null) { done(hit); return; }
            Task.Run(() =>
            {
                try { var b = Decode(path, width, gray, diskThumb); ui.BeginInvoke(new Action(() => done(b))); }
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
        /// <summary>Per card: the caption shown on focus, and whether the card belongs to the recent row.</summary>
        sealed class CardParts { public Border Strip, Ring; public bool KeepStrip, Recent; }
        readonly Dictionary<Border, CardParts> cardParts = new Dictionary<Border, CardParts>();
        Shelf recentShelf;
        readonly List<Shelf> shelves = new List<Shelf>();
        ScaleTransform cardScale;
        Border selCard;
        ScrollViewer hoverShelf;
        Game continueGame;

        IEnumerable<Game> AllCardGames { get { return S.ShowUnowned ? Lib.Games.Concat(Lib.Unowned) : Lib.Games; } }
        string LastLang(Game g) { string l; return g != null && S.LastLang.TryGetValue(g.Id, out l) && g.Lang(l) != null ? l : null; }
        Session SessionOf(Game g) { return Track.Sessions.FirstOrDefault(x => x.Game.Id == g.Id && x.Detected); }

        string BadgeText(Game g)
        {
            var last = LastLang(g);
            if (last != null) return "▶  " + Langs.Label(last) + (UsesThprac(g) ? "  ·  thprac" : "");
            int n = g.Langs.Count;
            return n == 0 ? "No language config" : n == 1 ? "1 language" : n + " languages";
        }

        string StatLine(Game g)
        {
            var st = Stats.Get(g.Id);
            return (st == null || !st.Played ? "Never played" : Fmt.Duration(st.Seconds)) + "  ·  " + g.Source;
        }

        // ---------------------------------------------------------------- picture sizes
        double DpiScale()
        {
            try { var src = PresentationSource.FromVisual(W); if (src != null && src.CompositionTarget != null) return src.CompositionTarget.TransformToDevice.M11; }
            catch { }
            return 1.0;
        }

        /// <summary>Pixels a cover needs at the current card size and screen DPI (+ the hover zoom), in steps of 80.</summary>
        int CoverWidth()
        {
            double px = 200 * Math.Max(1.0, S.CardScale) * 1.06 * DpiScale();
            return (int)Math.Max(240, Math.Min(960, Math.Ceiling(px / 80) * 80));
        }

        /// <summary>One size for every banner (Continue banner and game window), from the screen width.</summary>
        int HeroWidth()
        {
            double px = Math.Max(1200, SystemParameters.PrimaryScreenWidth) * DpiScale();
            return (int)Math.Max(1200, Math.Min(2560, Math.Ceiling(px / 320) * 320));
        }

        /// <summary>Frozen brushes are shared read-only and skip WPF's change tracking.</summary>
        static T Frozen<T>(T b) where T : Freezable { b.Freeze(); return b; }

        // ---------------------------------------------------------------- cards
        /// <summary>
        /// A game tile. At rest it's only the art (Big Picture style); its caption appears when focused.
        /// recent: a tile of the recent row (no caption, the line under the row names it). wide: the featured tile.
        /// </summary>
        Border NewCard(Game g, bool recent = false, bool wide = false)
        {
            var color = Col(Catalog.ColorHex(g.Id));
            bool unowned = g.Unowned;
            var coverPath = NoArt ? null : Covers.Find(g.Id);
            bool hasArt = coverPath != null;
            bool playing = SessionOf(g) != null;
            bool fav = S.Favorites.Contains(g.Id);

            var card = new Border
            {
                Width = wide ? FeaturedW : 200, Height = 300, CornerRadius = new CornerRadius(4), Margin = Th(5, 0, 5, 0),
                Cursor = Cursors.Hand, BorderThickness = Th(3, 3, 3, 3), Tag = g, ClipToBounds = false,
                RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = new Point(0.5, 0.5),
                Background = Frozen(new LinearGradientBrush(color, Mix(color, Colors.Black, 0.78), 65)),
                ToolTip = unowned ? "Touhou " + g.NumText + " - " + g.Title + "\nNot in your library - click to look it up on Steam"
                                  : "Touhou " + g.NumText + " - " + g.Title + "\nClick: choose language  ·  Right-click: play in your last language  ·  F: favorite",
            };
            if (unowned) card.Opacity = 0.5;
            card.Uid = playing ? "#CC5BE08A" : "#00FFFFFF";   // resting border: invisible, green while playing
            card.BorderBrush = Br(card.Uid);

            var grid = new Grid();
            var art = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(2), ClipToBounds = true, Child = art });
            Action<BitmapSource> show = bmp =>
            {
                art.Source = bmp;
                art.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(220))));
            };
            if (wide && coverPath != null)
            {
                // the last game: its cover, a bit wider than the others, cropped evenly top and bottom
                art.VerticalAlignment = VerticalAlignment.Center;
                ImageCache.Load(W.Dispatcher, coverPath, FeaturedWidth(), false, show);
            }
            else if (coverPath != null) ImageCache.Load(W.Dispatcher, coverPath, CoverWidth(), unowned, show);
            if (!hasArt)
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
            if (fav && !recent)
            {
                var star = TB("★", 15, "#FFD75E");
                star.HorizontalAlignment = HorizontalAlignment.Right; star.VerticalAlignment = VerticalAlignment.Top; star.Margin = Th(0, 8, 10, 0);
                star.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9 };
                grid.Children.Add(star);
            }
            if (playing)
            {
                var live = new Border { Background = Br("#E62A7A45"), CornerRadius = new CornerRadius(3), Padding = Th(7, 3, 8, 3), Margin = Th(8, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                live.Child = TB("PLAYING", 10, "#FFFFFF", FontWeights.Bold);
                grid.Children.Add(live);
            }

            var parts = new CardParts { Recent = recent };
            if (!recent)
            {
                var strip = new Border { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 2, 2), Padding = Th(12, 30, 12, 11) };
                var sb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                sb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
                sb.GradientStops.Add(new GradientStop(Color.FromArgb(240, 10, 11, 20), 0.5));
                strip.Background = Frozen(sb);
                var sp = new StackPanel();
                if (!hasArt) sp.Children.Add(TB("TOUHOU " + g.NumText, 11, "#B8FFFFFF", FontWeights.SemiBold));
                var t = TB(g.Title, 15, "#FFFFFFFF", FontWeights.SemiBold);
                t.TextWrapping = TextWrapping.Wrap; t.Margin = Th(0, hasArt ? 0 : 2, 0, 5);
                sp.Children.Add(t);
                sp.Children.Add(unowned ? TB("Not owned  ·  find on Steam", 12, "#C6D4DF", FontWeights.SemiBold) : TB(BadgeText(g), 12, baseLightHex));
                if (!unowned) { var stat = TB(StatLine(g), 11, "#A8FFFFFF"); stat.Margin = Th(0, 2, 0, 0); sp.Children.Add(stat); }
                strip.Child = sp;
                parts.Strip = strip;
                parts.KeepStrip = !hasArt;   // a tile without art keeps its name visible
                strip.Visibility = parts.KeepStrip ? Visibility.Visible : Visibility.Collapsed;
                grid.Children.Add(strip);
            }
            // focus ring (Steam style): a white outline just outside the card, with the card's own edge
            // turning dark in between, so the outline reads clearly against any cover
            parts.Ring = new Border
            {
                Margin = Th(-7, -7, -7, -7), BorderThickness = Th(3, 3, 3, 3), CornerRadius = new CornerRadius(8),
                BorderBrush = Br("#FFFFFFFF"), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            grid.Children.Add(parts.Ring);
            cardParts[card] = parts;
            card.Child = grid;

            // the mouse takes over the selection (one focused card at a time, and the arrows continue from it),
            // but only when it really moves: a row scrolling under a still pointer doesn't steal the focus
            card.MouseMove += (s, e) => { if (card != selCard && RealMouseMove(e)) HoverSelect(card); };
            card.MouseLeftButtonUp += (s, e) => Safe("open game", () => OpenCard(g));
            card.MouseRightButtonUp += (s, e) => Safe("play", () => { if (g.Unowned) OpenStore(g); else PlayLast(g); });
            return card;
        }

        const double FeaturedW = 260;

        /// <summary>Decode width for the featured cover (it is wider than a normal card, so it needs more pixels).</summary>
        int FeaturedWidth()
        {
            double px = FeaturedW * Math.Max(1.0, S.CardScale) * 1.05 * DpiScale();
            return (int)Math.Max(320, Math.Min(1200, Math.Ceiling(px / 80) * 80));
        }

        static void Scale(UIElement el, double to)
        {
            var st = (ScaleTransform)el.RenderTransform;
            var dur = new Duration(TimeSpan.FromMilliseconds(140));
            var a = new DoubleAnimation(to, dur) { EasingFunction = new QuadraticEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            // the zoomed card pushes its neighbours aside instead of overlapping them
            var fe = el as FrameworkElement;
            if (fe == null) return;
            double side = 5 + (to > 1 ? fe.Width * (to - 1) / 2 + 6 : 0);
            fe.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(new Thickness(side, 0, side, 0), dur) { EasingFunction = new QuadraticEase() });
            Panel.SetZIndex(el, to > 1 ? 1 : 0);
        }

        /// <summary>A soft dark shadow under the focused card. It falls mostly downwards, away from the row title,
        /// and stays inside the room the row leaves around its cards, so nothing gets cut off.</summary>
        static readonly DropShadowEffect FocusShadow = MakeFocusShadow();
        static DropShadowEffect MakeFocusShadow()
        {
            var e = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 9, BlurRadius = 20, Opacity = 0.6, RenderingBias = RenderingBias.Performance };
            e.Freeze();
            return e;
        }

        /// <summary>Big Picture focus: a slight zoom, a white ring with a dark gap, a shadow underneath.</summary>
        void SetGlow(Border card, bool on)
        {
            var g = (Game)card.Tag;
            CardParts parts;
            cardParts.TryGetValue(card, out parts);
            if (on)
            {
                FollowGameTheme(g, false);
                FollowBackground(g, false);
                Sounds.Play("move");
                card.Effect = FocusShadow;
                card.BorderBrush = Br("#FF0B0C16");
                if (parts != null && parts.Ring != null) parts.Ring.Visibility = Visibility.Visible;
                Scale(card, 1.05);
                if (parts != null && parts.Strip != null) parts.Strip.Visibility = Visibility.Visible;
                if (parts != null && parts.Recent) ShowFocusCaption(g);
            }
            else
            {
                card.Effect = null;
                card.BorderBrush = Br(card.Uid);
                if (parts != null && parts.Ring != null) parts.Ring.Visibility = Visibility.Collapsed;
                Scale(card, 1.0);
                if (parts != null && parts.Strip != null && !parts.KeepStrip) parts.Strip.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>The big line under the recent row: name, then last played, time and language.</summary>
        void ShowFocusCaption(Game g)
        {
            if (g == null) { FocusTitle.Text = ""; FocusInfo.Inlines.Clear(); return; }
            FocusTitle.Text = g.Title;
            FocusInfo.Inlines.Clear();
            var st = Stats.Get(g.Id);
            if (SessionOf(g) != null)
            {
                FocusInfo.Inlines.Add(new System.Windows.Documents.Run("●  PLAYING NOW") { Foreground = Br("#5BE08A") });
                return;
            }
            var parts = new List<string>();
            var ago = st == null ? null : Fmt.Ago(st.LastPlayedDate);
            if (ago != null) parts.Add("LAST PLAYED " + ago.ToUpperInvariant());
            if (st != null && st.Played) parts.Add(Fmt.Duration(st.Seconds).ToUpperInvariant());
            var last = LastLang(g);
            if (last != null) parts.Add(Langs.Label(last).ToUpperInvariant());
            if (UsesThprac(g)) parts.Add("THPRAC");
            FocusInfo.Inlines.Add(new System.Windows.Documents.Run("▶  ") { Foreground = Br("#5BE08A") });
            FocusInfo.Inlines.Add(new System.Windows.Documents.Run(parts.Count > 0 ? string.Join("   ", parts) : "NOT PLAYED YET"));
        }

        // ---------------------------------------------------------------- rows
        Shelf NewShelf(string name)
        {
            var sh = new Shelf { Name = name, Box = new StackPanel { Margin = Th(0, 0, 0, 2) } };
            if (name != null)
            {
                sh.Title = TB(name, 14, "#C9CBDA", FontWeights.Bold);
                sh.Title.Margin = Th(34, 10, 24, 0);
                sh.Box.Children.Add(sh.Title);
            }

            var frame = new Grid();
            sh.Scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            sh.Row = new StackPanel { Orientation = Orientation.Horizontal, Margin = Th(32, 30, 40, 32), LayoutTransform = cardScale };
            sh.Scroll.Content = sh.Row;
            frame.Children.Add(sh.Scroll);

            var arrows = new List<Border>();
            foreach (int dir in new[] { -1, 1 })
            {
                // a small round button over the row's end (a full-height fade panel showed as a dark box around the cards)
                var chev = TB(dir < 0 ? "" : "", 16, "#F0EEF8");
                chev.FontFamily = new FontFamily("Segoe MDL2 Assets");
                chev.VerticalAlignment = VerticalAlignment.Center; chev.HorizontalAlignment = HorizontalAlignment.Center;
                var a = new Border
                {
                    Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Margin = Th(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = dir < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Cursor = Cursors.Hand,
                    Background = Br("#E6141626"), BorderBrush = Br("#40FFFFFF"), BorderThickness = Th(1, 1, 1, 1), Child = chev, Visibility = Visibility.Collapsed,
                    Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 },
                };
                int d = dir; var sv = sh.Scroll;
                a.MouseEnter += (s, e) => { a.Background = Br("#ECEBF5"); chev.Foreground = Br("#14151F"); };
                a.MouseLeave += (s, e) => { a.Background = Br("#E6141626"); chev.Foreground = Br("#F0EEF8"); };
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

        int builtCoverWidth;
        DispatcherTimer sharpenDelay;

        /// <summary>After the card size changes, rebuild once (debounced) if covers need more pixels.</summary>
        void SharpenCoversLater()
        {
            if (sharpenDelay == null)
            {
                sharpenDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                sharpenDelay.Tick += (s, e) => { sharpenDelay.Stop(); if (CoverWidth() != builtCoverWidth) RebuildCards(); };
            }
            sharpenDelay.Stop(); sharpenDelay.Start();
        }

        void RebuildCards()
        {
            builtCoverWidth = CoverWidth();
            selCard = null;
            cards.Clear(); cardParts.Clear();
            foreach (var g in AllCardGames) cards[g.Id] = NewCard(g);
            UpdateContinue();
            ApplySort();
        }

        void ApplySort()
        {
            string label;
            SortBtn.Content = (SortNames.TryGetValue(S.SortMode, out label) ? label : "Release order").ToUpperInvariant() + "  ▾";
            foreach (var c in cards.Values) { var p = c.Parent as Panel; if (p != null) p.Children.Remove(c); }
            foreach (var c in cardParts.Keys.Where(k => !cardParts[k].Recent && !cards.ContainsValue(k)).ToList()) cardParts.Remove(c);
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
            FilterTabs.Children.Clear();
            filterChips.Clear();
            foreach (var k in FilterKeys)
            {
                var key = k;
                var b = new Button { Content = FilterNames[key].ToUpperInvariant(), Style = (Style)W.FindResource("TabBtn") };
                b.Click += (s, e) => { S.Filter = key; S.Save(); Sounds.Play("toggle"); UpdateFilterChips(); ApplyFilter(); };
                FilterTabs.Children.Add(b);
                filterChips[key] = b;
            }
            UpdateFilterChips();
        }

        void UpdateFilterChips()
        {
            if (S == null) return;
            foreach (var kv in filterChips)
            {
                bool on = kv.Key == (S.Filter ?? "all");
                kv.Value.Background = Br(on ? "#2EFFFFFF" : "#00FFFFFF");
                kv.Value.Foreground = Br(on ? "#FFFFFF" : "#B4B6C8");
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
                if (sh.Title != null) sh.Title.Text = sh.Name;
            }
            if (selCard != null && selCard.Visibility != Visibility.Visible) SetSelection(null);
            var label = q.Length > 0 ? Fmt.Plural(shown, "result", "results") + " for \"" + q + "\"" : Fmt.Plural(shown, "game", "games");
            if (shownUnowned > 0) label += ", " + shownUnowned + " not owned";
            if (Lib.Thcrap == null && q.Length == 0) label = "No games yet: set your thcrap folder in Settings.";
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
            if (old != null) cardParts.Remove(old);
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

        // ---------------------------------------------------------------- recent row (Big Picture home)
        /// <summary>
        /// The top row: your last game as a wide featured tile, then the next games you played most recently.
        /// The line under the row names whichever of them is focused.
        /// </summary>
        void UpdateContinue()
        {
            var played = selfTestRecent ?? Lib.Games
                .Select(g => { var st = Stats.Get(g.Id); return new { g, d = st == null ? null : st.LastPlayedDate }; })
                .Where(x => x.d != null).OrderByDescending(x => x.d.Value).Select(x => x.g).ToList();
            continueGame = played.FirstOrDefault();

            string keepId = null;
            CardParts sp;
            if (selCard != null && cardParts.TryGetValue(selCard, out sp) && sp.Recent) keepId = ((Game)selCard.Tag).Id;
            if (recentShelf != null)
                foreach (var c in recentShelf.Row.Children.OfType<Border>().ToList()) cardParts.Remove(c);
            RecentHost.Children.Clear();
            recentShelf = null;
            if (continueGame == null || SearchBox.Text.Trim().Length > 0) { RecentPanel.Visibility = Visibility.Collapsed; return; }

            recentShelf = NewShelf(null);
            recentShelf.Row.Margin = Th(32, 30, 40, 28);
            Border keep = null;
            for (int i = 0; i < Math.Min(8, played.Count); i++)
            {
                var c = NewCard(played[i], true, i == 0);
                recentShelf.Row.Children.Add(c);
                if (played[i].Id == keepId) keep = c;
            }
            RecentHost.Children.Add(recentShelf.Box);
            RecentPanel.Visibility = Visibility.Visible;
            ShowFocusCaption(keep != null ? (Game)keep.Tag : continueGame);
            if (keep != null) { selCard = null; SetSelection(keep); }
        }

        // ---------------------------------------------------------------- keyboard selection
        List<List<Border>> CardRows()
        {
            var rows = new List<List<Border>>();
            var all = new List<Shelf>();
            if (recentShelf != null && RecentPanel.Visibility == Visibility.Visible) all.Add(recentShelf);
            all.AddRange(shelves);
            foreach (var sh in all)
            {
                if (sh.Box.Visibility != Visibility.Visible) continue;
                var cs = sh.Row.Children.OfType<Border>().Where(c => c.Visibility == Visibility.Visible).ToList();
                if (cs.Count > 0) rows.Add(cs);
            }
            return rows;
        }

        Point lastMouse = new Point(double.NaN, double.NaN);

        bool RealMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(W);
            // the first event only records where the pointer is (it may just be resting over the window)
            bool moved = !double.IsNaN(lastMouse.X) && Math.Abs(p.X - lastMouse.X) + Math.Abs(p.Y - lastMouse.Y) > 0.5;
            lastMouse = p;
            return moved;
        }

        /// <summary>Selects the card under the mouse without scrolling to it (it's already where you're pointing).</summary>
        void HoverSelect(Border card)
        {
            if (selCard != null) SetGlow(selCard, false);
            selCard = card;
            SetGlow(card, true);
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
            ScrollTo(MainScroll, false, 0);   // already on the top row: just make sure the page is at the top
        }

        void MoveSelection(string dir)
        {
            var rows = CardRows();
            if (rows.Count == 0) return;
            int r = -1, i = -1;
            if (selCard != null) for (int k = 0; k < rows.Count; k++) { int j = rows[k].IndexOf(selCard); if (j >= 0) { r = k; i = j; break; } }
            if (r < 0) { SetSelection(rows[0][0]); return; }
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
