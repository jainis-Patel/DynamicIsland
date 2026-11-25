using System.Diagnostics;
using System.IO;
using System.Windows;

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
        App.Log($"[UpdateHandoff] Spawning silent upgrade from: {installerExePath}");

        int currentPid = Environment.ProcessId;
        string? currentExe = Environment.ProcessPath;
        string? currentDir = !string.IsNullOrEmpty(currentExe) ? Path.GetDirectoryName(currentExe) : null;
        string targetDirArg = !string.IsNullOrWhiteSpace(currentDir) ? $" --target-dir \"{currentDir}\"" : "";
        string args = $"--install --silent --relaunch --wait-pid {currentPid}{targetDirArg} /SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";

        var startInfo = new ProcessStartInfo
        {
            FileName = installerExePath,
            Arguments = args,
            UseShellExecute = true
        };

        using var proc = Process.Start(startInfo);
        if (proc != null)
        {
            RequestGracefulExit();
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

        string targetDir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
        string stagedNewExe = Path.Combine(targetDir, $".DynamicIsland.update_{Guid.NewGuid():N}.tmp");
        try
        {
            // Stage on the same volume as the target executable so replacement is an atomic move
            File.Copy(newBinaryPath, stagedNewExe, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Log($"[UpdateHandoff] Failed to stage binary on target volume: {ex.Message}");
            UpdateIntegrityVerifier.TryCleanFile(stagedNewExe);
            return false;
        }

        string updatesDir = Path.Combine(AppPaths.DataDirectory, "Updates");
        Directory.CreateDirectory(updatesDir);
        string scriptPath = Path.Combine(updatesDir, $"apply_update_{Guid.NewGuid():N}.cmd");

        string currentExeNormalized = currentExe.Replace("/", "\\");
        string backupExeNormalized = (currentExe + BackupExtension).Replace("/", "\\");
        int currentPid = Environment.ProcessId;

        // Pass paths strictly via environment variables without delayed expansion so special
        // CMD characters (&, |, ^, !, (, ), %) in paths cannot be injected or corrupted.
        const string scriptContent = @"@echo off
chcp 65001 >nul
set ""WAITCOUNT=0""

:wait_process
tasklist /fi ""PID eq %DI_WAIT_PID%"" 2>nul | findstr /i ""%DI_WAIT_PID%"" >nul
if not errorlevel 1 goto wait_more
goto do_backup_prep

:wait_more
set /a ""WAITCOUNT+=1""
if %WAITCOUNT% GEQ 30 goto abort_no_relaunch
timeout /t 1 /nobreak >nul
goto wait_process

:do_backup_prep
timeout /t 1 /nobreak >nul
set ""RETRY=0""

:try_backup
move /y ""%DI_TARGET%"" ""%DI_BAK%"" >nul 2>&1
if errorlevel 1 goto backup_retry
set ""RETRY=0""
goto try_replace

:backup_retry
set /a ""RETRY+=1""
if %RETRY% LSS 5 timeout /t 1 /nobreak >nul
if %RETRY% LSS 5 goto try_backup
goto abort_restore

:try_replace
move /y ""%DI_NEWFILE%"" ""%DI_TARGET%"" >nul 2>&1
if errorlevel 1 goto replace_retry
start """" ""%DI_TARGET%"" --updated
goto cleanup

:replace_retry
set /a ""RETRY+=1""
if %RETRY% LSS 5 timeout /t 1 /nobreak >nul
if %RETRY% LSS 5 goto try_replace
goto abort_restore

:abort_restore
if not exist ""%DI_TARGET%"" if exist ""%DI_BAK%"" move /y ""%DI_BAK%"" ""%DI_TARGET%"" >nul 2>&1
if exist ""%DI_NEWFILE%"" del /f /q ""%DI_NEWFILE%"" >nul 2>&1
if exist ""%DI_TARGET%"" start """" ""%DI_TARGET%""
goto cleanup

:abort_no_relaunch
if exist ""%DI_NEWFILE%"" del /f /q ""%DI_NEWFILE%"" >nul 2>&1

:cleanup
(goto) 2>nul & del ""%~f0""
";

        File.WriteAllText(scriptPath, scriptContent);

        App.Log($"[UpdateHandoff] Launching atomic binary replacement script: {scriptPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.Environment["DI_TARGET"] = currentExeNormalized;
        startInfo.Environment["DI_NEWFILE"] = stagedNewExe;
        startInfo.Environment["DI_BAK"] = backupExeNormalized;
        startInfo.Environment["DI_WAIT_PID"] = currentPid.ToString();

        using var proc = Process.Start(startInfo);
        if (proc != null)
        {
            RequestGracefulExit();
            return true;
        }

        UpdateIntegrityVerifier.TryCleanFile(stagedNewExe);
        UpdateIntegrityVerifier.TryCleanFile(scriptPath);
        return false;
    }

    private static void RequestGracefulExit()
    {
        App.FlushLogs();

        var app = Application.Current;
        if (app != null)
        {
            try
            {
                app.Dispatcher.BeginInvoke(() =>
                {
                    try { app.Shutdown(0); }
                    catch { Environment.Exit(0); }
                });
            }
            catch
            {
                Environment.Exit(0);
            }

            // Watchdog fallback in case a modal/hanging resource blocks Shutdown
            Task.Run(async () =>
            {
                await Task.Delay(5000).ConfigureAwait(false);
                App.FlushLogs();
                Environment.Exit(0);
            }).FireAndForget("UpdateHandoff.Launch");
            return;
        }

        Program.SignalStopOnDefaultDesktop();
        Thread.Sleep(300);
        Environment.Exit(0);
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
