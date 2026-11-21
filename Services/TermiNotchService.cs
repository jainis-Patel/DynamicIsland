using System.Diagnostics;
using System.IO;
using System.Text;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

public sealed class TermiCommandSnippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Shell { get; set; } = "powershell"; // "powershell" or "cmd"
    public string Glyph { get; set; } = "\uE756"; // Terminal prompt
    public DateTimeOffset? LastRunAt { get; set; }
    public int? LastExitCode { get; set; }
}

/// <summary>
/// Termi-Notch: Embedded shell snippet manager and runner inside Dynamic Island.
/// Executes PowerShell / CMD snippets with real-time live output streaming,
/// non-admin safety isolation, and zero background CPU when idle.
/// Stored in %LocalAppData%\DynamicIsland\commands.json
/// </summary>
internal sealed class TermiNotchService : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly List<TermiCommandSnippet> _snippets = new();
    private readonly DebouncedJsonStore<List<TermiCommandSnippet>> _store;
    private CancellationTokenSource? _currentRunCts;
    private Process? _activeProcess;
    private int _isRunningFlag;
    private bool _disposed;

    public event Action? SnippetsChanged;
    public event Action<string, bool>? OutputReceived;
    public event Action<bool, int>? ExecutionCompleted; // (success, exitCode)

    public bool IsRunning => Volatile.Read(ref _isRunningFlag) == 1;

    public IReadOnlyList<TermiCommandSnippet> Snippets
    {
        get
        {
            lock (_lock) return _snippets.ToList();
        }
    }

    public TermiNotchService(string? customFilePath = null)
    {
        _filePath = customFilePath ?? AppPaths.TermiNotchFile;
        _store = new DebouncedJsonStore<List<TermiCommandSnippet>>(_filePath, () =>
        {
            lock (_lock) return _snippets.ToList();
        });
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                var loaded = AtomicJsonStore.Load<List<TermiCommandSnippet>>(_filePath, () => new List<TermiCommandSnippet>());
                _snippets.Clear();
                if (loaded != null && loaded.Count > 0)
                {
                    _snippets.AddRange(loaded);
                }
                else
                {
                    PopulateDefaults();
                }
            }
            catch (Exception ex)
            {
                App.Log($"[TermiNotch] Load failed: {ex.Message}");
                PopulateDefaults();
            }
        }
    }

    private void PopulateDefaults()
    {
        _snippets.Add(new TermiCommandSnippet
        {
            Name = "Git Status",
            Command = "git status --short",
            Shell = "powershell",
            Glyph = "\uE9D9"
        });

        _snippets.Add(new TermiCommandSnippet
        {
            Name = "Flush DNS Cache",
            Command = "ipconfig /flushdns",
            Shell = "cmd",
            Glyph = "\uE774"
        });

        _snippets.Add(new TermiCommandSnippet
        {
            Name = "Ping Cloudflare (1.1.1.1)",
            Command = "ping 1.1.1.1 -n 4",
            Shell = "cmd",
            Glyph = "\uE896"
        });

        _snippets.Add(new TermiCommandSnippet
        {
            Name = "Top CPU Processes",
            Command = "Get-Process | Sort-Object CPU -Descending | Select-Object -First 5 Name, CPU, WorkingSet64",
            Shell = "powershell",
            Glyph = "\uE9D9"
        });
    }

    public void SaveNow()
    {
        if (_disposed) return;
        try
        {
            _store.FlushNow();
        }
        catch (Exception ex)
        {
            App.Log($"[TermiNotch] SaveNow error: {ex.Message}");
        }
    }

    public TermiCommandSnippet AddSnippet(string name, string command, string shell = "powershell", string glyph = "\uE756")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var snippet = new TermiCommandSnippet
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Command = command.Trim(),
            Shell = string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase) ? "cmd" : "powershell",
            Glyph = glyph
        };

        lock (_lock)
        {
            _snippets.Add(snippet);
        }

        _store.ScheduleSave();
        SnippetsChanged?.Invoke();
        return snippet;
    }

    public bool DeleteSnippet(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _snippets.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            _store.ScheduleSave();
            SnippetsChanged?.Invoke();
        }
        return removed;
    }

    public async Task<int> RunSnippetAsync(string id)
    {
        TermiCommandSnippet? snippet;
        lock (_lock)
        {
            snippet = _snippets.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (snippet == null)
        {
            OutputReceived?.Invoke($"[Error] Snippet '{id}' not found.", true);
            return -1;
        }

        return await RunRawCommandAsync(snippet.Command, snippet.Shell, snippet);
    }

    public async Task<int> RunRawCommandAsync(string command, string shell = "powershell", TermiCommandSnippet? snippetToUpdate = null)
    {
        if (_disposed)
        {
            return -1;
        }

        if (Interlocked.CompareExchange(ref _isRunningFlag, 1, 0) != 0)
        {
            OutputReceived?.Invoke("[Warning] Another command is already running.", true);
            return -1;
        }

        var runCts = new CancellationTokenSource();
        lock (_lock)
        {
            if (_disposed)
            {
                runCts.Dispose();
                Interlocked.Exchange(ref _isRunningFlag, 0);
                return -1;
            }
            _currentRunCts = runCts;
        }

        var ct = runCts.Token;
        bool useCmd = string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase);

        OutputReceived?.Invoke($"> [{shell.ToUpperInvariant()}] {command}", false);

        var psi = new ProcessStartInfo
        {
            FileName = useCmd ? "cmd.exe" : "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (useCmd)
        {
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            string script = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {command}";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
        }

        int exitCode = -1;
        var sw = Stopwatch.StartNew();
        Process? proc = null;

        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var outputEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) outputEof.TrySetResult();
                else OutputReceived?.Invoke(e.Data, false);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) errorEof.TrySetResult();
                else OutputReceived?.Invoke(e.Data, true);
            };

            lock (_lock)
            {
                _activeProcess = proc;
            }

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);
            await Task.WhenAll(outputEof.Task, errorEof.Task).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
            exitCode = proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(proc);
            OutputReceived?.Invoke("[TermiNotch] Command cancelled by user.", true);
            exitCode = -999;
        }
        catch (Exception ex)
        {
            KillProcessTree(proc);
            OutputReceived?.Invoke($"[TermiNotch] Execution failed: {ex.Message}", true);
            exitCode = -1;
        }
        finally
        {
            sw.Stop();
            lock (_lock)
            {
                _activeProcess = null;
                _currentRunCts = null;
            }

            if (proc != null)
            {
                try { proc.Dispose(); } catch { }
            }

            Interlocked.Exchange(ref _isRunningFlag, 0);
            try { runCts.Dispose(); } catch { }

            if (snippetToUpdate != null)
            {
                lock (_lock)
                {
                    snippetToUpdate.LastRunAt = DateTimeOffset.Now;
                    snippetToUpdate.LastExitCode = exitCode;
                }
                _store.ScheduleSave();
                SnippetsChanged?.Invoke();
            }

            OutputReceived?.Invoke($"--- Process exited with code {exitCode} ({sw.ElapsedMilliseconds} ms) ---", false);
            ExecutionCompleted?.Invoke(exitCode == 0, exitCode);
        }

        return exitCode;
    }

    private static void KillProcessTree(Process? proc)
    {
        if (proc == null) return;
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(1500);
            }
        }
        catch { }
    }

    public void CancelCurrentCommand()
    {
        CancellationTokenSource? cts;
        Process? proc;
        lock (_lock)
        {
            cts = _currentRunCts;
            proc = _activeProcess;
        }

        try
        {
            cts?.Cancel();
            KillProcessTree(proc);
        }
        catch (Exception ex)
        {
            App.Log($"[TermiNotch] Cancel error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _store.Dispose();
        _disposed = true;
        CancelCurrentCommand();
    }
}
