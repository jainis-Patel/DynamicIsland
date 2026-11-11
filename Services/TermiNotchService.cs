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
    private CancellationTokenSource? _currentRunCts;
    private Process? _activeProcess;
    private bool _disposed;

    public event Action? SnippetsChanged;
    public event Action<string, bool>? OutputReceived;
    public event Action<bool, int>? ExecutionCompleted; // (success, exitCode)

    public bool IsRunning { get; private set; }

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
        List<TermiCommandSnippet> snapshot;
        lock (_lock)
        {
            snapshot = _snippets.ToList();
        }

        try
        {
            AtomicJsonStore.Save(_filePath, snapshot, _lock);
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

        SaveNow();
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
            SaveNow();
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
        if (IsRunning)
        {
            OutputReceived?.Invoke("[Warning] Another command is already running.", true);
            return -1;
        }

        IsRunning = true;
        _currentRunCts = new CancellationTokenSource();
        var ct = _currentRunCts.Token;

        OutputReceived?.Invoke($"> [{shell.ToUpperInvariant()}] {command}", false);

        string exe = shell == "cmd" ? "cmd.exe" : "powershell.exe";
        string args = shell == "cmd" ? $"/c {command}" : $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        int exitCode = -1;
        var sw = Stopwatch.StartNew();

        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _activeProcess = proc;

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) OutputReceived?.Invoke(e.Data, false);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) OutputReceived?.Invoke(e.Data, true);
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);
            exitCode = proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            OutputReceived?.Invoke("[TermiNotch] Command cancelled by user.", true);
            exitCode = -999;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke($"[TermiNotch] Execution failed: {ex.Message}", true);
            exitCode = -1;
        }
        finally
        {
            sw.Stop();
            _activeProcess = null;
            IsRunning = false;
            _currentRunCts?.Dispose();
            _currentRunCts = null;

            if (snippetToUpdate != null)
            {
                snippetToUpdate.LastRunAt = DateTimeOffset.Now;
                snippetToUpdate.LastExitCode = exitCode;
                SaveNow();
                SnippetsChanged?.Invoke();
            }

            OutputReceived?.Invoke($"--- Process exited with code {exitCode} ({sw.ElapsedMilliseconds} ms) ---", false);
            ExecutionCompleted?.Invoke(exitCode == 0, exitCode);
        }

        return exitCode;
    }

    public void CancelCurrentCommand()
    {
        try
        {
            _currentRunCts?.Cancel();
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[TermiNotch] Cancel error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCurrentCommand();
        SaveNow();
    }
}
