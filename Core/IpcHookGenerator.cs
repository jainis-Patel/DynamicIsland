using System.IO;
using System.Text.Json;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Generates ready-to-use Live Activity IPC 2.0 hook scripts and configuration files
/// targeting \\.\pipe\DynamicIsland.Activities for AI coding agents and CLI build pipelines.
/// </summary>
internal static class IpcHookGenerator
{
    public const string PipePath = @"\\.\pipe\DynamicIsland.Activities";

    public static string BuildClaudeHooksJson(string ps1ScriptPath)
    {
        var payload = new
        {
            hooks = new
            {
                PreToolUse = new[]
                {
                    new
                    {
                        matcher = "*",
                        hooks = new[]
                        {
                            new
                            {
                                type = "command",
                                command = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ps1ScriptPath}\" -Phase working"
                            }
                        }
                    }
                },
                Stop = new[]
                {
                    new
                    {
                        hooks = new[]
                        {
                            new
                            {
                                type = "command",
                                command = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ps1ScriptPath}\" -Phase done"
                            }
                        }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildClaudeHookPs1() => """
param([string]$Phase = "working", [string]$Title = "Claude Code")
$pipeName = "DynamicIsland.Activities"
$isDone = $Phase -eq "done"
$msg = @{
    id = "claude-agent"
    source = "claude"
    title = $Title
    subtitle = if ($isDone) { "Task completed" } else { "Executing tool..." }
    status = if ($isDone) { "Completed" } else { "Active" }
    ttlSeconds = if ($isDone) { 4 } else { 30 }
} | ConvertTo-Json -Compress
try {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::Out)
    $client.Connect(250)
    $writer = New-Object System.IO.StreamWriter($client)
    $writer.AutoFlush = $true
    $writer.WriteLine($msg)
    $writer.Dispose()
    $client.Dispose()
} catch {}
""";

    public static string BuildCliWrapperPs1() => """
param([Parameter(Mandatory=$true)][string]$Command, [string]$Title = "CLI Task")
$pipeName = "DynamicIsland.Activities"
function Send-Island([string]$status, [string]$sub, [int]$ttl = 30) {
    $msg = @{ id = "cli-run"; source = "cli"; title = $Title; subtitle = $sub; status = $status; ttlSeconds = $ttl } | ConvertTo-Json -Compress
    try {
        $c = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::Out)
        $c.Connect(200)
        $w = New-Object System.IO.StreamWriter($c); $w.AutoFlush = $true; $w.WriteLine($msg)
        $w.Dispose(); $c.Dispose()
    } catch {}
}
Send-Island "Active" "Running: $Command" 120
Invoke-Expression $Command
if ($LASTEXITCODE -eq 0) { Send-Island "Completed" "Succeeded" 4 } else { Send-Island "Failed" "Exited with $LASTEXITCODE" 6 }
exit $LASTEXITCODE
""";

    public static (bool Success, string OutputDir, int FileCount, string? Error) ExportAll(string? targetDirectory = null)
    {
        try
        {
            string dir = string.IsNullOrWhiteSpace(targetDirectory)
                ? Path.Combine(AppPaths.DataDirectory, "hooks")
                : targetDirectory;
            Directory.CreateDirectory(dir);

            string ps1Path = Path.Combine(dir, "claude-island-hook.ps1");
            string claudeJsonPath = Path.Combine(dir, "claude-hooks.json");
            string cliWrapperPath = Path.Combine(dir, "di-run.ps1");

            File.WriteAllText(ps1Path, BuildClaudeHookPs1());
            File.WriteAllText(claudeJsonPath, BuildClaudeHooksJson(ps1Path));
            File.WriteAllText(cliWrapperPath, BuildCliWrapperPs1());

            return (true, dir, 3, null);
        }
        catch (Exception ex)
        {
            return (false, "", 0, ex.Message);
        }
    }
}
