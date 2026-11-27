using System.Collections.Concurrent;
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

    [JsonPropertyName("phases")]
    public string[]? Phases { get; set; }

    [JsonPropertyName("phaseIndex")]
    public int? PhaseIndex { get; set; }

    [JsonPropertyName("chunk")]
    public string? Chunk { get; set; }

    [JsonPropertyName("buttons")]
    public string[]? Buttons { get; set; }

    [JsonPropertyName("diff")]
    public string? Diff { get; set; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; set; }

    [JsonPropertyName("logLine")]
    public string? LogLine { get; set; }
}

public sealed class ActivityIpcResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("selectedButton")]
    public string? SelectedButton { get; set; }
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
    private const int MaxConcurrentClients = 16;
    private const int MaxLineBytes = 262_144; // 256 KB
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(10);

    private sealed record PendingPromptEntry(TaskCompletionSource<string> Tcs, long Sequence);

    private readonly LiveActivityHub _hub;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _clientGate = new(MaxConcurrentClients, MaxConcurrentClients);
    private readonly ConcurrentDictionary<string, PendingPromptEntry> _pendingPrompts = new(StringComparer.OrdinalIgnoreCase);
    private Task? _listenTask;
    private long _promptSequence;
    private volatile bool _disposed;

    public bool HasPendingPrompts => !_pendingPrompts.IsEmpty;

    public string? GetLatestPendingPromptId()
    {
        string? latestId = null;
        long maxSeq = -1;
        foreach (var kvp in _pendingPrompts)
        {
            if (kvp.Value.Sequence > maxSeq)
            {
                maxSeq = kvp.Value.Sequence;
                latestId = kvp.Key;
            }
        }
        return latestId;
    }

    public LiveActivityIpcService(LiveActivityHub hub)
    {
        _hub = hub;
    }

    public void Start()
    {
        if (_disposed || _listenTask != null) return;
        _listenTask = Task.Run(ListenLoopAsync);
    }

    public bool ResolvePrompt(string id, string chosenButton)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        string key = id.Trim();
        if (_pendingPrompts.TryRemove(key, out var entry))
        {
            bool ok = entry.Tcs.TrySetResult(chosenButton);
            _hub.Complete(key);
            return ok;
        }
        return false;
    }

    private void RegisterPendingPrompt(string promptId, PendingPromptEntry entry)
    {
        _pendingPrompts.AddOrUpdate(
            promptId,
            entry,
            (_, existing) =>
            {
                existing.Tcs.TrySetResult("superseded");
                return entry;
            });
    }

    private void CancelAllPendingPrompts()
    {
        foreach (var kvp in _pendingPrompts)
        {
            if (_pendingPrompts.TryRemove(kvp.Key, out var entry))
            {
                entry.Tcs.TrySetCanceled();
            }
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested && !_disposed)
        {
            NamedPipeServerStream? server = null;
            bool gateAcquired = false;
            try
            {
                await _clientGate.WaitAsync(_cts.Token);
                gateAcquired = true;

                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(_cts.Token);
                var connectedServer = server;
                server = null;

                Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(connectedServer, _cts.Token);
                    }
                    finally
                    {
                        try { _clientGate.Release(); } catch (ObjectDisposedException) { }
                    }
                }, CancellationToken.None).FireAndForget("LiveActivityIpc.Client");
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                if (gateAcquired)
                {
                    try { _clientGate.Release(); } catch (ObjectDisposedException) { }
                }
                break;
            }
            catch (ObjectDisposedException)
            {
                server?.Dispose();
                if (gateAcquired)
                {
                    try { _clientGate.Release(); } catch (ObjectDisposedException) { }
                }
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                if (gateAcquired)
                {
                    try { _clientGate.Release(); } catch (ObjectDisposedException) { }
                }
                App.Log($"[LiveActivityIpc] Pipe server accept error: {ex.Message}");
                try
                {
                    await Task.Delay(250, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        using (stream)
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            try
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(ClientReadTimeout);

                string? line = await ReadBoundedLineAsync(stream, MaxLineBytes, readCts.Token);
                if (string.IsNullOrWhiteSpace(line)) return;

                var req = JsonSerializer.Deserialize<ActivityIpcRequest>(line);
                if (req != null && (string.Equals(req.Action, "prompt", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(req.Action, "approval", StringComparison.OrdinalIgnoreCase)))
                {
                    await HandlePromptClientAsync(req, writer, ct);
                    return;
                }

                var resp = ProcessRequest(req);
                string jsonResp = JsonSerializer.Serialize(resp);
                await writer.WriteLineAsync(jsonResp);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    var timeoutResp = new ActivityIpcResponse { Ok = false, Error = "IPC request timed out or cancelled." };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(timeoutResp));
                }
                catch (Exception writeEx) { App.LogDebug("LiveActivityIpc.WriteTimeout", writeEx); }
            }
            catch (Exception ex)
            {
                try
                {
                    var errResp = new ActivityIpcResponse { Ok = false, Error = ex.Message };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(errResp));
                }
                catch (Exception writeEx) { App.LogDebug("LiveActivityIpc.WriteError", writeEx); }
            }
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream(Math.Min(4096, maxBytes));
        byte[] single = new byte[1];
        bool anyByte = false;

        while (true)
        {
            int read = await stream.ReadAsync(single.AsMemory(0, 1), ct);
            if (read == 0)
            {
                return anyByte ? Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length) : null;
            }

            anyByte = true;
            byte b = single[0];
            if (b == (byte)'\n')
            {
                int len = (int)ms.Length;
                if (len > 0 && ms.GetBuffer()[len - 1] == (byte)'\r')
                    len--;
                return Encoding.UTF8.GetString(ms.GetBuffer(), 0, len);
            }

            if (ms.Length >= maxBytes)
            {
                throw new InvalidDataException($"IPC payload line exceeded maximum limit of {maxBytes} bytes.");
            }

            ms.WriteByte(b);
        }
    }

    private async Task HandlePromptClientAsync(ActivityIpcRequest req, StreamWriter writer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
        {
            var errResp = new ActivityIpcResponse { Ok = false, Error = "Missing required 'id' for prompt." };
            await writer.WriteLineAsync(JsonSerializer.Serialize(errResp));
            return;
        }

        string promptId = req.Id.Trim();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingPromptEntry(tcs, Interlocked.Increment(ref _promptSequence));
        RegisterPendingPrompt(promptId, entry);

        int timeoutSec = Math.Clamp(req.TimeoutSec ?? 60, 5, 300);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        using var reg = linkedCts.Token.Register(() =>
        {
            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                tcs.TrySetException(new TimeoutException($"Prompt '{promptId}' timed out after {timeoutSec} seconds."));
            }
            else
            {
                tcs.TrySetCanceled();
            }
        });

        Color accentColor = ThemeBrushes.BlueColor;
        if (!string.IsNullOrWhiteSpace(req.Accent))
        {
            try
            {
                var parsed = ColorConverter.ConvertFromString(req.Accent);
                if (parsed is Color c) accentColor = c;
            }
            catch (Exception ex) { App.LogDebug("LiveActivityIpc.ParsePromptAccent", ex); }
        }

        var actions = new List<LiveActivityAction>();
        string[] buttons = req.Buttons != null && req.Buttons.Length > 0 ? req.Buttons : new[] { "Approve", "Reject" };

        foreach (var btn in buttons)
        {
            string bText = btn;
            actions.Add(new LiveActivityAction(
                Label: bText,
                Glyph: bText.Contains("Approve", StringComparison.OrdinalIgnoreCase) || bText.Contains("Yes", StringComparison.OrdinalIgnoreCase) ? "\uE73E" :
                       bText.Contains("Reject", StringComparison.OrdinalIgnoreCase) || bText.Contains("No", StringComparison.OrdinalIgnoreCase) ? "\uE711" :
                       bText.Contains("Diff", StringComparison.OrdinalIgnoreCase) ? "\uE8B7" : "\uE768",
                Callback: () =>
                {
                    if (bText.Contains("Diff", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(req.Diff))
                    {
                        IslandWindow.Instance?.Dispatcher.InvokeAsync(() =>
                        {
                            IslandWindow.Instance.EnsureTermiContainer().StageDiff(
                                req.Title ?? "AI Agent Change",
                                req.Subtitle,
                                req.Diff,
                                decision => ResolvePrompt(promptId, decision));
                            IslandWindow.Instance.SwitchExpandedView(ExpandedView.Termi, animateTransition: true);
                            IslandWindow.Instance.ExpandNow();
                        });
                    }
                    else
                    {
                        ResolvePrompt(promptId, bText);
                    }
                }
            ));
        }

        var item = new LiveActivityItem
        {
            Id = promptId,
            Source = string.IsNullOrWhiteSpace(req.Source) ? "AI Agent" : req.Source.Trim(),
            Title = string.IsNullOrWhiteSpace(req.Title) ? "AI Approval Required" : req.Title.Trim(),
            Subtitle = req.Subtitle?.Trim() ?? "Decision requested",
            Glyph = string.IsNullOrWhiteSpace(req.Glyph) ? "\uE756" : req.Glyph.Trim(),
            AccentColor = accentColor,
            Progress = null,
            Priority = req.Priority ?? 100,
            TargetViewKey = "termi",
            Actions = actions,
            Diff = req.Diff,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(timeoutSec)
        };

        _hub.Publish(item);

        IslandWindow.Instance?.Dispatcher.InvokeAsync(() =>
        {
            IslandWindow.Instance.PushInteractiveToast(
                glyph: item.Glyph,
                title: item.Title,
                subtitle: item.Subtitle,
                accent: item.AccentColor,
                duration: TimeSpan.FromSeconds(timeoutSec),
                activityId: promptId,
                diffText: req.Diff,
                onPromptDecision: decision => ResolvePrompt(promptId, decision));
        });

        try
        {
            string chosen = await tcs.Task;
            var okResp = new ActivityIpcResponse
            {
                Ok = true,
                SelectedButton = chosen,
                Message = $"User decided '{chosen}'."
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(okResp));
        }
        catch (TimeoutException)
        {
            var timeoutResp = new ActivityIpcResponse
            {
                Ok = false,
                Error = "timed_out",
                Message = $"Prompt timed out after {timeoutSec} seconds."
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(timeoutResp));
        }
        catch (Exception ex)
        {
            var cancelResp = new ActivityIpcResponse
            {
                Ok = false,
                Error = ex.Message,
                Message = "Prompt cancelled."
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(cancelResp));
        }
        finally
        {
            if (_pendingPrompts.TryRemove(KeyValuePair.Create(promptId, entry)))
            {
                _hub.Complete(promptId);
            }
        }
    }

    internal ActivityIpcResponse ProcessRequest(ActivityIpcRequest? req)
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
                    catch (Exception ex) { App.LogDebug("LiveActivityIpc.ParseAccent", ex); }
                }

                string subtitle = req.Subtitle?.Trim() ?? "";
                double? progress = req.Progress.HasValue ? Math.Clamp(req.Progress.Value, 0.0, 1.0) : null;

                if (req.Phases != null && req.Phases.Length > 0)
                {
                    int pIndex = Math.Clamp(req.PhaseIndex ?? 0, 0, req.Phases.Length - 1);
                    string phaseName = req.Phases[pIndex];
                    subtitle = string.IsNullOrEmpty(subtitle)
                        ? $"Step {pIndex + 1}/{req.Phases.Length}: {phaseName}"
                        : $"[{pIndex + 1}/{req.Phases.Length}] {phaseName} · {subtitle}";

                    if (!progress.HasValue)
                    {
                        progress = (pIndex + 0.5) / req.Phases.Length;
                    }
                }

                var item = new LiveActivityItem
                {
                    Id = req.Id.Trim(),
                    Source = string.IsNullOrWhiteSpace(req.Source) ? "External" : req.Source.Trim(),
                    Title = string.IsNullOrWhiteSpace(req.Title) ? req.Id.Trim() : req.Title.Trim(),
                    Subtitle = subtitle,
                    Glyph = string.IsNullOrWhiteSpace(req.Glyph) ? "\uE7E7" : req.Glyph.Trim(),
                    AccentColor = accentColor,
                    Progress = progress,
                    Priority = req.Priority ?? 60,
                    TargetViewKey = req.TargetView?.Trim()
                };

                _hub.Publish(item);
                if (!string.IsNullOrWhiteSpace(req.LogLine))
                {
                    _hub.AppendLog(item.Id, req.LogLine);
                }
                return new ActivityIpcResponse { Ok = true, Message = $"Published activity '{item.Id}'." };
            }

            case "log":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }
                string? line = req.LogLine ?? req.Chunk;
                if (string.IsNullOrWhiteSpace(line))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing 'logLine' or 'chunk'." };
                }
                bool appended = _hub.AppendLog(req.Id.Trim(), line);
                return appended
                    ? new ActivityIpcResponse { Ok = true, Message = $"Appended log to '{req.Id.Trim()}'." }
                    : new ActivityIpcResponse { Ok = false, Error = $"Activity '{req.Id.Trim()}' not found." };
            }

            case "update":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                string? subtitle = req.Subtitle?.Trim();
                double? clampedProgress = req.Progress.HasValue ? Math.Clamp(req.Progress.Value, 0.0, 1.0) : null;

                if (req.Phases != null && req.Phases.Length > 0)
                {
                    int pIndex = Math.Clamp(req.PhaseIndex ?? 0, 0, req.Phases.Length - 1);
                    string phaseName = req.Phases[pIndex];
                    subtitle = string.IsNullOrEmpty(subtitle)
                        ? $"Step {pIndex + 1}/{req.Phases.Length}: {phaseName}"
                        : $"[{pIndex + 1}/{req.Phases.Length}] {phaseName} · {subtitle}";

                    if (!clampedProgress.HasValue)
                    {
                        clampedProgress = (pIndex + 0.5) / req.Phases.Length;
                    }
                }

                bool ok = _hub.UpdateProgress(req.Id.Trim(), clampedProgress, subtitle);
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
                        TargetView = req.TargetView,
                        Phases = req.Phases,
                        PhaseIndex = req.PhaseIndex
                    });
                }

                return ok
                    ? new ActivityIpcResponse { Ok = true, Message = $"Updated activity '{req.Id}'." }
                    : new ActivityIpcResponse { Ok = false, Error = $"Activity '{req.Id}' not found." };
            }

            case "stream":
            case "chunk":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                string chunkText = req.Chunk?.Trim() ?? req.Subtitle?.Trim() ?? "";
                double? clampedProgress = req.Progress.HasValue ? Math.Clamp(req.Progress.Value, 0.0, 1.0) : null;
                bool ok = _hub.UpdateProgress(req.Id.Trim(), clampedProgress, chunkText);
                return ok
                    ? new ActivityIpcResponse { Ok = true, Message = $"Stream chunk applied to '{req.Id}'." }
                    : new ActivityIpcResponse { Ok = false, Error = $"Activity '{req.Id}' not found." };
            }

            case "fail":
            case "error":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                var existing = _hub.Activities.FirstOrDefault(a => string.Equals(a.Id, req.Id.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    _hub.Publish(existing with
                    {
                        Glyph = "\uE783",
                        AccentColor = ThemeBrushes.RedColor,
                        Subtitle = req.Subtitle?.Trim() ?? "Task failed",
                        Progress = 1.0
                    });
                }
                return new ActivityIpcResponse { Ok = true, Message = $"Marked activity '{req.Id}' as failed." };
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
                CancelAllPendingPrompts();
                _hub.ClearAll();
                return new ActivityIpcResponse { Ok = true, Message = "Cleared all live activities." };
            }

            case "prompt":
            case "approval":
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    return new ActivityIpcResponse { Ok = false, Error = "Missing required 'id'." };
                }

                string promptId = req.Id.Trim();
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var entry = new PendingPromptEntry(tcs, Interlocked.Increment(ref _promptSequence));
                RegisterPendingPrompt(promptId, entry);

                var actions = new List<LiveActivityAction>();
                string[] buttons = req.Buttons != null && req.Buttons.Length > 0 ? req.Buttons : new[] { "Approve", "Reject" };

                foreach (var b in buttons)
                {
                    string bText = b;
                    actions.Add(new LiveActivityAction(
                        Label: bText,
                        Glyph: bText.Contains("Approve", StringComparison.OrdinalIgnoreCase) ? "\uE73E" : "\uE711",
                        Callback: () => ResolvePrompt(promptId, bText)
                    ));
                }

                var item = new LiveActivityItem
                {
                    Id = promptId,
                    Source = string.IsNullOrWhiteSpace(req.Source) ? "AI Agent" : req.Source.Trim(),
                    Title = string.IsNullOrWhiteSpace(req.Title) ? "AI Approval Required" : req.Title.Trim(),
                    Subtitle = req.Subtitle?.Trim() ?? "Decision requested",
                    Glyph = string.IsNullOrWhiteSpace(req.Glyph) ? "\uE756" : req.Glyph.Trim(),
                    AccentColor = ThemeBrushes.BlueColor,
                    Priority = req.Priority ?? 100,
                    TargetViewKey = "termi",
                    Actions = actions,
                    Diff = req.Diff
                };

                _hub.Publish(item);
                return new ActivityIpcResponse { Ok = true, Message = $"Registered interactive prompt '{promptId}'." };
            }

            default:
                return new ActivityIpcResponse { Ok = false, Error = $"Unknown action '{req.Action}'." };
        }
    }

    public static async Task<(bool Success, string SelectedButton, string Message)> PromptCliAsync(ActivityIpcRequest request, int timeoutMs = 60000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(cts.Token);
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

            request.Action = "prompt";
            string json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json.AsMemory(), cts.Token);

            string? line = await reader.ReadLineAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(line))
            {
                return (false, "", "Empty response from Dynamic Island prompt service.");
            }

            var resp = JsonSerializer.Deserialize<ActivityIpcResponse>(line);
            if (resp == null) return (false, "", "Invalid response from prompt service.");

            return (resp.Ok, resp.SelectedButton ?? "", resp.Message ?? resp.Error ?? "");
        }
        catch (TimeoutException)
        {
            return (false, "", "Timed out connecting to Dynamic Island prompt service. Is Dynamic Island running?");
        }
        catch (OperationCanceledException)
        {
            return (false, "", "Timed out waiting for Dynamic Island prompt service. Is Dynamic Island running?");
        }
        catch (Exception ex)
        {
            return (false, "", $"Prompt pipe failure: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string Message)> SendCliCommandAsync(ActivityIpcRequest request, int timeoutMs = 2500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(cts.Token);
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

            string json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json.AsMemory(), cts.Token);

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
        catch (OperationCanceledException)
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
        try { _cts.Cancel(); } catch { }
        CancelAllPendingPrompts();
        try { _listenTask?.Wait(500); } catch { }
        try { _cts.Dispose(); } catch { }
        try { _clientGate.Dispose(); } catch { }
    }
}
