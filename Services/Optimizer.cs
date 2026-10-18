using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PCPulse.Services;

public class Optimizer
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunDisabledKey = @"Software\Microsoft\Windows\CurrentVersion\Run-Disabled";

    private readonly List<string> _safeToKill = new()
    {
        "Teams", "OneDrive", "OneDriveSetup", "Skype", "SkypeApp",
        "Spotify", "SpotifyWebHelper", "Discord", "DiscordCanary", "Steam",
        "EpicGamesLauncher", "Origin", "BattleNet", "GOGGalaxy",
        "AdobeCreativeCloud", "AdobeNotificationClient", "GoogleUpdate",
        "Cortana", "SearchIndexer", "YourPhone", "GameBar", "GameBarPresenceWriter",
        "Widgets", "WidgetService"
    };

    private readonly List<string> _neverKill = new()
    {
        "explorer", "svchost", "csrss", "wininit", "winlogon", "lsass",
        "services", "smss", "dwm", "PCPulse", "MsMpEng", "SecurityHealthService",
        "SecurityHealthSystray", "NisSrv", "WdNisSvc", "WinDefend", "Sense",
        "Registry", "System", "Idle", "Memory Compression", "fontdrvhost", "ctfmon"
    };

    private readonly string _logPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCPulse", "optimization.log");

    /// <summary>Builds a report of what would change, without changing anything.</summary>
    public OptimizationPreview BuildPreview()
    {
        var preview = new OptimizationPreview();

        // Background processes that can be closed
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (_neverKill.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (_safeToKill.Contains(name, StringComparer.OrdinalIgnoreCase))
                        preview.ProcessesToClose.Add(name);
                }
                catch { }
            }
        }
        catch { }
        preview.ProcessesToClose = preview.ProcessesToClose.Distinct().OrderBy(x => x).ToList();

        // Startup apps that would be disabled
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString() ?? "";
                    if (!value.Contains("Microsoft") && !value.Contains("Windows"))
                        preview.StartupAppsToDisable.Add(valueName);
                }
            }
        }
        catch { }

        // Temp file estimate
        preview.TempFilesFound = EstimateTempFiles();

        return preview;
    }

    public OptimizationResult OptimizeForNormalUse()
    {
        var result = new OptimizationResult
        {
            TempFilesCleared = ClearTempFiles(),
            StartupAppsDisabled = DisableStartupApps(),
            ProcessesKilled = KillSafeProcesses(),
            Timestamp = DateTime.Now,
            Success = true
        };

        LogOptimization(result);
        return result;
    }

    /// <summary>Restores startup apps that were previously disabled.</summary>
    public int RestoreStartupApps()
    {
        int restored = 0;
        try
        {
            using var disabledKey = Registry.CurrentUser.OpenSubKey(RunDisabledKey, false);
            if (disabledKey == null) return 0;

            using var runKey = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            foreach (var valueName in disabledKey.GetValueNames().ToArray())
            {
                var value = disabledKey.GetValue(valueName);
                if (value != null)
                {
                    runKey?.SetValue(valueName, value);
                    disabledKey.DeleteValue(valueName);
                    restored++;
                }
            }
        }
        catch { }
        return restored;
    }

    private int EstimateTempFiles()
    {
        int count = 0;
        foreach (var path in TempPaths())
        {
            try
            {
                if (Directory.Exists(path))
                    count += Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
            }
            catch { }
        }
        return count;
    }

    private static string[] TempPaths() => new[]
    {
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
    };

    private int ClearTempFiles()
    {
        int cleared = 0;
        foreach (var path in TempPaths())
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        cleared++;
                    }
                    catch { }
                }
            }
            catch { }
        }
        return cleared;
    }

    private int DisableStartupApps()
    {
        int disabled = 0;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames().ToArray())
                {
                    var value = key.GetValue(valueName)?.ToString() ?? "";
                    if (!value.Contains("Microsoft") && !value.Contains("Windows"))
                    {
                        using var disabledKey = Registry.CurrentUser.OpenSubKey(RunDisabledKey, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunDisabledKey);
                        disabledKey?.SetValue(valueName, value);
                        key.DeleteValue(valueName);
                        disabled++;
                    }
                }
            }
        }
        catch { }
        return disabled;
    }

    private int KillSafeProcesses()
    {
        int killed = 0;
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (_neverKill.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (_safeToKill.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        proc.Kill();
                        proc.WaitForExit(5000);
                        killed++;
                    }
                }
                catch { }
            }
        }
        catch { }
        return killed;
    }

    private void LogOptimization(OptimizationResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath,
                $"[{result.Timestamp:yyyy-MM-dd HH:mm:ss}] " +
                $"TempFiles={result.TempFilesCleared}, " +
                $"StartupDisabled={result.StartupAppsDisabled}, " +
                $"ProcessesKilled={result.ProcessesKilled}{Environment.NewLine}");
        }
        catch { }
    }
}

public record OptimizationResult
{
    public bool Success { get; set; }
    public int TempFilesCleared { get; set; }
    public int StartupAppsDisabled { get; set; }
    public int ProcessesKilled { get; set; }
    public DateTime Timestamp { get; set; }
}

public class OptimizationPreview
{
    public List<string> ProcessesToClose { get; set; } = new();
    public List<string> StartupAppsToDisable { get; set; } = new();
    public int TempFilesFound { get; set; }

    public bool HasAnythingToDo =>
        ProcessesToClose.Count > 0 || StartupAppsToDisable.Count > 0 || TempFilesFound > 0;
}
