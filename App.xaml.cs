using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using DynamicIsland.Core;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public enum InstallerStartupMode
{
    Normal,
    Setup,
    Uninstall
}

public partial class App : Application
{
    public static InstallerStartupMode StartupMode { get; set; } = InstallerStartupMode.Normal;
    public static bool IsDiagnosticMode { get; set; } = false;
    public static bool IsPostUpdateLaunch { get; set; } = false;

    private static readonly string LogFile = AppPaths.LogFile;
    private const long MaxLogSizeBytes = 512 * 1024; // Cap log file at 512 KB
    private static readonly object LogWriteLock = new();
    private static readonly ConcurrentDictionary<string, long> DebugLogLastTicks = new(StringComparer.Ordinal);
    private const long DebugLogCooldownMs = 60_000;
    private static readonly ConcurrentQueue<string> RecentLogs = new();
    private const int MaxRecentLogs = 200;

    private static readonly Channel<string> LogChannel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

    private Mutex? _singleInstance;
    private bool _ownsMutex;

    static App()
    {
        _ = Task.Run(ProcessLogQueueAsync);
    }

    public static IReadOnlyList<string> GetRecentLogs() => RecentLogs.ToArray();

    /// <summary>
    /// Non-blocking asynchronous logger. Queues log entries in memory and flushes on a background worker
    /// so disk I/O never stalls the 60-240 Hz WPF UI thread.
    /// </summary>
    public static void Log(string msg)
    {
        string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        RecentLogs.Enqueue(entry);
        while (RecentLogs.Count > MaxRecentLogs && RecentLogs.TryDequeue(out _)) { }
        LogChannel.Writer.TryWrite(entry);
    }

    /// <summary>
    /// Rate-limited debug exception logger (60s cooldown per category + exception type)
    /// for expected or noisy runtime catches that should remain observable without flooding logs.
    /// </summary>
    public static void LogDebug(string category, Exception ex)
    {
        if (ex == null) return;
        string key = $"{category}:{ex.GetType().Name}";
        long now = Environment.TickCount64;
        if (DebugLogLastTicks.TryGetValue(key, out long last) && (now - last) < DebugLogCooldownMs)
        {
            return;
        }

        DebugLogLastTicks[key] = now;
        Log($"[Debug:{category}] {ex.GetType().Name}: {ex.Message}");
    }

