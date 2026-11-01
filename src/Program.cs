// TS Launcher - entry point, single instance, crash protection and safe mode.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

[assembly: AssemblyTitle("TS Launcher")]
[assembly: AssemblyProduct("TS Launcher")]
[assembly: AssemblyDescription("Game and language picker for thcrap")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace TSLauncher
{
    public static class Program
    {
        static Mutex instance;
        static EventWaitHandle showSignal;
        static LauncherWindow window;
        static bool safeMode;
        static int crashing;

        [STAThread]
        public static int Main(string[] args)
        {
            safeMode = args.Any(a => a.Equals("--safe", StringComparison.OrdinalIgnoreCase));
            string exe = Assembly.GetExecutingAssembly().Location;
            Paths.Init(Path.GetDirectoryName(Path.GetFullPath(exe)));

            AppDomain.CurrentDomain.UnhandledException += (s, e) => CrashReporter.Report(e.ExceptionObject as Exception, true, safeMode);
            TaskScheduler.UnobservedTaskException += (s, e) => { Log.Error("background task", e.Exception); e.SetObserved(); };
            WinForms.Application.EnableVisualStyles();

            // One launcher per folder: a second start just brings the first one back (e.g. from the tray).
            string id = "TSLauncher-" + Paths.Root.ToLowerInvariant().GetHashCode().ToString("X8");
            bool created;
            instance = new Mutex(true, id, out created);
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, id + "-show");
            if (!created && !args.Contains("--restarted"))
            {
                showSignal.Set();
                return 0;
            }
            if (!created) { try { instance.WaitOne(5000); } catch (AbandonedMutexException) { } }

            // If the last start never finished, offer safe mode instead of crashing again.
            if (!safeMode && File.Exists(Paths.StartupMarker))
            {
                var r = WinForms.MessageBox.Show("TS Launcher didn't start properly last time.\n\nStart in safe mode? (Your settings stay untouched; covers, sounds and the controller are off.)",
                    "TS Launcher", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning);
                if (r == WinForms.DialogResult.Yes) safeMode = true;
            }
            try { File.WriteAllText(Paths.StartupMarker, DateTime.Now.ToString("o")); } catch { }
            Log.App("Starting " + AppInfo.Name + " " + AppInfo.Version + (safeMode ? " (safe mode)" : "") + " in " + Paths.Root);

            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    e.Handled = true;
                    if (CrashReporter.Report(e.Exception, false, safeMode) == CrashReporter.Choice.Quit) app.Shutdown();
                };
                // Background work (library scan, covers) reports back through this before app.Run() starts.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                window = new LauncherWindow(safeMode);
                window.Start();
                new Thread(() =>
                {
                    while (true)
                    {
                        showSignal.WaitOne();
                        try { window.W.Dispatcher.BeginInvoke(new Action(() => window.ExitTray())); } catch { return; }
                    }
                }) { IsBackground = true, Name = "show-signal" }.Start();
                app.Run();
            }
            catch (Exception ex)
            {
                CrashReporter.Report(ex, true, safeMode);
                return 1;
            }
            finally
            {
                try { instance.ReleaseMutex(); } catch { }
            }
            return 0;
        }

        /// <summary>Called once the window has rendered: the start went fine.</summary>
        public static void StartupSucceeded()
        {
            Task.Delay(3000).ContinueWith(t => { try { File.Delete(Paths.StartupMarker); } catch { } });
        }

        public static void Restart(bool safe)
        {
            try { File.Delete(Paths.StartupMarker); } catch { }
            try { instance.ReleaseMutex(); } catch { }
            Process.Start(Assembly.GetExecutingAssembly().Location, "--restarted" + (safe ? " --safe" : ""));
            Environment.Exit(0);
        }

        public static bool BeginCrash() { return Interlocked.Exchange(ref crashing, 1) == 0; }
        public static void EndCrash() { Interlocked.Exchange(ref crashing, 0); }
    }

    /// <summary>Shows a readable error, writes a report to logs\, and offers safe mode.</summary>
    public static class CrashReporter
    {
        public enum Choice { KeepGoing, Quit }

        public static Choice Report(Exception ex, bool fatal, bool inSafeMode)
        {
            if (ex == null) ex = new Exception("Unknown error");
            if (!Program.BeginCrash()) return Choice.KeepGoing;
            string file = null;
            try
            {
                Directory.CreateDirectory(Paths.Logs);
                file = Path.Combine(Paths.Logs, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(file,
                    AppInfo.Name + " " + AppInfo.Version + (inSafeMode ? " (safe mode)" : "") + "\r\n" +
                    DateTime.Now.ToString("o") + "\r\n" + Environment.OSVersion + "  ·  .NET " + Environment.Version + "\r\n" +
                    (fatal ? "Fatal" : "Recovered") + " error:\r\n\r\n" + ex + "\r\n\r\n--- last log lines ---\r\n" + Log.Tail(Paths.AppLog, 40));
                Log.Error(fatal ? "FATAL" : "unhandled", ex);
            }
            catch { }

            try
            {
                using (var f = new CrashForm(ex, fatal, file, inSafeMode))
                {
                    f.ShowDialog();
                    if (f.Result == "safe") Program.Restart(true);
                    if (f.Result == "restart") Program.Restart(false);
                    if (fatal || f.Result == "quit") { Program.EndCrash(); return Choice.Quit; }
                }
            }
            catch { }
            Program.EndCrash();
            return Choice.KeepGoing;
        }
    }

    sealed class CrashForm : WinForms.Form
    {
        public string Result = "quit";

        public CrashForm(Exception ex, bool fatal, string reportFile, bool inSafeMode)
        {
            Text = "TS Launcher";
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; TopMost = true;
            BackColor = Color.FromArgb(25, 26, 46); ForeColor = Color.FromArgb(240, 238, 248);
            Font = new Font("Segoe UI", 10f);
            ClientSize = new System.Drawing.Size(560, 330);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            var title = new WinForms.Label { Text = fatal ? "TS Launcher ran into a problem and has to close" : "Something went wrong", Font = new Font("Segoe UI Semibold", 14f), AutoSize = false, Bounds = new Rectangle(24, 18, 512, 32) };
            var body = new WinForms.Label
            {
                Text = (fatal ? "Your play time and settings are safe. " : "You can usually just keep going. ") + "A report was saved in the logs folder" +
                       (inSafeMode ? "." : "; if this keeps happening, restart in safe mode (settings untouched, covers, sounds and the controller off)."),
                AutoSize = false, Bounds = new Rectangle(24, 56, 512, 48), ForeColor = Color.FromArgb(210, 208, 230),
            };
            var details = new WinForms.TextBox
            {
                Text = ex.GetType().Name + ": " + ex.Message, Multiline = true, ReadOnly = true, ScrollBars = WinForms.ScrollBars.Vertical,
                Bounds = new Rectangle(24, 112, 512, 140), BackColor = Color.FromArgb(39, 42, 68), ForeColor = Color.FromArgb(240, 238, 248), BorderStyle = WinForms.BorderStyle.None,
            };
            Controls.Add(title); Controls.Add(body); Controls.Add(details);

            int x = 536;
            Func<string, string, WinForms.Button> add = (text, result) =>
            {
                var b = new WinForms.Button { Text = text, AutoSize = true, FlatStyle = WinForms.FlatStyle.Flat, BackColor = Color.FromArgb(39, 42, 68), ForeColor = Color.White, Padding = new WinForms.Padding(8, 2, 8, 2) };
                b.FlatAppearance.BorderColor = Color.FromArgb(56, 60, 94);
                b.Click += (s, e) => { if (result == "report") { if (reportFile != null) Process.Start("notepad.exe", "\"" + reportFile + "\""); return; } Result = result; Close(); };
                Controls.Add(b);
                b.PerformLayout();
                x -= b.PreferredSize.Width + 10;
                b.Location = new System.Drawing.Point(x + 10, 272);
                return b;
            };
            var main = fatal ? add("Close", "quit") : add("Keep going", "continue");
            if (!inSafeMode) add("Restart in safe mode", "safe"); else add("Restart normally", "restart");
            if (reportFile != null) add("Open report", "report");
            AcceptButton = main;
        }
    }
}
