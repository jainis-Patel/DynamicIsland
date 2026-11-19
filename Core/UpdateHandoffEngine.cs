using System.Diagnostics;
using System.IO;

namespace DynamicIsland.Core;

/// <summary>
/// Manages the process handoff, safe executable replacement, and rollback protection on Windows.
/// Bypasses Windows file locks by orchestrating either Inno Setup silent restart or an atomic bootstrap script.
/// </summary>
public static class UpdateHandoffEngine
{
    private const string BackupExtension = ".bak";

    /// <summary>
    /// Executes the update handoff and terminates the current instance cleanly.
    /// </summary>
    /// <param name="packageFilePath">Path to the downloaded installer (.exe) or binary.</param>
    /// <param name="isInstaller">Whether the package is an Inno Setup installer executable.</param>
    /// <returns>True if the handoff process was successfully spawned.</returns>
    public static bool ExecuteHandoff(string packageFilePath, bool isInstaller)
    {
        if (!File.Exists(packageFilePath))
        {
            App.Log($"[UpdateHandoff] Cannot execute update handoff: package '{packageFilePath}' does not exist.");
            return false;
        }

        try
        {
            if (isInstaller)
            {
                return LaunchInstallerHandoff(packageFilePath);
            }
            else
            {
                return LaunchBinaryHandoff(packageFilePath);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[UpdateHandoff] Failed to launch update handoff: {ex.Message}");
            return false;
        }
    }

    private static bool LaunchInstallerHandoff(string installerExePath)
    {
        App.Log($"[UpdateHandoff] Spawning Inno Setup silent upgrade from: {installerExePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = installerExePath,
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        };

        var proc = Process.Start(startInfo);
        if (proc != null)
        {
            // Give installer a moment to spin up, then exit current instance gracefully
            Program.SignalStopOnDefaultDesktop();
            Thread.Sleep(300);
            Environment.Exit(0);
            return true;
        }
        return false;
    }

    private static bool LaunchBinaryHandoff(string newBinaryPath)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
        {
            App.Log("[UpdateHandoff] Unable to determine current process path for binary handoff.");
            return false;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "DynamicIsland", "Updates");
        Directory.CreateDirectory(tempDir);
        string scriptPath = Path.Combine(tempDir, "apply_update.cmd");

        string currentExeEscaped = currentExe.Replace("/", "\\");
        string newBinaryEscaped = newBinaryPath.Replace("/", "\\");
        string backupExeEscaped = (currentExe + BackupExtension).Replace("/", "\\");
        int currentPid = Environment.ProcessId;

        // Atomic script: waits for current PID to exit, backs up current exe with retry,
        // moves new exe with retry, auto-rolls back if replace fails, relaunches with --updated
        string scriptContent = $@"@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

set TARGET={currentExeEscaped}
set NEWFILE={newBinaryEscaped}
set BAK={backupExeEscaped}
set PID={currentPid}

rem 1. Wait up to 30 seconds for the running process (PID %PID%) to terminate
set WAITCOUNT=0
:wait_process
tasklist /fi ""PID eq %PID%"" 2>nul | findstr /i ""%PID%"" >nul
if not errorlevel 1 (
    set /a WAITCOUNT+=1
    if !WAITCOUNT! geq 30 (
        goto abort_and_restore
    )
    timeout /t 1 /nobreak >nul
    goto wait_process
)

rem Brief delay to allow Windows to release file locks completely
timeout /t 1 /nobreak >nul

rem 2. Backup existing binary for rollback safety (with retry up to 5 times)
set RETRY=0
:try_backup
move /y ""!TARGET!"" ""!BAK!"" >nul 2>&1
if errorlevel 1 (
    set /a RETRY+=1
    if !RETRY! lss 5 (
        timeout /t 1 /nobreak >nul
        goto try_backup
    )
    goto abort_and_restore
)

rem 3. Move newly downloaded binary into place (with retry up to 5 times)
set RETRY=0
:try_replace
move /y ""!NEWFILE!"" ""!TARGET!"" >nul 2>&1
if errorlevel 1 (
    set /a RETRY+=1
    if !RETRY! lss 5 (
        timeout /t 1 /nobreak >nul
        goto try_replace
    )
    rem Failed to install new binary! Automatically roll back from backup
    goto abort_and_restore
)

rem 4. Relaunch updated application
start """" ""!TARGET!"" --updated
goto cleanup

:abort_and_restore
if not exist ""!TARGET!"" (
    if exist ""!BAK!"" move /y ""!BAK!"" ""!TARGET!"" >nul 2>&1
)
if exist ""!TARGET!"" (
    start """" ""!TARGET!""
)

:cleanup
(goto) 2>nul & del ""%~f0""
";

        File.WriteAllText(scriptPath, scriptContent);

        App.Log($"[UpdateHandoff] Launching atomic binary replacement script: {scriptPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var proc = Process.Start(startInfo);
        if (proc != null)
        {
            Program.SignalStopOnDefaultDesktop();
            Thread.Sleep(300);
            Environment.Exit(0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cleans up obsolete .bak backup files once the updated application has booted and verified stable.
    /// </summary>
    public static void CleanupOldBackups()
    {
        try
        {
            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return;

            string backupExe = currentExe + BackupExtension;
            if (File.Exists(backupExe))
            {
                File.Delete(backupExe);
                App.Log("[UpdateHandoff] Cleaned up obsolete pre-update backup executable.");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[UpdateHandoff] Non-fatal error cleaning up backup: {ex.Message}");
        }
    }
}
