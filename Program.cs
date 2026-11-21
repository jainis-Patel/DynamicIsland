using System.Runtime.InteropServices;
using System.Text;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--diagnose-memory", StringComparison.OrdinalIgnoreCase)))
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            Console.WriteLine($"[Process] WorkingSet={p.WorkingSet64 / 1024 / 1024} MB, PrivateBytes={p.PrivateMemorySize64 / 1024 / 1024} MB, PagedMemory={p.PagedMemorySize64 / 1024 / 1024} MB");
            Console.WriteLine($"[GC] IsServerGC={System.Runtime.GCSettings.IsServerGC}, LatencyMode={System.Runtime.GCSettings.LatencyMode}, ConserveMemory={AppContext.GetData("System.GC.ConserveMemory")}");
            Console.WriteLine($"[GC Heap] HeapSize={gc.HeapSizeBytes / 1024 / 1024} MB, Committed={gc.TotalCommittedBytes / 1024 / 1024} MB, TotalManaged={GC.GetTotalMemory(false) / 1024 / 1024} MB");
            for (int i = 0; i < gc.GenerationInfo.Length; i++)
            {
                var gen = gc.GenerationInfo[i];
                Console.WriteLine($"  Gen {i}: Size={gen.SizeAfterBytes / 1024 / 1024} MB, Frag={gen.FragmentationAfterBytes / 1024 / 1024} MB");
            }
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--diagnose-window", StringComparison.OrdinalIgnoreCase)))
        {
            App.IsDiagnosticMode = true;
            var app = new App();
            app.InitializeComponent();
            var win = new IslandWindow();
            win.Loaded += async (_, _) =>
            {
                await Task.Delay(4000);
                Core.IslandMemoryManager.ExecuteConservativeTrim();
                var p = System.Diagnostics.Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                Console.WriteLine($"[Window Loaded Process] WorkingSet={p.WorkingSet64 / 1024 / 1024} MB, PrivateBytes={p.PrivateMemorySize64 / 1024 / 1024} MB, PagedMemory={p.PagedMemorySize64 / 1024 / 1024} MB");
                Console.WriteLine($"[Window Loaded GC] HeapSize={gc.HeapSizeBytes / 1024 / 1024} MB, Committed={gc.TotalCommittedBytes / 1024 / 1024} MB, TotalManaged={GC.GetTotalMemory(false) / 1024 / 1024} MB");
                Environment.Exit(0);
            };
            app.Run(win);
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--diagnose-software", StringComparison.OrdinalIgnoreCase)))
        {
            App.IsDiagnosticMode = true;
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new App();
            app.InitializeComponent();
            var win = new IslandWindow();
            win.Loaded += async (_, _) =>
            {
                await Task.Delay(4000);
                var p = System.Diagnostics.Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                Console.WriteLine($"[Software Render Process] WorkingSet={p.WorkingSet64 / 1024 / 1024} MB, PrivateBytes={p.PrivateMemorySize64 / 1024 / 1024} MB, PagedMemory={p.PagedMemorySize64 / 1024 / 1024} MB");
                Console.WriteLine($"[Software Render GC] HeapSize={gc.HeapSizeBytes / 1024 / 1024} MB, Committed={gc.TotalCommittedBytes / 1024 / 1024} MB, TotalManaged={GC.GetTotalMemory(false) / 1024 / 1024} MB");
                Console.WriteLine("--- Graphics Modules ---");
                foreach (System.Diagnostics.ProcessModule mod in p.Modules)
                {
                    if (mod.ModuleName.Contains("nv") || mod.ModuleName.Contains("AMD") || mod.ModuleName.Contains("d3d") || mod.ModuleName.Contains("Skia"))
                    {
                        Console.WriteLine($"  {mod.ModuleName}: {mod.ModuleMemorySize / 1024 / 1024} MB");
                    }
                }
                Environment.Exit(0);
            };
            app.Run(win);
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return Core.SelfTestRunner.RunAll();
        }

        if (args.Any(a => string.Equals(a, "--render-golden-glass", StringComparison.OrdinalIgnoreCase)))
        {
            return LiquidGlass.GoldenImageGenerator.GenerateGoldenImages();
        }

        if (args.Any(a => string.Equals(a, "--playground", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "--glass-playground", StringComparison.OrdinalIgnoreCase)))
        {
            var app = new App();
            app.InitializeComponent();
            var playground = new Views.GlassPlayground();
            app.Run(playground);
            return 0;
        }

        if (args.Length > 0 && (string.Equals(args[0], "activity", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(args[0], "--activity", StringComparison.OrdinalIgnoreCase)))
        {
            return Services.LiveActivityIpcService.RunCliAsync(args).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            SignalStopOnDefaultDesktop();
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--replace", StringComparison.OrdinalIgnoreCase)))
        {
            SignalStopOnDefaultDesktop();
            Thread.Sleep(300);
        }

        if (args.Any(a => string.Equals(a, "--render-installer-previews", StringComparison.OrdinalIgnoreCase)))
        {
            string? specifiedDir = args.Skip(1).FirstOrDefault(a => !a.StartsWith("-"));
            string outDir = specifiedDir ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer_previews");
            var app = new App();
            Views.SetupWindow.RenderPreviews(outDir);
            Console.WriteLine($"[OK] Installer previews rendered to: {outDir}");
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--updated", StringComparison.OrdinalIgnoreCase)))
        {
            App.IsPostUpdateLaunch = true;
            Core.UpdateHandoffEngine.CleanupOldBackups();
        }

        // Check for Silent or GUI Uninstall Mode
        if (Core.InstallerService.IsUninstallMode(args))
        {
            if (Core.InstallerService.IsSilent(args))
            {
                Core.InstallerService.Uninstall(removeUserData: false, out _);
                return 0;
            }

            App.StartupMode = InstallerStartupMode.Uninstall;
            RunApp();
            return 0;
        }

        // Check for Silent or GUI Setup/Install Mode
        if (Core.InstallerService.IsInstallerMode(args))
        {
            if (Core.InstallerService.IsSilent(args))
            {
                if (Core.InstallerService.TryParseWaitPid(args, out int waitPid))
                {
                    try
                    {
                        using var parentProc = System.Diagnostics.Process.GetProcessById(waitPid);
                        if (!parentProc.HasExited)
                        {
                            parentProc.WaitForExit(15000);
                        }
                    }
                    catch { }
                }

                Core.InstallerService.TryParseTargetDir(args, out string? customTargetDir);
                bool installed = Core.InstallerService.Install(
                    targetDir: customTargetDir,
                    createDesktopShortcut: true,
                    createStartMenuShortcut: true,
                    autoStartOnBoot: true,
                    out _);

                if (installed && Core.InstallerService.ShouldRelaunchAfterSilentInstall(args))
                {
                    string resolvedDir = !string.IsNullOrWhiteSpace(customTargetDir)
                        ? customTargetDir
                        : (Core.InstallerService.GetExistingInstallation().Location ?? Core.InstallerService.GetDefaultInstallDir());
                    string installedExe = Core.InstallerService.GetInstalledExePath(resolvedDir);
                    if (System.IO.File.Exists(installedExe))
                    {
                        try
                        {
                            using var launched = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installedExe, "--updated")
                            {
                                WorkingDirectory = resolvedDir,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[Installer] Failed to relaunch updated binary: {ex.Message}");
                        }
                    }
                }
                return installed ? 0 : 1;
            }

            App.StartupMode = InstallerStartupMode.Setup;
            RunApp();
            return 0;
        }

        App.EnsurePowerSavingGpuPreference();
        App.Log("Program.Main started.");

        var currentDesktopName = GetCurrentDesktopName();
        App.Log($"Current process desktop: {currentDesktopName}");

        // If already running on the interactive "Default" desktop, avoid spawning an extra thread.
        if (string.Equals(currentDesktopName, "Default", StringComparison.OrdinalIgnoreCase))
        {
            RunApp();
            return 0;
        }

        IntPtr hDefault = OpenDesktop("Default", 0, false, DESKTOP_ALL);
        if (hDefault != IntPtr.Zero)
        {
            try
            {
                App.Log("OpenDesktop(Default) handle acquired.");

                Exception? threadEx = null;
                var uiThread = new Thread(() =>
                {
                    try
                    {
                        if (SetThreadDesktop(hDefault))
                        {
                            App.Log($"UI thread attached to desktop: {GetCurrentDesktopName()}");
                        }
                        else
                        {
                            App.Log($"SetThreadDesktop failed with error {Marshal.GetLastWin32Error()}");
                        }

                        RunApp();
                    }
                    catch (Exception ex)
                    {
                        threadEx = ex;
                        App.Log($"Fatal exception on UI thread: {ex}");
                    }
                });

                uiThread.SetApartmentState(ApartmentState.STA);
                uiThread.Start();
                uiThread.Join();

                if (threadEx != null) throw threadEx;
                return 0;
            }
            finally
            {
                CloseDesktop(hDefault);
            }
        }

        App.Log($"OpenDesktop(Default) failed (error {Marshal.GetLastWin32Error()}). Running on current desktop directly.");
        RunApp();
        return 0;
    }

    private static string GetCurrentDesktopName()
    {
        var sb = new StringBuilder(256);
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), UOI_NAME, sb, sb.Capacity, out _);
        return sb.ToString();
    }

    private static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    public static void SignalStopOnDefaultDesktop()
    {
        CloseDynamicIslandWindow();

        IntPtr hDefault = OpenDesktop("Default", 0, false, DESKTOP_ALL);
        if (hDefault != IntPtr.Zero)
        {
            var t = new Thread(() =>
            {
                try
                {
                    if (SetThreadDesktop(hDefault))
                    {
                        CloseDynamicIslandWindow();
                    }
                }
                finally
                {
                    CloseDesktop(hDefault);
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(2000);
        }
    }

    private static void CloseDynamicIslandWindow()
    {
        uint quitMsg = RegisterWindowMessage("DYNAMIC_ISLAND_QUIT");
        if (quitMsg != 0)
        {
            PostMessage(HWND_BROADCAST, quitMsg, IntPtr.Zero, IntPtr.Zero);
        }

        for (int i = 0; i < 5; i++)
        {
            IntPtr hwnd = FindWindow(null, "Dynamic Island");
            if (hwnd == IntPtr.Zero) hwnd = FindWindow(null, "DynamicIsland");
            if (hwnd == IntPtr.Zero) break;
            if (!IsDynamicIslandProcessWindow(hwnd)) break;
            if (quitMsg != 0) PostMessage(hwnd, quitMsg, IntPtr.Zero, IntPtr.Zero);
            PostMessage(hwnd, WM_HOTKEY, new IntPtr(3), IntPtr.Zero);
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(100);
        }
    }

    private static bool IsDynamicIslandProcessWindow(IntPtr hwnd)
    {
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return false;
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return string.Equals(proc.ProcessName, "DynamicIsland", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
