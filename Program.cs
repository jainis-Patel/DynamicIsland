using System.Runtime.InteropServices;
using System.Text;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return Core.SelfTestRunner.RunAll();
        }

        if (args.Any(a => string.Equals(a, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            SignalStopOnDefaultDesktop();
            return 0;
        }

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
            if (quitMsg != 0) PostMessage(hwnd, quitMsg, IntPtr.Zero, IntPtr.Zero);
            PostMessage(hwnd, WM_HOTKEY, new IntPtr(3), IntPtr.Zero);
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(100);
        }
    }
}
