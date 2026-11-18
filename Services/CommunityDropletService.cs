using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Community Droplet Plugin SDK engine:
///  - Discovers and schema-validates JSON droplet definitions from %LocalAppData%\DynamicIsland\Droplets
///  - Hot-reloads in real-time on filesystem changes (create, edit, delete) with zero app restart
///  - Polls external HTTP endpoints asynchronously and publishes Live Activities with custom action buttons
///  - Zero-Idle CPU: unhooks polling loops when droplets are disabled or idle
/// </summary>
public sealed class CommunityDropletService : IDisposable
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(8)
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly DropletRegistry _registry;
    private readonly LiveActivityHub _hub;
    private readonly ConcurrentDictionary<string, CommunityDropletDefinition> _loadedDroplets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DispatcherTimer> _pollTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounceTimer;
    private bool _disposed;

    public IReadOnlyCollection<CommunityDropletDefinition> LoadedDroplets => _loadedDroplets.Values.ToList();

    public CommunityDropletService(DropletRegistry registry, LiveActivityHub hub)
    {
        _registry = registry;
        _hub = hub;
    }

    public void Start()
    {
        string dir = AppPaths.DropletsDirectory;
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }

        // Initial discovery scan
        ScanAndLoadAll(dir);

        // Setup FileSystemWatcher for hot reloading
        try
        {
            _watcher = new FileSystemWatcher(dir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = true
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemDeleted;
            _watcher.Renamed += OnFileSystemRenamed;
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] FileSystemWatcher initialization failed: {ex.Message}");
        }
    }

    public int ScanAndLoadAll(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        int count = 0;

        try
        {
            var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (Path.GetFileName(file).StartsWith('.')) continue;
                if (LoadDropletFromFile(file, notifyUser: false))
                {
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] Scan error: {ex.Message}");
        }

        return count;
    }

    public bool LoadDropletFromJson(string json, string sourceFilePath = "", bool notifyUser = true)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            var def = CommunityDropletSchemaValidator.Deserialize(json);
            if (!CommunityDropletSchemaValidator.Validate(def, out var errors))
            {
                App.Log($"[CommunityDropletService] Validation failed for '{sourceFilePath}': {string.Join("; ", errors)}");
                return false;
            }

            RegisterCommunityDroplet(def!, notifyUser);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] Failed parsing droplet JSON '{sourceFilePath}': {ex.Message}");
            return false;
        }
    }

    public bool LoadDropletFromFile(string filePath, bool notifyUser = true)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            string json = File.ReadAllText(filePath);
            return LoadDropletFromJson(json, filePath, notifyUser);
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] Could not read file '{filePath}': {ex.Message}");
            return false;
        }
    }

    private void RegisterCommunityDroplet(CommunityDropletDefinition def, bool notifyUser)
    {
        _loadedDroplets[def.Id] = def;

        Color accent = ThemeBrushes.BlueColor;
        if (!string.IsNullOrWhiteSpace(def.AccentColor))
        {
            try
            {
                var converted = ColorConverter.ConvertFromString(def.AccentColor);
                if (converted is Color c) accent = c;
            }
            catch { }
        }

        var actions = new List<LiveActivityAction>();
        if (def.Actions != null)
        {
            foreach (var a in def.Actions)
            {
                var capturedAction = a;
                actions.Add(new LiveActivityAction(
                    Label: capturedAction.Label,
                    Glyph: string.IsNullOrWhiteSpace(capturedAction.Glyph) ? "\uE71D" : capturedAction.Glyph,
                    Callback: () => ExecuteAction(capturedAction)
                ));
            }
        }

        Enum.TryParse<DropletCategory>(def.Category, ignoreCase: true, out var cat);

        var descriptor = new DropletDescriptor
        {
            Id = def.Id,
            Name = def.Name,
            Description = def.Description,
            Glyph = string.IsNullOrWhiteSpace(def.Glyph) ? "\uE7B5" : def.Glyph,
            Category = cat,
            EstimatedRamKb = def.EstimatedRamKb,
            IsDefaultEnabled = true,
            OnActivate = () => OnDropletActivated(def),
            OnDeactivate = () => OnDropletDeactivated(def.Id)
        };

        _registry.RegisterOrUpdate(descriptor);

        // If enabled, start polling loop
        if (_registry.IsEnabled(def.Id))
        {
            OnDropletActivated(def);
        }

        if (notifyUser)
        {
            TactileFeedback.PlaySnap();
            IslandWindow.Instance?.Dispatcher.InvokeAsync(() =>
            {
                IslandWindow.Instance.PushNotificationToast(
                    def.Glyph,
                    "Droplet Installed",
                    $"{def.Name} by {def.Author}",
                    accent);
            });
        }
    }

    public bool UnregisterCommunityDroplet(string id, bool notifyUser = true)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        string cleanId = id.Trim();

        _loadedDroplets.TryRemove(cleanId, out var def);
        StopDropletPolling(cleanId);
        _hub.Complete($"community_{cleanId}");

        bool removed = _registry.Unregister(cleanId);
        if (removed && notifyUser && def != null)
        {
            TactileFeedback.PlayDismiss();
            IslandWindow.Instance?.Dispatcher.InvokeAsync(() =>
            {
                IslandWindow.Instance.PushNotificationToast(
                    "\uE74D",
                    "Droplet Uninstalled",
                    def.Name,
                    ThemeBrushes.RedColor);
            });
        }

        return removed;
    }

    private void OnDropletActivated(CommunityDropletDefinition def)
    {
        PublishDropletActivity(def, def.CustomUi?.BadgeText ?? def.Name, def.Description);

        if (!string.IsNullOrWhiteSpace(def.Endpoint) && def.RefreshIntervalSec >= 5)
        {
            StartDropletPolling(def);
        }
    }

    private void OnDropletDeactivated(string id)
    {
        StopDropletPolling(id);
        _hub.Complete($"community_{id}");
    }

    private void StartDropletPolling(CommunityDropletDefinition def)
    {
        StopDropletPolling(def.Id);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(def.RefreshIntervalSec, 5, 86400))
        };

        timer.Tick += async (_, _) =>
        {
            await PollEndpointAsync(def);
        };

        _pollTimers[def.Id] = timer;
        timer.Start();

        // Immediate background poll
        Task.Run(() => PollEndpointAsync(def));
    }

    private void StopDropletPolling(string id)
    {
        if (_pollTimers.TryRemove(id, out var timer))
        {
            timer.Stop();
        }
    }

    public async Task<bool> PollEndpointAsync(CommunityDropletDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Endpoint)) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, def.Endpoint);
            if (def.Headers != null)
            {
                foreach (var (k, v) in def.Headers)
                {
                    req.Headers.TryAddWithoutValidation(k, v);
                }
            }

            using var resp = await HttpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                App.Log($"[CommunityDropletService] HTTP {resp.StatusCode} for droplet '{def.Id}'");
                return false;
            }

            string content = await resp.Content.ReadAsStringAsync();
            string extractedValue = ExtractJsonValue(content, def.ResponsePath);

            string formatted = string.IsNullOrWhiteSpace(def.FormatTemplate)
                ? extractedValue
                : def.FormatTemplate.Replace("{value}", extractedValue, StringComparison.OrdinalIgnoreCase);

            PublishDropletActivity(def, formatted, def.Description);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] Poll failed for droplet '{def.Id}': {ex.Message}");
            return false;
        }
    }

    public static string ExtractJsonValue(string json, string? responsePath)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        if (string.IsNullOrWhiteSpace(responsePath)) return json.Trim();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;
            var parts = responsePath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var child))
                {
                    current = child;
                }
                else
                {
                    return json.Trim();
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? "",
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => current.GetRawText()
            };
        }
        catch
        {
            return json.Trim();
        }
    }

    private void PublishDropletActivity(CommunityDropletDefinition def, string title, string subtitle)
    {
        Color accent = ThemeBrushes.BlueColor;
        if (!string.IsNullOrWhiteSpace(def.AccentColor))
        {
            try
            {
                var parsed = ColorConverter.ConvertFromString(def.AccentColor);
                if (parsed is Color c) accent = c;
            }
            catch { }
        }

        var actions = new List<LiveActivityAction>();
        if (def.Actions != null)
        {
            foreach (var a in def.Actions)
            {
                var captured = a;
                actions.Add(new LiveActivityAction(
                    Label: captured.Label,
                    Glyph: string.IsNullOrWhiteSpace(captured.Glyph) ? "\uE71D" : captured.Glyph,
                    Callback: () => ExecuteAction(captured)
                ));
            }
        }

        var item = new LiveActivityItem
        {
            Id = $"community_{def.Id}",
            Source = def.Name,
            Title = title,
            Subtitle = subtitle,
            Glyph = string.IsNullOrWhiteSpace(def.Glyph) ? "\uE7B5" : def.Glyph,
            AccentColor = accent,
            Progress = null,
            Priority = 60,
            TargetViewKey = null,
            Actions = actions
        };

        _hub.Publish(item);
    }

    public void ExecuteAction(DropletActionDefinition action)
    {
        if (string.IsNullOrWhiteSpace(action.Command)) return;
        TactileFeedback.PlaySnap();

        try
        {
            string type = action.Type?.Trim() ?? "OpenUrl";

            if (string.Equals(type, "OpenUrl", StringComparison.OrdinalIgnoreCase) ||
                action.Command.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                action.Command.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = action.Command,
                    UseShellExecute = true
                });
            }
            else if (string.Equals(type, "ShellExecute", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {action.Command}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else if (string.Equals(type, "HttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                var method = string.Equals(action.Method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? HttpMethod.Post
                    : HttpMethod.Get;

                Task.Run(async () =>
                {
                    try
                    {
                        using var req = new HttpRequestMessage(method, action.Command);
                        await HttpClient.SendAsync(req);
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] Action failed: {ex.Message}");
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        Debounce(() =>
        {
            LoadDropletFromFile(e.FullPath, notifyUser: true);
        });
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        Debounce(() =>
        {
            string id = Path.GetFileNameWithoutExtension(e.Name ?? "");
            UnregisterCommunityDroplet(id, notifyUser: true);
        });
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        Debounce(() =>
        {
            string oldId = Path.GetFileNameWithoutExtension(e.OldName ?? "");
            UnregisterCommunityDroplet(oldId, notifyUser: false);
            LoadDropletFromFile(e.FullPath, notifyUser: true);
        });
    }

    private void Debounce(Action action)
    {
        lock (_lock)
        {
            _debounceTimer?.Stop();
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                _debounceTimer = null;
                action();
            };
            _debounceTimer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        foreach (var timer in _pollTimers.Values)
        {
            timer.Stop();
        }
        _pollTimers.Clear();

        _loadedDroplets.Clear();
    }
}
