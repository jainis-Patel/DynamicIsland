using System.IO;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.Core;

/// <summary>
/// Provides crash-safe atomic JSON file persistence by writing to a temporary sibling file
/// and atomically replacing the target file via <see cref="File.Move(string,string,bool)"/>.
/// Prevents corrupt/truncated JSON files on unexpected power loss or concurrent writes.
/// </summary>
internal static class AtomicJsonStore
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true
    };

    public static T Load<T>(string filePath, Func<T> fallbackFactory, JsonSerializerOptions? options = null)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var loaded = JsonSerializer.Deserialize<T>(json, options ?? DefaultOptions);
                    if (loaded is not null)
                    {
                        return loaded;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[AtomicJsonStore] Failed to load '{Path.GetFileName(filePath)}': {ex.Message}");
        }

        return fallbackFactory();
    }

    public static bool Save<T>(string filePath, T data, object? syncRoot = null, JsonSerializerOptions? options = null)
    {
        if (syncRoot is not null)
        {
            lock (syncRoot)
            {
                return SaveCore(filePath, data, options);
            }
        }

        return SaveCore(filePath, data, options);
    }

    private static bool SaveCore<T>(string filePath, T data, JsonSerializerOptions? options)
    {
        string tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(data, options ?? DefaultOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tmpPath, filePath, overwrite: true);
                    break;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 3)
                {
                    Thread.Sleep(15);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[AtomicJsonStore] Failed to save '{Path.GetFileName(filePath)}': {ex.Message}");
            try
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
            }
            catch
            {
                // Ignore temp cleanup failure
            }
            return false;
        }
    }
}
