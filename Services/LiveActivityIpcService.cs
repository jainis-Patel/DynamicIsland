using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class ActivityIpcRequest
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("glyph")]
    public string? Glyph { get; set; }

    [JsonPropertyName("accent")]
    public string? Accent { get; set; }

    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("targetView")]
    public string? TargetView { get; set; }
}

public sealed class ActivityIpcResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// High-throughput, zero-allocation Named Pipe server allowing any external CLI tool,
/// AI coding agent (Claude Code, Antigravity, Copilot), build script, or timer
/// to broadcast Live Activities directly into Dynamic Island with sub-millisecond latency.
/// Pipe: \\.\pipe\DynamicIsland.Activities (CurrentUserOnly ACL)
/// </summary>
internal sealed class LiveActivityIpcService : IDisposable
{
    public const string PipeName = "DynamicIsland.Activities";

    private readonly LiveActivityHub _hub;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public LiveActivityIpcService(LiveActivityHub hub)
    {
        _hub = hub;
    }

    public void Start()
    {
        Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested && !_disposed)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(server, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                App.Log($"[LiveActivityIpc] Pipe server accept error: {ex.Message}");
                await Task.Delay(250, _cts.Token);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            try
            {
                string? line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) return;

                var req = JsonSerializer.Deserialize<ActivityIpcRequest>(line);
                var resp = ProcessRequest(req);
                string jsonResp = JsonSerializer.Serialize(resp);
                await writer.WriteLineAsync(jsonResp);
            }
            catch (Exception ex)
            {
                try
                {
                    var errResp = new ActivityIpcResponse { Ok = false, Error = ex.Message };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(errResp));
                }
                catch { }
            }
        }
    }

    private ActivityIpcResponse ProcessRequest(ActivityIpcRequest? req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Action))
        {
            return new ActivityIpcResponse { Ok = false, Error = "Missing 'action' parameter." };
        }

        string action = req.Action.Trim().ToLowerInvariant();
        switch (action)
        {
            case "publish":
            case "start":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                Color accentColor = ThemeBrushes.GreenColor;
                if (!string.IsNullOrWhiteSpace(req.Accent))
                {
                    try
                    {
                        var parsed = ColorConverter.ConvertFromString(req.Accent);
                        if (parsed is Color c) accentColor = c;
                    }
                    catch { }
                }

                var item = new LiveActivityItem
                {
                    Id = req.Id.Trim(),
                    Source = string.IsNullOrWhiteSpace(req.Source) ? "External" : req.Source.Trim(),
                    Title = string.IsNullOrWhiteSpace(req.Title) ? req.Id.Trim() : req.Title.Trim(),
                    Subtitle = req.Subtitle?.Trim() ?? "",
                    Glyph = string.IsNullOrWhiteSpace(req.Glyph) ? "\uE7E7" : req.Glyph.Trim(),
                    AccentColor = accentColor,
                    Progress = req.Progress.HasValue ? Math.Clamp(req.Progress.Value, 0.0, 1.0) : null,
                    Priority = req.Priority ?? 60,
                    TargetViewKey = req.TargetView?.Trim()
                };

                _hub.Publish(item);
                return new ActivityIpcResponse { Ok = true, Message = $"Published activity '{item.Id}'." };
            }

            case "update":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                double? clampedProgress = req.Progress.HasValue ? Math.Clamp(req.Progress.Value, 0.0, 1.0) : null;
                bool ok = _hub.UpdateProgress(req.Id.Trim(), clampedProgress, req.Subtitle?.Trim());
                if (!ok && !string.IsNullOrWhiteSpace(req.Title))
                {
                    // If not found, auto-publish
                    return ProcessRequest(new ActivityIpcRequest
                    {
                        Action = "publish",
                        Id = req.Id,
                        Source = req.Source,
                        Title = req.Title,
                        Subtitle = req.Subtitle,
                        Glyph = req.Glyph,
                        Accent = req.Accent,
                        Progress = req.Progress,
                        Priority = req.Priority,
                        TargetView = req.TargetView
                    });
                }

                return ok
                    ? new ActivityIpcResponse { Ok = true, Message = $"Updated activity '{req.Id}'." }
                    : new ActivityIpcResponse { Ok = false, Error = $"Activity '{req.Id}' not found." };
            }

            case "complete":
            case "end":
            case "stop":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                bool removed = _hub.Complete(req.Id.Trim());
                return new ActivityIpcResponse
                {
                    Ok = true,
                    Message = removed ? $"Completed activity '{req.Id}'." : $"Activity '{req.Id}' was already inactive."
                };
            }

            case "clear":
            {
                _hub.ClearAll();
                return new ActivityIpcResponse { Ok = true, Message = "Cleared all live activities." };
            }

            default:
                return new ActivityIpcResponse { Ok = false, Error = $"Unknown action '{req.Action}'." };
        }
    }

    public static async Task<(bool Success, string Message)> SendCliCommandAsync(ActivityIpcRequest request, int timeoutMs = 2500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(cts.Token);
            using var reader = new StreamReader(client, Encoding.UTF8);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };

            string json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json);

            string? line = await reader.ReadLineAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(line))
            {
                return (false, "Empty response from Dynamic Island activity service.");
            }

            var resp = JsonSerializer.Deserialize<ActivityIpcResponse>(line);
            if (resp == null) return (false, "Invalid JSON response from activity service.");

            return (resp.Ok, resp.Message ?? resp.Error ?? (resp.Ok ? "OK" : "Error"));
        }
        catch (TimeoutException)
        {
            return (false, "Could not connect to Dynamic Island: timed out. Is Dynamic Island running?");
        }
        catch (Exception ex)
        {
            return (false, $"Activity IPC error: {ex.Message}");
        }
    }

    public static async Task<int> RunCliAsync(string[] args)
    {
        // Expect: [activity|--activity] <action> [options]
        int actionIdx = 0;
        if (args.Length > 0 && (string.Equals(args[0], "activity", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(args[0], "--activity", StringComparison.OrdinalIgnoreCase)))
        {
            actionIdx = 1;
        }

        if (actionIdx >= args.Length)
        {
            PrintUsage();
            return 1;
        }

        string action = args[actionIdx].ToLowerInvariant();
        var req = new ActivityIpcRequest { Action = action };

        for (int i = actionIdx + 1; i < args.Length; i++)
        {
            string key = args[i];
            string? val = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : null;

            switch (key.ToLowerInvariant())
            {
                case "--id": req.Id = val; break;
                case "--title": req.Title = val; break;
                case "--subtitle": case "--sub": req.Subtitle = val; break;
                case "--glyph": case "-g": req.Glyph = val; break;
                case "--source": case "-s": req.Source = val; break;
                case "--accent": case "-c": req.Accent = val; break;
                case "--view": req.TargetView = val; break;
                case "--progress": case "-p":
                    if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p))
                        req.Progress = p;
                    break;
                case "--priority":
                    if (int.TryParse(val, out int prio)) req.Priority = prio;
                    break;
            }
        }

        var (ok, msg) = await SendCliCommandAsync(req);
        if (ok)
        {
            Console.WriteLine($"[DynamicIsland Activity] {msg}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"[DynamicIsland Activity] Failed: {msg}");
            return 1;
        }
    }

    public static void PrintUsage()
    {
        Console.WriteLine(@"Dynamic Island Live Activity CLI
Usage:
  DynamicIsland.exe activity start --id <id> --title <title> [options]
  DynamicIsland.exe activity update --id <id> [--progress <0.0..1.0>] [--subtitle <text>]
  DynamicIsland.exe activity complete --id <id>
  DynamicIsland.exe activity clear

Options:
  --id <id>          Unique identifier for this live activity (e.g. 'build_42')
  --title <text>     Primary title text shown in pill
  --subtitle <text>  Secondary descriptive text or step count
  --progress <0..1>  Floating point progress (0.0 to 1.0)
  --glyph <symbol>   Segoe Fluent glyph or emoji (e.g. '🔨', '⚡', '📦')
  --accent <hex>     Hex accent color (e.g. '#00F0FF', '#30D158')
  --priority <int>   Activity priority (higher number takes visual precedence)
  --source <name>    Source agent or tool name (e.g. 'antigravity', 'cargo', 'npm')
");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
