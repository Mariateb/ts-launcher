// TS Launcher - thprac, screenshots, backup folder / import, Steam play-time import (interface side).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TSLauncher
{
    public sealed partial class LauncherWindow
    {
        // ---------------------------------------------------------------- thprac
        bool UsesThprac(Game g) { return g != null && !SafeMode && Thprac.Supports(g.Id) && S.ThpracGames.Contains(g.Id); }

        /// <summary>thprac.exe: the saved one, else found near TS Launcher / thcrap / the games (and remembered).</summary>
        string ThpracExe()
        {
            var p = Thprac.Find(S.ThpracPath, Lib.Thcrap, Lib.Games);
            if (p != null && p != S.ThpracPath) { S.ThpracPath = p; S.Save(); }
            return p;
        }

        /// <summary>Asks where thprac is (or opens its download page). True when thprac is ready.</summary>
        bool LocateThprac()
        {
            var r = MessageBox.Show(W, "thprac (the Touhou practice tool) wasn't found next to TS Launcher, thcrap or your games.\n\nYes: show me where thprac.exe is\nNo: open the thprac download page",
                "thprac", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.No) { try { Process.Start(Thprac.Page); } catch { } return false; }
            if (r != MessageBoxResult.Yes) return false;
            using (var dlg = new WinForms.OpenFileDialog { Title = "Where is thprac.exe?", Filter = "thprac|thprac*.exe|Programs|*.exe" })
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return false;
                S.ThpracPath = dlg.FileName; S.Save();
                return true;
            }
        }

        void ToggleThprac()
        {
            var g = current;
            if (g == null) return;
            bool on = ThpracChk.IsChecked ?? false;
            if (on && ThpracExe() == null && !LocateThprac()) { ThpracChk.IsChecked = false; return; }
            if (on) S.ThpracGames.Add(g.Id); else S.ThpracGames.Remove(g.Id);
            S.Save();
            Sounds.Play("toggle");
            UpdatePickerMode();
            UpdateCard(g);
        }

        /// <summary>Called when a game's process is found: attach thprac a moment later (lets the game create its window).</summary>
        void AttachThpracLater(Session s)
        {
            if (!s.Thprac || s.ThpracDone || s.Pid == 0) return;
            s.ThpracDone = true;
            int pid = s.Pid;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (o, e) =>
            {
                t.Stop();
                if (!Procs.IsAlive(pid)) return;
                var err = Thprac.Attach(ThpracExe(), pid);
                if (err != null) ShowNotice("thprac couldn't be started for " + s.Game.Title + ": " + err, "thprac settings", () => ShowView("settings"));
                else StatusText.Text = "thprac attached to " + s.Game.Title + ".";
            };
            t.Start();
        }

        void ThpracSettings()
        {
            Section("thprac");
            var exe = ThpracExe();
            SettingRow("thprac.exe", exe ?? "Not found. Put it next to TS Launcher or thcrap, or show where it is.",
                FlatBtn("Locate…", () =>
                {
                    using (var dlg = new WinForms.OpenFileDialog { Title = "Where is thprac.exe?", Filter = "thprac|thprac*.exe|Programs|*.exe" })
                        if (dlg.ShowDialog() == WinForms.DialogResult.OK) { S.ThpracPath = dlg.FileName; S.Save(); BuildSettingsPage(); }
                }),
                FlatBtn("Download", () => { try { Process.Start(Thprac.Page); } catch { } }));
            var on = S.ThpracGames.Where(Thprac.Supports).OrderBy(Catalog.Number).Select(Catalog.Short).ToList();
            SettingRow("Games using thprac", (on.Count == 0 ? "None yet. " : string.Join(", ", on) + ". ") +
                "Turn it on per game with the thprac button in the game's window: the game starts as usual (thcrap, your language) and thprac attaches itself a moment later.");
        }

        // ---------------------------------------------------------------- screenshots
        void ShowScreenshots()
        {
            pickerMode = "shots";
            LangScroll.Visibility = Visibility.Collapsed; DlgFooter.Visibility = Visibility.Collapsed; CoverScroll.Visibility = Visibility.Visible;
            BuildScreenshots();
            CoverScroll.ScrollToTop();
            Sounds.Play("select");
        }

        static readonly string[] ShotExts = { ".png", ".bmp", ".jpg", ".jpeg" };

        void BuildScreenshots()
        {
            var g = current;
            var dir = Saves.SubFolder(g, "snapshot");
            CoverPanel.Children.Clear();
            var buttons = new List<Button>();
            if (dir != null) buttons.Add(FlatBtn("Open folder", () => OpenFolder(dir)));
            CoverPanel.Children.Add(PanelTopBar(buttons.ToArray()));

            var files = dir == null ? new List<FileInfo>() : new DirectoryInfo(dir).GetFiles()
                .Where(f => ShotExts.Contains(f.Extension.ToLowerInvariant())).OrderByDescending(f => f.LastWriteTime).ToList();
            var h = TB("SCREENSHOTS   ·   " + files.Count + (files.Count > 120 ? "  (newest 120 shown)" : ""), 12, "#8E8CA6", FontWeights.SemiBold);
            h.Margin = Th(0, 4, 0, 8);
            CoverPanel.Children.Add(h);
            if (files.Count == 0)
            {
                var n = TB("No screenshots yet. Press the game's screenshot key while playing (Home on most games) and they appear here.", 13, "#8E8CA6");
                n.TextWrapping = TextWrapping.Wrap;
                CoverPanel.Children.Add(n);
                return;
            }
            var wrap = new WrapPanel();
            int w = (int)Math.Ceiling(196 * 1.5 * DpiScale());
            foreach (var f in files.Take(120))
            {
                var file = f;
                var outer = new StackPanel { Width = 196, Margin = Th(0, 0, 10, 12) };
                var b = new Border { Width = 196, Height = 147, CornerRadius = new CornerRadius(2), BorderThickness = Th(2, 2, 2, 2), BorderBrush = Br("#22FFFFFF"), Background = Br("#222440"), Cursor = Cursors.Hand, ClipToBounds = true, ToolTip = file.Name + "\nClick: open  ·  Right-click: copy, show in folder, delete" };
                var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
                b.Child = img;
                ImageCache.Load(W.Dispatcher, file.FullName, w, false, bmp => { img.Source = bmp; img.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(160)))); }, false);
                b.MouseEnter += (s, e) => b.BorderBrush = Br(accentHex);
                b.MouseLeave += (s, e) => b.BorderBrush = Br("#22FFFFFF");
                b.MouseLeftButtonUp += (s, e) => Safe("screenshot", () => Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true }));
                var menu = new ContextMenu();
                Action<string, Action> item = (header, act) => { var mi = new MenuItem { Header = header }; mi.Click += (s, e) => Safe("screenshot", act); menu.Items.Add(mi); };
                item("Open", () => Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true }));
                item("Copy image", () =>
                {
                    Clipboard.SetImage(ImageCache.DecodeBytes(File.ReadAllBytes(file.FullName), 0));
                    StatusText.Text = file.Name + " copied: paste it anywhere.";
                    Sounds.Play("toggle");
                });
                item("Show in folder", () => Process.Start("explorer.exe", "/select,\"" + file.FullName + "\""));
                menu.Items.Add(new Separator());
                item("Delete…", () =>
                {
                    if (!Confirm("Move " + file.Name + " to the Recycle Bin?", "Delete screenshot")) return;
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file.FullName, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    BuildScreenshots();
                });
                b.ContextMenu = menu;
                outer.Children.Add(b);
                var cap = TB(file.LastWriteTime.ToString("d MMM yyyy, HH:mm", Fmt.Inv), 11, "#8E8CA6"); cap.Margin = Th(2, 4, 0, 0);
                outer.Children.Add(cap);
                wrap.Children.Add(outer);
            }
            CoverPanel.Children.Add(wrap);
        }

        // ---------------------------------------------------------------- backup folder
        static string PickFolder(string description, string start)
        {
            using (var dlg = new WinForms.FolderBrowserDialog { Description = description, ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dlg.SelectedPath = start;
                return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        void BackupSettings()
        {
            bool custom = !string.IsNullOrEmpty(Paths.CustomBackups);
            var change = FlatBtn("Change…", () => ChangeBackupFolder(null));
            var reset = FlatBtn("Use default", () => ChangeBackupFolder(Paths.DefaultBackups));
            reset.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            change.IsEnabled = reset.IsEnabled = !SafeMode;
            SettingRow("Backup folder", Paths.Backups + (custom ? "" : "  (default, next to TSLauncher.exe)") +
                ". Choose a folder in OneDrive, Google Drive or Dropbox to keep your save and replay backups on every PC.",
                change, reset, FlatBtn("Open", () => { Directory.CreateDirectory(Paths.Backups); OpenFolder(Paths.Backups); }));
            var import = FlatBtn("Import…", ImportBackups); import.IsEnabled = !SafeMode;
            SettingRow("Import backups", "From another PC or a copy you kept: brings in save and replay backups, can restore the newest save of each game, and can bring back your play time.", import);
            var steam = FlatBtn("Import…", ImportSteamTime); steam.IsEnabled = !SafeMode && Lib.Games.Any(x => x.Source == "Steam");
            SettingRow("Steam play time", "Use the hours Steam recorded for your Steam copies (whichever is higher is kept, so nothing is counted twice).", steam);
        }

        /// <summary>target null = ask. Copies the current backups to the new folder, then switches to it.</summary>
        void ChangeBackupFolder(string target)
        {
            if (target == null) target = PickFolder("Choose where TS Launcher keeps its backups (for example a folder in OneDrive).", Paths.Backups);
            if (target == null) return;
            target = Path.GetFullPath(target).TrimEnd('\\');
            var old = Paths.Backups;
            if (string.Equals(target, old, StringComparison.OrdinalIgnoreCase)) return;
            if (target.StartsWith(old + "\\", StringComparison.OrdinalIgnoreCase)) { ShowError("Choose a folder outside the current backup folder."); return; }
            try { Directory.CreateDirectory(target); var probe = Path.Combine(target, ".tslauncher-write-test"); File.WriteAllText(probe, "ok"); File.Delete(probe); }
            catch (Exception ex) { ShowError("TS Launcher can't write in that folder:\n" + ex.Message); return; }

            bool hasOld = Directory.Exists(old) && Directory.EnumerateFileSystemEntries(old).Any();
            var answer = hasOld ? MessageBox.Show(W, "Copy your existing backups to\n" + target + "?\n\n(Recommended. The old copies stay where they are until you delete them.)",
                "Backup folder", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) : MessageBoxResult.No;
            if (answer == MessageBoxResult.Cancel) return;
            W.Cursor = Cursors.Wait;
            StatusText.Text = "Copying backups…";
            Task.Run(() => answer == MessageBoxResult.Yes ? BackupTools.Merge(old, target) : 0).ContinueWith(t =>
            {
                W.Cursor = null;
                if (t.IsFaulted) { ShowError("Couldn't copy the backups:\n" + t.Exception.GetBaseException().Message + "\n\nNothing was changed."); return; }
                bool isDefault = string.Equals(target, Paths.DefaultBackups, StringComparison.OrdinalIgnoreCase);
                S.BackupDir = isDefault ? null : target;
                Paths.CustomBackups = S.BackupDir;
                S.Save();
                StatusText.Text = answer == MessageBoxResult.Yes ? "Backups copied (" + t.Result + " files). New backups now go to " + target + "." : "New backups now go to " + target + ".";
                Log.App("Backup folder changed to " + target);
                if (View == "settings") BuildSettingsPage();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ImportBackups()
        {
            var chosen = PickFolder("Choose a TS Launcher backups folder (from another PC, a USB stick, OneDrive…).", null);
            if (chosen == null) return;
            var root = BackupTools.FindRoot(chosen);
            if (root == null) { ShowError("No TS Launcher backups were found in that folder.\n\nChoose the \"backups\" folder itself, or the folder that contains it."); return; }
            bool same = string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Path.GetFullPath(Paths.Backups).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            W.Cursor = Cursors.Wait;
            StatusText.Text = "Importing backups…";
            Task.Run(() => same ? 0 : BackupTools.Merge(root, Paths.Backups)).ContinueWith(t =>
            {
                W.Cursor = null;
                if (t.IsFaulted) { ShowError("Couldn't import the backups:\n" + t.Exception.GetBaseException().Message); return; }
                StatusText.Text = same ? "That's already your backup folder." : "Imported " + t.Result + " backup files.";

                var snap = BackupTools.NewestLauncherSnapshot(root);
                if (snap != null && Confirm("These backups also contain your play time and session history (from " + Path.GetFileName(snap) + ").\n\nBring them back? For each game the higher play time is kept, so nothing is lost or counted twice.", "Import play time"))
                {
                    try
                    {
                        Stats.RaiseTo(StatsStore.Parse(File.ReadAllText(Path.Combine(snap, "playtime.json"))));
                        int added = SessionLog.MergeFrom(Path.Combine(snap, "sessions.csv"));
                        StatusText.Text += "  ·  play time imported" + (added > 0 ? " (" + Fmt.Plural(added, "session", "sessions") + ")" : "");
                        RebuildCards();
                    }
                    catch (Exception ex) { ShowError("Couldn't read the play time from that backup:\n" + ex.Message); }
                }

                var withBackups = Lib.Games.Where(g => Saves.List(g).Count > 0).ToList();
                if (withBackups.Count > 0 && Confirm("Restore the newest save of each game now? (" + string.Join(", ", withBackups.Select(g => Catalog.Short(g.Id))) + ")\n\nEach current save is backed up first, so this can be undone from the game's Saves menu.", "Restore saves"))
                {
                    int restored = 0;
                    var skipped = new List<string>();
                    foreach (var g in withBackups)
                    {
                        if (Track.Sessions.Any(x => x.Game.Id == g.Id)) { skipped.Add(Catalog.Short(g.Id) + " (running)"); continue; }
                        var newest = Saves.List(g).First().FullName;   // pick before backing up the current save
                        Saves.Backup(g);
                        try { if (Saves.Restore(newest, g) > 0) restored++; }
                        catch (Exception ex) { skipped.Add(Catalog.Short(g.Id) + " (" + ex.Message + ")"); }
                    }
                    MessageBox.Show(W, "Restored " + Fmt.Plural(restored, "game", "games") + "." + (skipped.Count > 0 ? "\n\nSkipped: " + string.Join(", ", skipped) : ""), AppInfo.Name);
                }
                if (View == "settings") BuildSettingsPage();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------------------------------------------------------------- Steam play time
        void ImportSteamTime()
        {
            W.Cursor = Cursors.Wait;
            var games = Lib.Games.ToList();
            string problem = null;
            Task.Run(() => SteamPlaytime.Read(games, out problem)).ContinueWith(t =>
            {
                W.Cursor = null;
                if (t.IsFaulted) { ShowError("Couldn't read Steam's play time:\n" + t.Exception.GetBaseException().Message); return; }
                var list = t.Result;
                if (list.Count == 0) { MessageBox.Show(W, problem ?? "Steam has no play time recorded for your Touhou games.", "Steam play time", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                var lines = new List<string>();
                var incoming = new Dictionary<string, GameStat>();
                foreach (var st in list)
                {
                    var mine = Stats.Get(st.Game.Id);
                    double have = mine == null ? 0 : mine.Seconds;
                    bool raise = st.Seconds > have;
                    lines.Add("TH" + st.Game.NumText + "  " + st.Game.Title + ":  Steam " + Fmt.Duration(st.Seconds) + "  ·  here " + Fmt.Duration(have) + (raise ? "  →  " + Fmt.Duration(st.Seconds) : "  (kept)"));
                    if (raise) incoming[st.Game.Id] = new GameStat { Seconds = st.Seconds, Launches = 0, LastPlayed = st.LastPlayed == null ? null : Fmt.Iso(st.LastPlayed.Value) };
                }
                if (incoming.Count == 0) { MessageBox.Show(W, "Your play time here is already at least as high as Steam's:\n\n" + string.Join("\n", lines), "Steam play time"); return; }
                if (!Confirm("Steam recorded this play time:\n\n" + string.Join("\n", lines) + "\n\nUse Steam's time where it's higher?", "Steam play time")) return;
                Stats.RaiseTo(incoming);
                RebuildCards();
                StatusText.Text = "Steam play time imported for " + Fmt.Plural(incoming.Count, "game", "games") + ".";
                if (View == "stats") BuildStatsPage();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
