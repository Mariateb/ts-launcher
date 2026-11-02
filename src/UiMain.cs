// TS Launcher - main window: startup, library loading, themes, views, input (keyboard/controller), tray.
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
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TSLauncher
{
    public sealed partial class LauncherWindow
    {
        // ---------------------------------------------------------------- state
        public readonly Window W;
        readonly bool SafeMode;
        AppSettings S;
        StatsStore Stats = new StatsStore();
        LibraryResult Lib = new LibraryResult();
        readonly Tracker Track = new Tracker();
        string View = "library";
        bool exitWhenIdle, reallyClose;
        WinForms.NotifyIcon tray;
        DispatcherTimer trackTimer, padTimer, coverTimer;

        // XAML elements
        TextBlock StatusText, SearchHint, DlgSub, DlgTitle, DlgStats, LangHint, LibLabel, BottomHint, NoticeText;
        TextBox SearchBox;
        Button HomeBtn, StatsBtn, SettingsBtn, HelpBtn, VanillaBtn, CoverBtn, ReplaysBtn, SavesBtn, DlgPlay, DlgFav, DlgClose, SortBtn, NoticeAction, NoticeClose;
        StackPanel GamePanel, NowPlayingPanel, CoverPanel, StatsPanel, SetupPanel;
        Grid Overlay, DlgHeaderGrid, SetupOverlay;
        Border Dialog, DlgHeader, DlgShade, NowPlaying, ContinueBanner, DlgFooter, HeaderBar, BottomBar, NoticeBar, SetupCard;
        Image DlgIcon;
        CheckBox CustomChk;
        UniformGrid LangPanel;
        ScrollViewer MainScroll, StatsScroll, LangScroll, CoverScroll;
        Rectangle SearchLine;

        T F<T>(string name) where T : class
        {
            var o = W.FindName(name) as T;
            if (o == null) throw new InvalidOperationException("Window layout is missing element '" + name + "'.");
            return o;
        }

        public LauncherWindow(bool safeMode)
        {
            SafeMode = safeMode;
            string xaml;
            using (var st = typeof(LauncherWindow).Assembly.GetManifestResourceStream("Window.xaml"))
            using (var sr = new StreamReader(st)) xaml = sr.ReadToEnd();
            W = (Window)XamlReader.Parse(xaml);

            StatusText = F<TextBlock>("StatusText"); SearchHint = F<TextBlock>("SearchHint"); DlgSub = F<TextBlock>("DlgSub");
            DlgTitle = F<TextBlock>("DlgTitle"); DlgStats = F<TextBlock>("DlgStats"); LangHint = F<TextBlock>("LangHint");
            LibLabel = F<TextBlock>("LibLabel"); BottomHint = F<TextBlock>("BottomHint"); NoticeText = F<TextBlock>("NoticeText");
            SearchBox = F<TextBox>("SearchBox");
            HomeBtn = F<Button>("HomeBtn"); StatsBtn = F<Button>("StatsBtn"); SettingsBtn = F<Button>("SettingsBtn"); HelpBtn = F<Button>("HelpBtn");
            VanillaBtn = F<Button>("VanillaBtn"); CoverBtn = F<Button>("CoverBtn"); ReplaysBtn = F<Button>("ReplaysBtn"); SavesBtn = F<Button>("SavesBtn");
            DlgPlay = F<Button>("DlgPlay"); DlgFav = F<Button>("DlgFav"); DlgClose = F<Button>("DlgClose"); SortBtn = F<Button>("SortBtn");
            NoticeAction = F<Button>("NoticeAction"); NoticeClose = F<Button>("NoticeClose");
            GamePanel = F<StackPanel>("GamePanel"); NowPlayingPanel = F<StackPanel>("NowPlayingPanel"); CoverPanel = F<StackPanel>("CoverPanel");
            StatsPanel = F<StackPanel>("StatsPanel"); SetupPanel = F<StackPanel>("SetupPanel");
            Overlay = F<Grid>("Overlay"); DlgHeaderGrid = F<Grid>("DlgHeaderGrid"); SetupOverlay = F<Grid>("SetupOverlay");
            Dialog = F<Border>("Dialog"); DlgHeader = F<Border>("DlgHeader"); DlgShade = F<Border>("DlgShade"); NowPlaying = F<Border>("NowPlaying");
            ContinueBanner = F<Border>("ContinueBanner"); DlgFooter = F<Border>("DlgFooter"); HeaderBar = F<Border>("HeaderBar"); BottomBar = F<Border>("BottomBar");
            NoticeBar = F<Border>("NoticeBar"); SetupCard = F<Border>("SetupCard");
            DlgIcon = F<Image>("DlgIcon"); CustomChk = F<CheckBox>("CustomChk"); LangPanel = F<UniformGrid>("LangPanel");
            MainScroll = F<ScrollViewer>("MainScroll"); StatsScroll = F<ScrollViewer>("StatsScroll"); LangScroll = F<ScrollViewer>("LangScroll"); CoverScroll = F<ScrollViewer>("CoverScroll");
            SearchLine = F<Rectangle>("SearchLine");

            W.Title = AppInfo.Name;
            try { W.Icon = AppIcon(); } catch { }
        }

        static ImageSource AppIcon()
        {
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
        }

        // ---------------------------------------------------------------- startup
        public void Start()
        {
            string settingsNotice = null;
            if (SafeMode)
            {
                S = new AppSettings { ReadOnly = true, SetupDone = true };
                string ignored;
                var real = AppSettings.Load(out ignored);   // read (never write) just enough to find games
                S.ThcrapPath = real.ThcrapPath; S.LastLang = real.LastLang;
                S.LangNames = real.LangNames; S.HiddenLangs = real.HiddenLangs;
                Stats.ReadOnly = true; SessionLog.ReadOnly = true;
            }
            else
            {
                SafeFile.DailySnapshot();
                S = AppSettings.Load(out settingsNotice);
            }
            Stats.Load();
            Langs.Custom = new Dictionary<string, string>(S.LangNames, StringComparer.OrdinalIgnoreCase);
            Updater.CleanupOld();
            Sounds.Enabled = S.SoundsOn && !SafeMode;
            if (!SafeMode) Task.Run(() => Sounds.Init(S.SoundVolume));

            BuildBackground();
            cardScale = new ScaleTransform(S.CardScale, S.CardScale);
            WireUp();
            InitTheme();
            ShowView("library", true);

            if (SafeMode)
                ShowNotice("Safe mode: your settings aren't loaded or changed, and covers, sounds and the controller are off.", "Restart normally", () => Program.Restart(false));
            else if (settingsNotice != null) ShowNotice(settingsNotice, null, null);
            else if (Stats.Notice != null) ShowNotice(Stats.Notice, null, null);

            RestoreWindow();
            W.Show();
            if (S.WinMaximized && !SafeMode) W.WindowState = WindowState.Maximized;
            StatusText.Text = "Loading your games…";
            W.ContentRendered += (s, e) => Program.StartupSucceeded();
            Reload(() =>
            {
                FollowGameTheme(continueGame);
                if (!SafeMode && !S.SetupDone) ShowSetup();
                else if (!SafeMode) StartCoverDownload(false, true);
                if (!SafeMode && S.CheckUpdates) CheckForUpdate(false);
            });
        }

        // ---------------------------------------------------------------- window position
        /// <summary>Reopens where it was left, as long as that spot is still on a screen.</summary>
        void RestoreWindow()
        {
            if (SafeMode || S.WinWidth == null || S.WinHeight == null || S.WinLeft == null || S.WinTop == null) return;
            double w = Math.Max(W.MinWidth, S.WinWidth.Value), h = Math.Max(W.MinHeight, S.WinHeight.Value);
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            double l = S.WinLeft.Value, t = S.WinTop.Value;
            // the title bar must be reachable: at least 120 px of it inside the desktop
            bool visible = l + w - 120 > vl && l + 120 < vl + vw && t >= vt - 10 && t + 40 < vt + vh;
            if (!visible) return;
            W.WindowStartupLocation = WindowStartupLocation.Manual;
            W.Left = l; W.Top = t; W.Width = Math.Min(w, vw); W.Height = Math.Min(h, vh);
        }

        void SaveWindow()
        {
            if (SafeMode || !W.IsLoaded) return;
            var r = W.WindowState == WindowState.Normal ? new Rect(W.Left, W.Top, W.ActualWidth, W.ActualHeight) : W.RestoreBounds;
            if (r.IsEmpty || r.Width < 100 || r.Height < 100) return;
            S.WinLeft = r.Left; S.WinTop = r.Top; S.WinWidth = r.Width; S.WinHeight = r.Height;
            S.WinMaximized = W.WindowState == WindowState.Maximized;
            S.Save();
        }

        // ---------------------------------------------------------------- updates

        void CheckForUpdate(bool manual)
        {
            if (manual) StatusText.Text = "Checking for updates…";
            Task.Run(() => Updater.Check()).ContinueWith(t =>
            {
                var info = t.Result;
                if (info == null)
                {
                    if (manual) { StatusText.Text = "TS Launcher is up to date (" + AppInfo.Version + ")."; MessageBox.Show(W, "You have the latest version (" + AppInfo.Version + ").", AppInfo.Name); }
                    return;
                }

                ShowNotice("TS Launcher " + info.VersionText + " is available (you have " + AppInfo.Version + ").", "Update now…", () => InstallUpdate(info));
                if (manual) InstallUpdate(info);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void InstallUpdate(UpdateInfo info)
        {
            if (Track.Sessions.Count > 0) { ShowError("Close your game first: updating restarts TS Launcher, which would stop the play-time count."); return; }
            var notes = (info.Notes ?? "").Trim();
            if (notes.Length > 900) notes = notes.Substring(0, 900).TrimEnd() + "…";
            var msg = "Update TS Launcher to " + info.VersionText + "?\n\n" + (notes.Length > 0 ? "What's new:\n" + notes + "\n\n" : "") +
                      "It downloads from GitHub, replaces TSLauncher.exe and restarts. Your settings and play time are kept.";
            if (MessageBox.Show(W, msg, AppInfo.Name + " update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            StatusText.Text = "Downloading TS Launcher " + info.VersionText + "…";
            NoticeBar.Visibility = Visibility.Collapsed;
            W.Cursor = Cursors.Wait;
            Task.Run(() => Updater.Install(info)).ContinueWith(t =>
            {
                W.Cursor = null;
                if (t.IsFaulted)
                {
                    var ex = t.Exception.GetBaseException();
                    Log.Error("update", ex);
                    if (MessageBox.Show(W, "The update couldn't be installed:\n" + ex.Message + "\n\nOpen the download page to update by hand?", AppInfo.Name,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        try { Process.Start(info.PageUrl ?? Updater.ReleasesPage); } catch { }
                    StatusText.Text = "Update failed (see logs).";
                    return;
                }
                SaveWindow();
                S.Save();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                Program.Restart(false);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        int reloadVersion;

        /// <summary>Rescans thcrap on a background thread, then rebuilds the cards.</summary>
        void Reload(Action after = null)
        {
            int version = ++reloadVersion;
            string saved = S.ThcrapPath, root = Paths.Root;
            var hidden = new HashSet<string>(S.HiddenLangs, StringComparer.OrdinalIgnoreCase);
            var onlyFor = S.LangGames.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value), StringComparer.OrdinalIgnoreCase);
            Task.Run(() =>
            {
                var thc = Library.Find(saved, root);
                return Library.Build(thc, root, hidden, onlyFor);
            }).ContinueWith(t =>
            {
                if (version != reloadVersion) return;
                if (t.IsFaulted) { Log.Error("Reload", t.Exception); Lib = new LibraryResult(); }
                else Lib = t.Result;
                RebuildCards();
                if (Lib.Thcrap == null) StatusText.Text = "thcrap not found - set its folder in Settings";
                else StatusText.Text = Fmt.Plural(Lib.Games.Count, "game", "games") + "  ·  " + Fmt.Plural(Lib.ConfigCount, "language", "languages");
                if (View == "settings") BuildSettingsPage();
                if (after != null) after();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------------------------------------------------------------- notices
        Action noticeAction;
        readonly Queue<Action> queuedNotices = new Queue<Action>();
        void ShowNotice(string text, string action, Action onAction)
        {
            if (NoticeBar.Visibility == Visibility.Visible && NoticeText.Text != text)
            {
                queuedNotices.Enqueue(() => ShowNotice(text, action, onAction));
                return;
            }
            NoticeText.Text = text;
            noticeAction = onAction;
            NoticeAction.Visibility = action != null ? Visibility.Visible : Visibility.Collapsed;
            NoticeAction.Content = action;
            NoticeBar.Visibility = Visibility.Visible;
        }

        // ---------------------------------------------------------------- small UI helpers
        static readonly Dictionary<string, SolidColorBrush> brushCache = new Dictionary<string, SolidColorBrush>();
        public static SolidColorBrush Br(string hex)
        {
            SolidColorBrush b;
            if (brushCache.TryGetValue(hex, out b)) return b;
            b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            brushCache[hex] = b;
            return b;
        }
        public static Color Col(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }
        public static string Hex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }
        static Thickness Th(double l, double t, double r, double b) { return new Thickness(l, t, r, b); }

        public static TextBlock TB(string text, double size, string hex, FontWeight? weight = null)
        {
            return new TextBlock { Text = text ?? "", FontSize = size, Foreground = Br(hex), FontWeight = weight ?? FontWeights.Normal };
        }

        Button FlatBtn(string text, Action click)
        {
            var b = new Button { Content = text, Style = (Style)W.FindResource("FlatBtn"), Margin = Th(8, 0, 0, 0), Padding = Th(14, 7, 14, 7), VerticalAlignment = VerticalAlignment.Center };
            b.Click += (s, e) => Safe("button", click);
            return b;
        }

        /// <summary>Runs UI code; any error is logged and shown without taking the launcher down.</summary>
        void Safe(string where, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Error(where, ex); ShowError("Something went wrong (" + where + "):\n" + ex.Message + "\n\nDetails are in the logs folder."); }
        }

        void ShowError(string msg) { MessageBox.Show(W, msg, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error); }
        bool Confirm(string msg, string title) { return MessageBox.Show(W, msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }

        static string Ask(string prompt, string title, string value)
        {
            return Microsoft.VisualBasic.Interaction.InputBox(prompt, title, value ?? "");
        }

        static void OpenFolder(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            else MessageBox.Show("That folder doesn't exist yet.", AppInfo.Name);
        }

        // ---------------------------------------------------------------- themes
        static readonly string[] ThemeKeys = { "purple", "crimson", "blue", "teal", "gold", "pink" };
        static readonly Dictionary<string, string> ThemeHex = new Dictionary<string, string>
        {
            { "purple", "#8B5CF6" }, { "crimson", "#E0435A" }, { "blue", "#3D8BFD" }, { "teal", "#14B8A6" }, { "gold", "#E5A50A" }, { "pink", "#EC4899" },
        };
        static readonly Dictionary<string, string> ThemeLabel = new Dictionary<string, string>
        {
            { "purple", "Purple" }, { "crimson", "Crimson" }, { "blue", "Blue" }, { "teal", "Teal" }, { "gold", "Gold" }, { "pink", "Pink" }, { "game", "Per game" },
        };

        string accentHex = "", accentLightHex = "#B79CFF", accentDarkHex = "#6D4FD0", baseLightHex = "#B79CFF";
        Color accent = Col("#8B5CF6"), accentLight = Col("#B79CFF");
        GradientStop bgTop, bgMid;

        void BuildBackground()
        {
            bgTop = new GradientStop(Col("#2E2758"), 0);
            bgMid = new GradientStop(Col("#1A1A31"), 0.42);
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.55, 1) };
            b.GradientStops.Add(bgTop); b.GradientStops.Add(bgMid); b.GradientStops.Add(new GradientStop(Col("#0D0D18"), 1));
            W.Background = b;
        }

        static LinearGradientBrush FadeLine(Color c, byte alpha)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0.5));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }

        void ApplyAccent(string hex, bool force = false)
        {
            var c = Col(hex);
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (lum < 115) c = Mix(c, Colors.White, Math.Min(0.45, (115 - lum) / 255 * 1.3));   // keep dark game colours readable
            var newHex = Hex(c);
            if (!force && newHex == accentHex) return;
            var light = Mix(c, Colors.White, 0.45);
            accent = c; accentLight = light;
            accentHex = newHex; accentLightHex = Hex(light); accentDarkHex = Hex(Mix(c, Colors.Black, 0.25));
            W.Resources["Accent"] = new SolidColorBrush(c);
            W.Resources["AccentLight"] = new SolidColorBrush(light);
            AnimateColor(bgTop, Mix(Col("#14131F"), c, 0.25));
            AnimateColor(bgMid, Mix(Col("#16162B"), c, 0.06));
            HeaderBar.BorderBrush = FadeLine(c, 112);
            BottomBar.BorderBrush = FadeLine(c, 80);
            DlgFooter.BorderBrush = FadeLine(c, 96);
            var sl = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            sl.GradientStops.Add(new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 0));
            sl.GradientStops.Add(new GradientStop(Color.FromArgb(255, light.R, light.G, light.B), 0.15));
            sl.GradientStops.Add(new GradientStop(Color.FromArgb(128, light.R, light.G, light.B), 0.7));
            sl.GradientStops.Add(new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 1));
            SearchLine.Fill = sl;
            SearchBox.CaretBrush = new SolidColorBrush(light);
            BottomHint.Foreground = new SolidColorBrush(light);
            UpdateViewButtons();
            UpdateFilterChips();
        }

        static void AnimateColor(GradientStop stop, Color to)
        {
            stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(450))) { EasingFunction = new QuadraticEase() });
        }

        static string GameAccent(Game g) { return g != null && g.InCatalog ? Catalog.ColorHex(g.Id) : ThemeHex["purple"]; }

        void InitTheme()
        {
            if (S.Theme == "game") { baseLightHex = "#B79CFF"; ApplyAccent(GameAccent(continueGame), true); }
            else
            {
                string hex;
                if (!ThemeHex.TryGetValue(S.Theme ?? "", out hex)) hex = ThemeHex["purple"];
                ApplyAccent(hex, true);
                baseLightHex = accentLightHex;
            }
        }

        void FollowGameTheme(Game g) { if (S.Theme == "game" && g != null) ApplyAccent(GameAccent(g)); }

        void SetTheme(string name)
        {
            S.Theme = name; S.Save();
            InitTheme();
            RebuildCards();
            if (View == "settings") BuildSettingsPage();
        }

        // ---------------------------------------------------------------- views
        void ShowView(string v, bool quiet = false)
        {
            if (!quiet && v != View) Sounds.Play(v == "library" ? "back" : "select");
            View = v;
            if (v == "library") { StatsScroll.Visibility = Visibility.Collapsed; MainScroll.Visibility = Visibility.Visible; }
            else
            {
                SetSelection(null);
                if (v == "stats") BuildStatsPage(); else if (v == "settings") BuildSettingsPage(); else BuildHelpPage();
                MainScroll.Visibility = Visibility.Collapsed;
                StatsScroll.Visibility = Visibility.Visible;
                StatsScroll.ScrollToTop();
            }
            UpdateViewButtons();
        }

        void UpdateViewButtons()
        {
            if (string.IsNullOrEmpty(accentHex) || HomeBtn == null) return;
            HomeBtn.Background = Br(View == "library" ? accentHex : "#272A44");
            StatsBtn.Background = Br(View == "stats" ? accentHex : "#272A44");
            SettingsBtn.Background = Br(View == "settings" ? accentHex : "#272A44");
            HelpBtn.Background = Br(View == "help" ? accentHex : "#272A44");
        }

        // ---------------------------------------------------------------- smooth scrolling
        sealed class SmoothTarget { public ScrollViewer SV; public bool H; public double Target; }
        readonly Dictionary<string, SmoothTarget> smooth = new Dictionary<string, SmoothTarget>();
        readonly Stopwatch smoothClock = Stopwatch.StartNew();
        double smoothLast;
        bool smoothOn;

        void ScrollTo(ScrollViewer sv, bool h, double target)
        {
            if (sv == null) return;
            double max = h ? sv.ScrollableWidth : sv.ScrollableHeight;
            target = Math.Max(0, Math.Min(max, target));
            smooth[sv.GetHashCode() + "|" + h] = new SmoothTarget { SV = sv, H = h, Target = target };
            if (!smoothOn) { smoothOn = true; smoothLast = smoothClock.Elapsed.TotalSeconds; CompositionTarget.Rendering += StepSmooth; }
        }

        void ScrollBy(ScrollViewer sv, bool h, double delta)
        {
            if (sv == null) return;
            SmoothTarget cur;
            double baseVal = smooth.TryGetValue(sv.GetHashCode() + "|" + h, out cur) ? cur.Target : (h ? sv.HorizontalOffset : sv.VerticalOffset);
            ScrollTo(sv, h, baseVal + delta);
        }

        void StepSmooth(object sender, EventArgs e)
        {
            double now = smoothClock.Elapsed.TotalSeconds, dt = Math.Min(0.05, now - smoothLast);
            smoothLast = now;
            double f = 1 - Math.Exp(-dt * 13);   // frame-rate independent ease-out
            foreach (var k in smooth.Keys.ToList())
            {
                var a = smooth[k];
                double cur = a.H ? a.SV.HorizontalOffset : a.SV.VerticalOffset, d = a.Target - cur;
                double next = Math.Abs(d) < 0.5 ? a.Target : cur + d * f;
                if (a.H) a.SV.ScrollToHorizontalOffset(next); else a.SV.ScrollToVerticalOffset(next);
                if (Math.Abs(d) < 0.5) smooth.Remove(k);
            }
            if (smooth.Count == 0) { CompositionTarget.Rendering -= StepSmooth; smoothOn = false; }
        }

        // ---------------------------------------------------------------- wiring
        void WireUp()
        {
            NoticeClose.Click += (s, e) =>
            {
                NoticeBar.Visibility = Visibility.Collapsed;
                if (queuedNotices.Count > 0) queuedNotices.Dequeue()();
            };
            NoticeAction.Click += (s, e) => { if (noticeAction != null) Safe("notice", noticeAction); };
            SearchBox.TextChanged += (s, e) => { ApplyFilter(); UpdateContinue(); };
            SearchBox.GotKeyboardFocus += (s, e) => SearchLine.Opacity = 1;
            SearchBox.LostKeyboardFocus += (s, e) => SearchLine.Opacity = 0.45;
            SortBtn.Click += (s, e) => ShowSortMenu();
            BuildFilterChips();
            foreach (var b in new[] { HomeBtn, StatsBtn, SettingsBtn, HelpBtn })
            {
                var btn = b;
                btn.MouseEnter += (s, e) => BottomHint.Text = Convert.ToString(btn.Tag);
                btn.MouseLeave += (s, e) => BottomHint.Text = "";
                btn.GotKeyboardFocus += (s, e) => BottomHint.Text = Convert.ToString(btn.Tag);
                btn.LostKeyboardFocus += (s, e) => BottomHint.Text = "";
            }
            HomeBtn.Click += (s, e) => ShowView("library");
            StatsBtn.Click += (s, e) => ShowView("stats");
            SettingsBtn.Click += (s, e) => ShowView("settings");
            HelpBtn.Click += (s, e) => ShowView("help");

            DlgClose.Click += (s, e) => HidePicker();
            DlgFav.Click += (s, e) => ToggleFav(current);
            DlgPlay.Click += (s, e) => Safe("play", () => { var last = LastLang(current); if (last != null) LaunchLang(last); });
            CustomChk.Checked += (s, e) => UpdatePickerMode();
            CustomChk.Unchecked += (s, e) => UpdatePickerMode();
            VanillaBtn.Click += (s, e) => Safe("play original", LaunchOriginal);
            CoverBtn.Click += (s, e) => Safe("covers", ShowCoverGallery);
            ReplaysBtn.Click += (s, e) => Safe("replays", ShowReplays);
            SavesBtn.Click += (s, e) => Safe("saves", ShowSavesMenu);
            Overlay.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == Overlay) HidePicker(); };
            W.SizeChanged += (s, e) => { if (Overlay.Visibility == Visibility.Visible) FitDialog(); };

            W.PreviewMouseWheel += OnWheel;
            W.PreviewKeyDown += (s, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (Nav(key.ToString(), ctrl)) e.Handled = true;
            };
            W.Closing += (s, e) =>
            {
                if (!reallyClose && Track.Sessions.Count > 0) { e.Cancel = true; EnterTray(); }
            };
            W.Activated += (s, e) =>
            {
                if (!thcrapOpened) return;
                thcrapOpened = false;
                Reload(() => StatusText.Text += "  ·  refreshed after thcrap");
            };
            W.Closed += (s, e) =>
            {
                SaveWindow();
                S.Save();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                Application.Current.Shutdown();
            };

            trackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            trackTimer.Tick += (s, e) => Safe("tracking", TrackTick);
            coverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            coverTimer.Tick += (s, e) => CoverTick();
            if (!SafeMode)
            {
                padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                padTimer.Tick += (s, e) => { try { PollPad(); } catch (Exception ex) { Log.Error("pad", ex); padTimer.Stop(); } };
                padTimer.Start();
            }
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) != 0)
            {
                SetCardScale(S.CardScale + (e.Delta > 0 ? 0.1 : -0.1)); S.Save();
                if (View == "settings") BuildSettingsPage();
                e.Handled = true; return;
            }
            if (Overlay.Visibility == Visibility.Visible || SetupOverlay.Visibility == Visibility.Visible) return;
            if ((mods & ModifierKeys.Shift) != 0 && hoverShelf != null && MainScroll.IsVisible) ScrollBy(hoverShelf, true, -e.Delta * 2.2);
            else if (MainScroll.IsVisible) ScrollBy(MainScroll, false, -e.Delta * 1.3);
            else if (StatsScroll.IsVisible) ScrollBy(StatsScroll, false, -e.Delta * 1.3);
            else return;
            e.Handled = true;
        }

        void SetCardScale(double v)
        {
            v = Math.Round(Math.Max(0.6, Math.Min(1.6, v)), 2);
            cardScale.ScaleX = v; cardScale.ScaleY = v;
            S.CardScale = v;
        }

        // ---------------------------------------------------------------- keyboard + controller navigation
        static readonly string[] Views = { "library", "stats", "settings", "help" };

        /// <summary>Shared by keyboard and controller. Returns true when the key was used.</summary>
        bool Nav(string k, bool ctrl)
        {
            if (SetupOverlay.Visibility == Visibility.Visible) return false;   // the setup screen uses normal focus

            if (k == "PadLB" || k == "PadRB" || k == "PadStart" || k == "PadBack")
            {
                if (Overlay.Visibility == Visibility.Visible) return true;
                int i = Array.IndexOf(Views, View);
                if (k == "PadLB") ShowView(Views[(i + 3) % 4]);
                else if (k == "PadRB") ShowView(Views[(i + 1) % 4]);
                else if (k == "PadStart") ShowView(View == "settings" ? "library" : "settings");
                else ShowView(View == "help" ? "library" : "help");
                return true;
            }

            if (Overlay.Visibility == Visibility.Visible) return NavPicker(k);

            if (View != "library")
            {
                bool onSlider = Keyboard.FocusedElement is Slider;
                if (k == "Escape") { ShowView("library"); return true; }
                if (k == "Up" && !onSlider) { ScrollBy(StatsScroll, false, -160); return true; }
                if (k == "Down" && !onSlider) { ScrollBy(StatsScroll, false, 160); return true; }
                return false;
            }

            if ((ctrl && k == "F") || k == "OemQuestion") { SearchBox.Focus(); SearchBox.SelectAll(); return true; }
            if (SearchBox.IsKeyboardFocused)
            {
                if (k == "Down" || k == "Return")
                {
                    var rows = CardRows();
                    if (rows.Count > 0) { GamePanel.Focus(); SetSelection(rows[0][0]); }
                    return true;
                }
                if (k == "Escape") { if (SearchBox.Text.Length > 0) SearchBox.Text = ""; else GamePanel.Focus(); return true; }
                return false;
            }

            var fe = Keyboard.FocusedElement;
            if (fe != null && (fe == playBtn || fe == otherBtn))
            {
                switch (k)
                {
                    case "Left": playBtn.Focus(); return true;
                    case "Right": otherBtn.Focus(); return true;
                    case "Up": return true;
                    case "Down": GamePanel.Focus(); MoveSelection("Down"); return true;
                    case "Return": ((Button)fe).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); return true;
                }
            }
            switch (k)
            {
                case "Left": case "Right": case "Up": case "Down": GamePanel.Focus(); MoveSelection(k); return true;
                case "Return": if (selCard != null) OpenCard((Game)selCard.Tag); else MoveSelection("Right"); return true;
                case "F": if (selCard != null) { ToggleFav((Game)selCard.Tag); return true; } return false;
                case "PadX": if (selCard != null && !((Game)selCard.Tag).Unowned) PlayLast((Game)selCard.Tag); return true;
                case "Escape": if (SearchBox.Text.Length > 0) SearchBox.Text = ""; else SetSelection(null); return true;
            }
            return false;
        }

        bool NavPicker(string k)
        {
            if (pickerMode != "langs")
            {
                if (k == "Escape")
                {
                    ShowLangMode(); Sounds.Play("back");
                    W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SetLangSel(langSel, true)));
                    return true;
                }
                return false;
            }
            var fe = Keyboard.FocusedElement;
            int n = langButtons.Count;
            switch (k)
            {
                case "Escape": HidePicker(); return true;
                case "Left": SetLangSel(langSel - 1); return true;
                case "Right": SetLangSel(langSel + 1); return true;
                case "Up": if (langSel - langCols >= 0) SetLangSel(langSel - langCols); return true;
                case "Down":
                    if (langSel + langCols < n) SetLangSel(langSel + langCols); else if (langSel < n - 1) SetLangSel(n - 1);
                    return true;
                case "Return":
                    var cb = fe as CheckBox;
                    var bb = fe as ButtonBase;
                    if (cb != null) cb.IsChecked = !(cb.IsChecked ?? false);
                    else if (bb != null && !langButtons.Contains(bb as Button)) bb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    else if (langSel >= 0 && langSel < n) Safe("play", () => LaunchLang((string)langButtons[langSel].Tag));
                    return true;
                case "F": ToggleFav(current); return true;
                case "PadX": var last = LastLang(current); if (last != null) Safe("play", () => LaunchLang(last)); return true;
            }
            return false;
        }

        // Controller: polled only while the window is active, so the game never gets doubled input.
        int padIndex = -1, padPrev, padScan;
        string padDir;
        DateTime padDirSince, padDirLast;
        static readonly int[] PadMasks = { Pad.A, Pad.B, Pad.X, Pad.Y, Pad.LB, Pad.RB, Pad.Start, Pad.Back };
        static readonly string[] PadKeys = { "Return", "Escape", "PadX", "F", "PadLB", "PadRB", "PadStart", "PadBack" };

        void PollPad()
        {
            if (!W.IsActive || !W.IsVisible) { padPrev = 0; padDir = null; return; }
            int b, lx, ly;
            if (padIndex < 0)
            {
                if (padScan-- > 0) return;
                padScan = 30;   // look for a controller about once a second
                for (int i = 0; i < 4; i++) if (Pad.Read(i, out b, out lx, out ly)) { padIndex = i; padPrev = b; break; }
                return;
            }
            if (!Pad.Read(padIndex, out b, out lx, out ly)) { padIndex = -1; return; }
            int pressed = b & ~padPrev;
            padPrev = b;
            for (int i = 0; i < PadMasks.Length; i++) if ((pressed & PadMasks[i]) != 0) Nav(PadKeys[i], false);

            string dir = null;
            if ((b & Pad.Up) != 0) dir = "Up"; else if ((b & Pad.Down) != 0) dir = "Down";
            else if ((b & Pad.Left) != 0) dir = "Left"; else if ((b & Pad.Right) != 0) dir = "Right";
            else if (Math.Abs(lx) > 16000 || Math.Abs(ly) > 16000)
                dir = Math.Abs(lx) > Math.Abs(ly) ? (lx > 0 ? "Right" : "Left") : (ly > 0 ? "Up" : "Down");
            var now = DateTime.Now;
            if (dir != padDir)
            {
                padDir = dir;
                if (dir != null) { padDirSince = now; padDirLast = now; Nav(dir, false); }
            }
            else if (dir != null && (now - padDirSince).TotalMilliseconds > 380 && (now - padDirLast).TotalMilliseconds > 110)
            {
                padDirLast = now; Nav(dir, false);   // held: auto-repeat
            }
        }

        // ---------------------------------------------------------------- tray
        void EnterTray()
        {
            if (tray == null)
            {
                tray = new WinForms.NotifyIcon { Text = AppInfo.Name };
                try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); }
                catch { tray.Icon = System.Drawing.SystemIcons.Application; }
                tray.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) W.Dispatcher.BeginInvoke(new Action(ExitTray)); };
                var menu = new WinForms.ContextMenuStrip();
                menu.Items.Add("Show " + AppInfo.Name, null, (s, e) => W.Dispatcher.BeginInvoke(new Action(ExitTray)));
                menu.Items.Add("Quit (saves play time so far)", null, (s, e) => W.Dispatcher.BeginInvoke(new Action(QuitApp)));
                tray.ContextMenuStrip = menu;
            }
            exitWhenIdle = true;
            SaveWindow();
            tray.Visible = true;
            W.Hide();
            UpdateNowPlaying();
        }

        public void ExitTray()
        {
            exitWhenIdle = false;
            if (tray != null) tray.Visible = false;
            W.Show();
            if (W.WindowState == WindowState.Minimized) W.WindowState = WindowState.Normal;
            W.Activate();
        }

        void QuitApp()
        {
            trackTimer.Stop();
            foreach (var s in Track.Sessions.Where(x => x.Detected).ToList())
            {
                Log.Track(s.Game.Id + ": launcher quit, saving time so far");
                double sec = s.Elapsed.TotalSeconds;
                Stats.Update(s.Game.Id, sec, 0);
                SessionLog.Add(s.Game.Id, s.Start, sec);
            }
            Track.Sessions.Clear();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            reallyClose = true;
            W.Close();
        }
    }
}