    public static void LogMemorySnapshot(string tag)
    {
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            long totalMem = GC.GetTotalMemory(false);
            Log($"[Memory Snapshot: {tag}] WorkingSet={p.WorkingSet64 / 1024 / 1024} MB, PrivateBytes={p.PrivateMemorySize64 / 1024 / 1024} MB, GCHeap={gc.HeapSizeBytes / 1024 / 1024} MB, GCCommitted={gc.TotalCommittedBytes / 1024 / 1024} MB, TotalManaged={totalMem / 1024 / 1024} MB, IsServerGC={System.Runtime.GCSettings.IsServerGC}");
        }
        catch { }
    }

    private static bool _gpuPreferenceChecked;

    internal static void EnsurePowerSavingGpuPreference()
    {
        if (_gpuPreferenceChecked) return;
        _gpuPreferenceChecked = true;
        Choreographer.EnsureIntegratedGpuPreference();
    }

    public static void FlushLogs()
    {
        try
        {
            var batch = new StringBuilder(512);
            while (LogChannel.Reader.TryRead(out string? line))
            {
                batch.AppendLine(line);
            }
            if (batch.Length > 0)
            {
                lock (LogWriteLock)
                {
                    File.AppendAllText(LogFile, batch.ToString());
                }
            }
        }
        catch
        {
            // Ignore logging I/O failures
        }
    }

    private static async Task ProcessLogQueueAsync()
    {
        var batch = new StringBuilder(512);
        var reader = LogChannel.Reader;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            batch.Clear();
            while (reader.TryRead(out string? line))
            {
                batch.AppendLine(line);
                if (batch.Length >= 4096) break;
            }

            if (batch.Length > 0)
            {
                try
                {
                    lock (LogWriteLock)
                    {
                        var fi = new FileInfo(LogFile);
                        if (fi.Exists && fi.Length > MaxLogSizeBytes)
                        {
                            try
                            {
                                File.Move(LogFile, AppPaths.LogArchiveFile, overwrite: true);
                            }
                            catch
                            {
                                // Fallback if archive file is locked
                            }
                            File.WriteAllText(LogFile, batch.ToString());
                        }
                        else
                        {
                            File.AppendAllText(LogFile, batch.ToString());
                        }
                    }
                }
                catch
                {
                    // Ignore logging I/O failures
                }
            }
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("=== App OnStartup ===");
        if (StartupMode != InstallerStartupMode.Uninstall)
        {
            EnsurePowerSavingGpuPreference();
        }

        try
        {
            var settings = AtomicJsonStore.Load<Services.AppSettings?>(AppPaths.SettingsFile, () => null);
            if (settings?.LowMemoryMode == true)
            {
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                Log("[Rendering] Software-only rendering enabled (LowMemoryMode = true).");
            }

            ThemeManager.Initialize(settings?.ThemeMode, settings?.ThemeStyle);
            Log($"[Theme] Initialized theme mode: {ThemeManager.CurrentMode}, style: {ThemeManager.CurrentStyle} -> Active: {ThemeManager.CurrentTheme}");
        }
        catch (Exception ex)
        {
            ThemeManager.Initialize();
            Log($"[Theme] Notice during theme init: {ex.Message}");
        }

        LogMemorySnapshot("Pre-Startup");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"AppDomain UnhandledException: {args.ExceptionObject}");
            FlushLogs();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            FlushLogs();
            if (args.Exception is OutOfMemoryException or StackOverflowException or AccessViolationException)
            {
                return;
            }
            if (MainWindow == null || !MainWindow.IsVisible)
            {
                args.Handled = true;
                Shutdown(1);
                return;
            }
            args.Handled = true;
        };

        if (StartupMode == InstallerStartupMode.Setup)
        {
            Log("App started in Setup Wizard mode.");
            base.OnStartup(e);
            var setupWin = new Views.SetupWindow();
            MainWindow = setupWin;
            setupWin.Show();
            return;
        }

        if (StartupMode == InstallerStartupMode.Uninstall)
        {
            Log("App started in Uninstall mode.");
            base.OnStartup(e);
            var uninstallWin = new Views.UninstallWindow();
            MainWindow = uninstallWin;
            uninstallWin.Show();
            return;
        }

        if (!IsDiagnosticMode)
        {
            _singleInstance = new Mutex(true, @"Local\DynamicIsland.SingleInstance", out _ownsMutex);
            if (!_ownsMutex)
            {
                bool replace = e.Args.Any(a => string.Equals(a, "--replace", StringComparison.OrdinalIgnoreCase));
                if (replace)
                {
                    Log("Another instance is already running with --replace flag. Signaling existing instance to shut down.");
                    Program.SignalStopOnDefaultDesktop();
                    try
                    {
                        _ownsMutex = _singleInstance.WaitOne(TimeSpan.FromMilliseconds(2500));
                    }
                    catch (AbandonedMutexException)
                    {
                        _ownsMutex = true;
                    }
                }

                if (!_ownsMutex)
                {
                    Log("Another instance is already running. Signaling existing instance to expand and exiting.");
                    uint expandMsg = RegisterWindowMessage("DYNAMIC_ISLAND_EXPAND");
                    PostMessage(HWND_BROADCAST, expandMsg, IntPtr.Zero, IntPtr.Zero);
                    Shutdown();
                    return;
                }
            }
        }

        base.OnStartup(e);

        if (MainWindow == null)
        {
            try
            {
                Log("Creating IslandWindow...");
                var island = new IslandWindow();
                MainWindow = island;
                Log("Showing IslandWindow...");
                island.Show();
                Log("IslandWindow shown successfully.");
                LogMemorySnapshot("Post-Show");
            }
            catch (Exception ex)
            {
                Log($"Exception during window creation/show: {ex}");
                throw;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"=== App OnExit (code={e.ApplicationExitCode}) ===");
        LogChannel.Writer.TryComplete();
        FlushLogs();
        if (_ownsMutex)
        {
            try { _singleInstance?.ReleaseMutex(); } catch { }
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
