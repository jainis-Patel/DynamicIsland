using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>
/// Extracts, freezes, and caches shell application icons from executables, shortcuts, and folders.
/// Shared across Core, Services, and Views without UI-control coupling.
/// </summary>
internal static class AppIconLoader
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const int MaxCacheEntries = 512;

    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? GetAppIcon(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        lock (IconCache)
        {
            if (IconCache.TryGetValue(targetPath, out var cached))
                return cached;
        }

        ImageSource? result = null;
        try
        {
            string? resolved = ResolveExePath(targetPath);
            if (!string.IsNullOrEmpty(resolved) && (File.Exists(resolved) || Directory.Exists(resolved)))
            {
                var shinfo = new SHFILEINFO();
                IntPtr hSuccess = SHGetFileInfo(resolved, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                if (hSuccess != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        result = bmp;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
        }
        catch
        {
            result = null;
        }

        lock (IconCache)
        {
            if (IconCache.Count >= MaxCacheEntries)
                IconCache.Clear();
            IconCache[targetPath] = result;
        }

        return result;
    }

    public static string? ResolveExePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (File.Exists(path) || Directory.Exists(path)) return path;

            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded) || Directory.Exists(expanded)) return expanded;

            string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cand = Path.Combine(system32, path);
            if (File.Exists(cand)) return cand;
            if (!cand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cand + ".exe"))
                return cand + ".exe";

            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            cand = Path.Combine(windir, path);
            if (File.Exists(cand)) return cand;
            if (!cand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cand + ".exe"))
                return cand + ".exe";

            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    try
                    {
                        cand = Path.Combine(dir, path);
                        if (File.Exists(cand)) return cand;
                        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            cand = Path.Combine(dir, path + ".exe");
                            if (File.Exists(cand)) return cand;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }
}
