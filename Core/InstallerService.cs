using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Handles seamless per-user installation, upgrade, shortcut creation, autostart registration,
/// and complete uninstallation for Dynamic Island without requiring administrator UAC privileges.
/// </summary>
internal static class InstallerService
{
    public const string AppDisplayName = "Dynamic Island";
    public const string AppPublisher = "Jainis Patel";
    public const string AppVersion = "0.10.0";

    public static string GetDefaultInstallDir()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Programs", "DynamicIsland");
    }

    public static string GetInstalledExePath(string? installDir = null)
    {
        return Path.Combine(installDir ?? GetDefaultInstallDir(), "DynamicIsland.exe");
    }

    public static string GetStartMenuShortcutPath()
    {
        string programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        return Path.Combine(programsFolder, "Dynamic Island.lnk");
    }

    public static string GetDesktopShortcutPath()
    {
        string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktopFolder, "Dynamic Island.lnk");
    }

    public static string GetStartupShortcutPath()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, "Dynamic Island.lnk");
    }

    public static (bool IsInstalled, string? Version, string? Location) GetExistingInstallation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland", writable: false);
            if (key != null)
            {
                string? version = key.GetValue("DisplayVersion") as string;
                string? location = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
                {
                    return (true, version ?? AppVersion, location);
                }
            }
        }
        catch (Exception ex) { App.LogDebug("Installer.ReadRegistry", ex); }

        // Fallback: check default install directory
        string defaultDir = GetDefaultInstallDir();
        string defaultExe = GetInstalledExePath(defaultDir);
        if (File.Exists(defaultExe))
        {
            return (true, AppVersion, defaultDir);
        }

        return (false, null, null);
    }

    public static string GetDriveFreeSpace(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                double gb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                return $"{gb:0.0} GB free on {drive.Name.TrimEnd('\\')}";
            }
        }
        catch (Exception ex) { App.LogDebug("Installer.GetDriveFreeSpace", ex); }
        return "Sufficient space available";
    }

    public static bool IsRunningFromInstallDir(string? exePath = null)
    {
        string? currentExe = exePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return false;

        string installedExe = GetInstalledExePath();
        try
        {
            return string.Equals(
                Path.GetFullPath(currentExe),
                Path.GetFullPath(installedExe),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsInstalledAtCurrentLocation(string? exePath = null)
    {
        if (IsRunningFromInstallDir(exePath)) return true;

        string? currentExe = exePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return false;

        var existing = GetExistingInstallation();
        if (!existing.IsInstalled || string.IsNullOrWhiteSpace(existing.Location)) return false;

        try
        {
            string currentDir = Path.GetFullPath(Path.GetDirectoryName(currentExe) ?? "");
            string installedDir = Path.GetFullPath(existing.Location);
            return string.Equals(
                currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                installedDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsInstallerMode(string[] args, string? exePath = null)
    {
        if (args.Any(a => string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "/setup", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "/install", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-i", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string? currentExe = exePath ?? Environment.ProcessPath;
        if (!string.IsNullOrEmpty(currentExe))
        {
            string name = Path.GetFileNameWithoutExtension(currentExe);
            if ((name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Installer", StringComparison.OrdinalIgnoreCase)) &&
                !IsRunningFromInstallDir(currentExe))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUninstallMode(string[] args)
    {
        return args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "-u", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSilent(string[] args)
    {
        return args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "/verysilent", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "/quiet", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldRelaunchAfterSilentInstall(string[] args)
    {
        return args.Any(a => string.Equals(a, "--relaunch", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "/RESTARTAPPLICATIONS", StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryParseWaitPid(string[] args, out int pid)
    {
        pid = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--wait-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int parsed) && parsed > 0)
            {
                pid = parsed;
                return true;
            }
        }
        return false;
    }

    public static bool TryParseTargetDir(string[] args, out string? targetDir)
    {
        targetDir = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--target-dir", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                targetDir = args[i + 1].Trim().Trim('"');
                return !string.IsNullOrWhiteSpace(targetDir);
            }
        }
        return false;
    }

    public static bool CreateShortcut(string shortcutPath, string targetExePath, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    string dir = Path.GetDirectoryName(shortcutPath) ?? "";
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetExePath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath);
                    shortcut.Description = description;
                    shortcut.Save();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[InstallerService] WScript.Shell shortcut failed: {ex.Message}");
        }

        // PowerShell fallback
        try
        {
            string ps = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{shortcutPath.Replace("'", "''")}');" +
                        $"$s.TargetPath='{targetExePath.Replace("'", "''")}';" +
                        $"$s.WorkingDirectory='{Path.GetDirectoryName(targetExePath)?.Replace("'", "''")}';" +
                        $"$s.Description='{description.Replace("'", "''")}';$s.Save()";

            using var proc = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            proc?.WaitForExit(3000);
            return File.Exists(shortcutPath);
        }
        catch (Exception ex)
        {
            App.Log($"[InstallerService] PowerShell shortcut failed: {ex.Message}");
            return false;
        }
    }

    public static bool Install(
        string? targetDir,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool autoStartOnBoot,
        out string? errorMessage,
        IProgress<(int Percent, string StepMessage)>? progress = null,
        AppSettings? initialSettings = null)
    {
        errorMessage = null;
        try
        {
            progress?.Report((15, "Closing any running Dynamic Island processes..."));
            string resolvedDir = !string.IsNullOrWhiteSpace(targetDir)
                ? targetDir
                : (GetExistingInstallation().Location ?? GetDefaultInstallDir());
            Directory.CreateDirectory(resolvedDir);

            string targetExe = Path.Combine(resolvedDir, "DynamicIsland.exe");
            string backupExe = targetExe + ".bak";
            string? currentExe = Environment.ProcessPath;

            // Stop any existing running instance
            Program.SignalStopOnDefaultDesktop();
            Thread.Sleep(600);

            progress?.Report((40, "Copying application binaries and companion assets..."));
            // Copy executable if not already in target location
            if (!string.IsNullOrEmpty(currentExe) &&
                !string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase))
            {
                bool movedExistingToBackup = false;
                try
                {
                    if (File.Exists(targetExe))
                    {
                        try
                        {
                            File.Move(targetExe, backupExe, overwrite: true);
                            movedExistingToBackup = true;
                        }
                        catch
                        {
                            // Fall through to direct overwrite retries below
                        }
                    }

                    // Retry file copy up to 20 times (6s) in case an exiting process or AV scanner holds a brief lock
                    int retries = 20;
                    while (retries-- > 0)
                    {
                        try
                        {
                            File.Copy(currentExe, targetExe, overwrite: true);
                            break;
                        }
                        catch (IOException) when (retries > 0)
                        {
                            Thread.Sleep(300);
                        }
                        catch (UnauthorizedAccessException) when (retries > 0)
                        {
                            Thread.Sleep(300);
                        }
                    }

                    if (File.Exists(backupExe))
                    {
                        try { File.Delete(backupExe); } catch { }
                    }
                }
                catch
                {
                    if (movedExistingToBackup && !File.Exists(targetExe) && File.Exists(backupExe))
                    {
                        try { File.Move(backupExe, targetExe, overwrite: true); } catch { }
                    }
                    throw;
                }
            }

            progress?.Report((65, "Configuring DirectX GPU power efficiency..."));
            // Configure DirectX Power-Saving iGPU preference (avoids loading discrete NVIDIA driver compiler tables)
            ConfigurePowerSavingGpuPreference(targetExe);

            progress?.Report((80, "Creating Start Menu & Desktop shortcuts..."));
            // Create Start Menu Shortcut
            if (createStartMenuShortcut)
            {
                CreateShortcut(GetStartMenuShortcutPath(), targetExe, "Dynamic Island for Windows");
            }

            // Create Desktop Shortcut
            if (createDesktopShortcut)
            {
                CreateShortcut(GetDesktopShortcutPath(), targetExe, "Dynamic Island for Windows");
            }

            progress?.Report((90, "Applying custom settings and Windows integration..."));
            // Configure Windows Startup
            SetAutoStart(autoStartOnBoot, targetExe);

            // Register in Windows Settings -> Apps & Features
            RegisterUninstallInfo(resolvedDir, targetExe);

            // Provision initial user settings if customized during setup
            if (initialSettings != null)
            {
                try
                {
                    AtomicJsonStore.Save(AppPaths.SettingsFile, initialSettings);
                }
                catch (Exception ex)
                {
                    App.Log($"[InstallerService] Initial settings save notice: {ex.Message}");
                }
            }

            progress?.Report((100, "Dynamic Island installed successfully!"));
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            App.Log($"[InstallerService] Install failed: {ex}");
            return false;
        }
    }

    public static bool Uninstall(bool removeUserData, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            // Close running island window
            Program.SignalStopOnDefaultDesktop();
            Thread.Sleep(600);

            // Remove shortcuts
            try
            {
                string startMenu = GetStartMenuShortcutPath();
                if (File.Exists(startMenu)) File.Delete(startMenu);
            }
            catch (Exception ex) { App.LogDebug("Installer.RemoveStartMenuShortcut", ex); }

            try
            {
                string desktop = GetDesktopShortcutPath();
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch (Exception ex) { App.LogDebug("Installer.RemoveDesktopShortcut", ex); }

            string installDir = GetExistingInstallation().Location ?? GetDefaultInstallDir();

            // Remove startup registration
            SetAutoStart(false, "");
            string targetExe = GetInstalledExePath(installDir);
            RemovePowerSavingGpuPreference(targetExe);

            // Remove Add/Remove Programs registry entry
            UnregisterUninstallInfo();

            // Optionally remove user data (%LocalAppData%\DynamicIsland)
            if (removeUserData)
            {
                try
                {
                    string dataDir = AppPaths.DataDirectory;
                    if (Directory.Exists(dataDir))
                    {
                        Directory.Delete(dataDir, recursive: true);
                    }
                }
                catch (Exception ex) { App.LogDebug("Installer.RemoveUserData", ex); }
            }

            // Schedule post-exit self deletion of installation directory
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 1 /nobreak >nul & rmdir /s /q \"{installDir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex) { App.LogDebug("Installer.ScheduleSelfDelete", ex); }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            App.Log($"[InstallerService] Uninstall failed: {ex}");
            return false;
        }
    }

    public static void SetAutoStart(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (enabled && !string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("DynamicIsland", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("DynamicIsland", throwOnMissingValue: false);
                }
            }

            if (!enabled || string.IsNullOrWhiteSpace(exePath))
            {
                string startupShortcut = GetStartupShortcutPath();
                if (File.Exists(startupShortcut))
                {
                    File.Delete(startupShortcut);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[InstallerService] SetAutoStart failed: {ex.Message}");
        }
    }

    public static void ConfigurePowerSavingGpuPreference(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            key?.SetValue(exePath, "GpuPreference=1;");
            App.Log($"[InstallerService] Pinned {exePath} to iGPU (GpuPreference=1)");
        }
        catch (Exception ex)
        {
            App.Log($"[InstallerService] ConfigurePowerSavingGpuPreference notice: {ex.Message}");
        }
    }

    public static void RemovePowerSavingGpuPreference(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: true);
            key?.DeleteValue(exePath, throwOnMissingValue: false);
        }
        catch (Exception ex) { App.LogDebug("Installer.RemoveGpuPref", ex); }
    }

    public static bool GetAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue("DynamicIsland") != null || File.Exists(GetStartupShortcutPath());
        }
        catch
        {
            return File.Exists(GetStartupShortcutPath());
        }
    }

    public static void RegisterUninstallInfo(string installDir, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland");
            if (key != null)
            {
                key.SetValue("DisplayName", AppDisplayName);
                key.SetValue("DisplayIcon", $"{exePath},0");
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("Publisher", AppPublisher);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall --silent");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                try
                {
                    var fi = new FileInfo(exePath);
                    if (fi.Exists)
                    {
                        key.SetValue("EstimatedSize", (int)(fi.Length / 1024), RegistryValueKind.DWord);
                    }
                }
                catch (Exception sizeEx) { App.LogDebug("Installer.EstimatedSize", sizeEx); }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[InstallerService] RegisterUninstallInfo failed: {ex.Message}");
        }
    }

    public static void UnregisterUninstallInfo()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
            key?.DeleteSubKeyTree("DynamicIsland", throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            App.Log($"[InstallerService] UnregisterUninstallInfo failed: {ex.Message}");
        }
    }
}
