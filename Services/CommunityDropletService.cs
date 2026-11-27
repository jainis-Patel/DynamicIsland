using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
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
    private static readonly HttpClient HttpClient = SharedHttpClient.CreateClient(TimeSpan.FromSeconds(10));

    private const int MaxResponseBytes = 256 * 1024;

    private readonly DropletRegistry _registry;
    private readonly LiveActivityHub _hub;
    private readonly Dispatcher _uiDispatcher;
    private readonly ConcurrentDictionary<string, CommunityDropletDefinition> _loadedDroplets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _fileToDropletId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DispatcherTimer> _pollTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _pollInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly object _trustLock = new();
    private readonly HashSet<string> _trustedShellCommands = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private Choreographer? _choreographer;
    private volatile bool _disposed;

    public void AttachChoreographer(Choreographer choreographer)
    {
        if (_disposed || choreographer == null) return;
        _choreographer = choreographer;
        foreach (var def in _loadedDroplets.Values)
        {
            if (_registry.IsEnabled(def.Id) && !string.IsNullOrWhiteSpace(def.Endpoint))
            {
                StartDropletPolling(def);
            }
        }
    }

    public IReadOnlyCollection<CommunityDropletDefinition> LoadedDroplets => _loadedDroplets.Values.ToList();

    public CommunityDropletService(DropletRegistry registry, LiveActivityHub hub)
    {
        _registry = registry;
        _hub = hub;
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        LoadTrustedShellCommands();
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
        if (string.IsNullOrWhiteSpace(json) || _disposed) return false;

        try
        {
            var def = CommunityDropletSchemaValidator.Deserialize(json);
            if (!CommunityDropletSchemaValidator.Validate(def, out var errors))
            {
                App.Log($"[CommunityDropletService] Validation failed for '{sourceFilePath}': {string.Join("; ", errors)}");
                return false;
            }

            if (!_loadedDroplets.ContainsKey(def!.Id) && _registry.Get(def.Id) != null)
            {
                App.Log($"[CommunityDropletService] Rejected community droplet '{def.Id}' because it collides with a built-in droplet.");
                return false;
            }

            RegisterCommunityDroplet(def, notifyUser);
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                _fileToDropletId[sourceFilePath] = def.Id;
            }
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

        void ApplyRegistration()
        {
            if (_disposed) return;
            _registry.RegisterOrUpdate(descriptor);
            if (_registry.IsEnabled(def.Id))
            {
                OnDropletActivated(def);
            }
        }

        if (_uiDispatcher.CheckAccess())
            ApplyRegistration();
        else
            _uiDispatcher.Invoke(ApplyRegistration);

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
        if (_disposed) return;

        int intervalSec = Math.Clamp(def.RefreshIntervalSec, 5, 86400);
        if (_choreographer != null)
        {
            _choreographer.Register($"community_droplet:{def.Id}", () => Task.Run(() => PollEndpointGuardedAsync(def)).FireAndForget("CommunityDroplet.Chore"), intervalSec, PowerSensitivity.Relaxed);
        }
        else
        {
            var timer = new DispatcherTimer(
                TimeSpan.FromSeconds(intervalSec),
                DispatcherPriority.Background,
                (_, _) => _ = PollEndpointGuardedAsync(def),
                _uiDispatcher);

            _pollTimers[def.Id] = timer;
            timer.Start();
        }

        // Immediate background poll
        Task.Run(() => PollEndpointGuardedAsync(def)).FireAndForget("CommunityDroplet.Poll");
    }

    private async Task PollEndpointGuardedAsync(CommunityDropletDefinition def)
    {
        if (_disposed || !_loadedDroplets.ContainsKey(def.Id) || !_registry.IsEnabled(def.Id))
            return;

        if (!_pollInFlight.TryAdd(def.Id, 1))
            return;

        try
        {
            await PollEndpointAsync(def).ConfigureAwait(false);
        }
        finally
        {
            _pollInFlight.TryRemove(def.Id, out _);
        }
    }

    private void StopDropletPolling(string id)
    {
        _choreographer?.Unregister($"community_droplet:{id}");
        if (_pollTimers.TryRemove(id, out var timer))
        {
            if (_uiDispatcher.CheckAccess())
                timer.Stop();
            else
                _uiDispatcher.BeginInvoke(timer.Stop);
        }
    }

    public async Task<bool> PollEndpointAsync(CommunityDropletDefinition def)
    {
        if (_disposed || string.IsNullOrWhiteSpace(def.Endpoint)) return false;

        try
        {
            if (!await IsAllowedExternalHttpUrlAsync(def.Endpoint).ConfigureAwait(false))
            {
                App.Log($"[CommunityDropletService] Blocked unsafe/private endpoint URL for droplet '{def.Id}': {def.Endpoint}");
                return false;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, def.Endpoint);
            if (def.Headers != null)
            {
                foreach (var (k, v) in def.Headers)
                {
                    req.Headers.TryAddWithoutValidation(k, v);
                }
            }

            using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                App.Log($"[CommunityDropletService] HTTP {resp.StatusCode} for droplet '{def.Id}'");
                return false;
            }

            string content = await ReadBoundedContentAsync(resp.Content, MaxResponseBytes).ConfigureAwait(false);
            if (_disposed || !_loadedDroplets.ContainsKey(def.Id) || !_registry.IsEnabled(def.Id))
                return false;

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

    private static async Task<string> ReadBoundedContentAsync(HttpContent content, int maxBytes)
    {
        await using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var ms = new MemoryStream();
        byte[] buf = new byte[8192];
        int remaining = maxBytes;
        while (remaining > 0)
        {
            int read = await stream.ReadAsync(buf.AsMemory(0, Math.Min(buf.Length, remaining))).ConfigureAwait(false);
            if (read == 0) break;
            ms.Write(buf, 0, read);
            remaining -= read;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task<bool> IsAllowedExternalHttpUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (IPAddress.TryParse(uri.Host, out var literalIp))
            return !IsPrivateOrLoopbackIp(literalIp);

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var addrs = await Dns.GetHostAddressesAsync(uri.Host).ConfigureAwait(false);
            if (addrs.Length == 0) return false;
            foreach (var addr in addrs)
            {
                if (IsPrivateOrLoopbackIp(addr))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPrivateOrLoopbackIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 10) return true;                                  // 10.0.0.0/8
            if (b[0] == 127) return true;                                 // 127.0.0.0/8
            if (b[0] == 169 && b[1] == 254) return true;                  // 169.254.0.0/16 link-local
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;     // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;                  // 192.168.0.0/16
            if (b[0] == 0) return true;                                   // 0.0.0.0/8
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            byte[] b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;                       // fc00::/7 unique local
        }
        return false;
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
                    Callback: () => ExecuteAction(captured, def.Id, def.Name)
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

    public void ExecuteAction(DropletActionDefinition action) => ExecuteAction(action, null, null);

    public void ExecuteAction(DropletActionDefinition action, string? dropletId, string? dropletName)
    {
        if (string.IsNullOrWhiteSpace(action.Command)) return;
        TactileFeedback.PlaySnap();

        try
        {
            string type = action.Type?.Trim() ?? "OpenUrl";

            if (string.Equals(type, "OpenUrl", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(action.Command.Trim(), UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    App.Log($"[CommunityDropletService] Blocked unsafe OpenUrl command: '{action.Command}'");
                    return;
                }

                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            else if (string.Equals(type, "ShellExecute", StringComparison.OrdinalIgnoreCase))
            {
                if (!EnsureShellCommandTrusted(dropletId ?? "community", dropletName ?? dropletId ?? "Community Droplet", action.Command))
                {
                    App.Log($"[CommunityDropletService] ShellExecute denied by user for droplet '{dropletId}'.");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("/d");
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(action.Command);

                using var _ = Process.Start(psi);
            }
            else if (string.Equals(type, "HttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                var method = (action.Method?.Trim().ToUpperInvariant()) switch
                {
                    "POST" => HttpMethod.Post,
                    "PUT" => HttpMethod.Put,
                    "DELETE" => HttpMethod.Delete,
                    "PATCH" => HttpMethod.Patch,
                    _ => HttpMethod.Get
                };

                string url = action.Command.Trim();
                Task.Run(async () =>
                {
                    try
                    {
                        if (!await IsAllowedExternalHttpUrlAsync(url).ConfigureAwait(false))
                        {
                            App.Log($"[CommunityDropletService] Blocked unsafe/private HttpRequest action URL: {url}");
                            return;
                        }

                        using var req = new HttpRequestMessage(method, url);
                        using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[CommunityDropletService] HttpRequest action error: {ex.Message}");
                    }
                }).FireAndForget("CommunityDroplet.HttpAction");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[CommunityDropletService] Action failed: {ex.Message}");
        }
    }

    private string TrustStorePath => Path.Combine(AppPaths.DropletsDirectory, "trusted_commands.json");

    private void LoadTrustedShellCommands()
    {
        var list = AtomicJsonStore.Load<List<string>>(TrustStorePath, () => new List<string>());
        lock (_trustLock)
        {
            _trustedShellCommands.Clear();
            foreach (var entry in list)
            {
                if (!string.IsNullOrWhiteSpace(entry)) _trustedShellCommands.Add(entry);
            }
        }
    }

    private bool EnsureShellCommandTrusted(string dropletId, string dropletName, string command)
    {
        string cmdHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command)));
        string key = $"{dropletId}:{cmdHash}";

        lock (_trustLock)
        {
            if (_trustedShellCommands.Contains(key))
                return true;
        }

        bool approved = false;
        try
        {
            var res = _uiDispatcher.Invoke(() =>
                MessageBox.Show(
                    $"Community droplet '{dropletName}' wants to execute a shell command:\n\n{command}\n\nAllow this command to run?",
                    "Dynamic Island — Community Droplet Security",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No));
            approved = res == MessageBoxResult.Yes;
        }
        catch
        {
            approved = false;
        }

        if (approved)
        {
            List<string> snapshot;
            lock (_trustLock)
            {
                _trustedShellCommands.Add(key);
                snapshot = _trustedShellCommands.ToList();
            }
            AtomicJsonStore.Save(TrustStorePath, snapshot, _trustLock);
        }

        return approved;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        Debounce(e.FullPath, () =>
        {
            LoadDropletFromFile(e.FullPath, notifyUser: true);
        });
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        Debounce(e.FullPath, () =>
        {
            string id = _fileToDropletId.TryRemove(e.FullPath, out var mapped)
                ? mapped
                : Path.GetFileNameWithoutExtension(e.Name ?? "");
            UnregisterCommunityDroplet(id, notifyUser: true);
        });
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        Debounce(e.FullPath, () =>
        {
            string oldId = _fileToDropletId.TryRemove(e.OldFullPath, out var mapped)
                ? mapped
                : Path.GetFileNameWithoutExtension(e.OldName ?? "");
            UnregisterCommunityDroplet(oldId, notifyUser: false);
            LoadDropletFromFile(e.FullPath, notifyUser: true);
        });
    }

    private void Debounce(string key, Action action)
    {
        if (_disposed) return;

        var cts = new CancellationTokenSource();
        if (_debounceTokens.TryGetValue(key, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
        _debounceTokens[key] = cts;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(220, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested || _disposed) return;

                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (!_disposed && !cts.IsCancellationRequested)
                        action();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Log($"[CommunityDropletService] Hot-reload error: {ex.Message}");
            }
            finally
            {
                _debounceTokens.TryRemove(KeyValuePair.Create(key, cts));
                cts.Dispose();
            }
        }).FireAndForget("CommunityDroplet.Reload");
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

        foreach (var cts in _debounceTokens.Values)
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }
        _debounceTokens.Clear();

        foreach (var id in _loadedDroplets.Keys)
        {
            _choreographer?.Unregister($"community_droplet:{id}");
        }
        foreach (var timer in _pollTimers.Values)
        {
            if (_uiDispatcher.CheckAccess())
                timer.Stop();
            else
                _uiDispatcher.BeginInvoke(timer.Stop);
        }
        _pollTimers.Clear();
        _pollInFlight.Clear();
        _fileToDropletId.Clear();
        _loadedDroplets.Clear();
    }
}
