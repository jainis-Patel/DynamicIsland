using System.Collections.Concurrent;
using System.IO;

namespace DynamicIsland.Core;

/// <summary>
/// Resolves UAC-safe, writable persistence paths under %LocalAppData%\DynamicIsland
/// with automatic migration from <see cref="AppDomain.CurrentDomain.BaseDirectory"/> and
/// transparent fallback for portable or sandboxed environments.
/// </summary>
internal static class AppPaths
{
    private static readonly ConcurrentDictionary<string, string> CachedDataPaths = new(StringComparer.OrdinalIgnoreCase);

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile => GetDataFilePath("settings.json");
    public static string ShelfFile => GetDataFilePath("shelf.json");
    public static string ClipboardFile => GetDataFilePath("clipboard.json");
    public static string NotesFile => GetDataFilePath("notes.json");
    public static string TasksFile => GetDataFilePath("tasks.json");
    public static string EventsFile => GetDataFilePath("events.json");
    public static string LauncherFile => GetDataFilePath("launcher.json");
    public static string TermiNotchFile => GetDataFilePath("commands.json");
    public static string LogFile => GetDataFilePath("app.log");
    public static string LogArchiveFile => GetDataFilePath("app.1.log");

    public static string DropletsDirectory
    {
        get
        {
            string dir = Path.Combine(DataDirectory, "Droplets");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }
    }

    public static string GetDataFilePath(string fileName)
    {
        return CachedDataPaths.GetOrAdd(fileName, static name =>
        {
            string targetPath = Path.Combine(DataDirectory, name);
            if (!File.Exists(targetPath))
            {
                try
                {
                    string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                    if (!string.Equals(targetPath, legacyPath, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(legacyPath))
                    {
                        File.Copy(legacyPath, targetPath, overwrite: false);
                    }
                }
                catch
                {
                    // Ignore migration errors
                }
            }
            return targetPath;
        });
    }

    private static string ResolveDataDirectory()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string dir = Path.Combine(localAppData, "DynamicIsland");
                Directory.CreateDirectory(dir);

                // Verify write access (handles sandboxed or restricted environments gracefully)
                string probe = Path.Combine(dir, $".write_probe_{Guid.NewGuid():N}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
        }
        catch
        {
            // Fall back to application base directory (portable / sandboxed mode)
        }

        return AppDomain.CurrentDomain.BaseDirectory;
    }
}
