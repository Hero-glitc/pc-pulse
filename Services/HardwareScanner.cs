using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using PCPulse.Models;

namespace PCPulse.Services;

public class HardwareScanner
{
    private readonly HardwareTiers _tiers;
    private readonly bool _isAdmin;

    public HardwareScanner(HardwareTiers tiers, int timeoutMs = 5000)
    {
        _tiers = tiers;
        _isAdmin = IsRunningAsAdmin();
    }

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public SystemProfile Scan()
    {
        var cpuTask = Task.Run(ScanCpu);
        var gpusTask = Task.Run(ScanGpus);
        var ramTask = Task.Run(ScanRam);
        var storageTask = Task.Run(ScanStorage);

        var powerInfo = GetPowerInfo();

        Task.WaitAll(cpuTask, gpusTask, ramTask, storageTask);

        var gpus = gpusTask.Result;
        var primary = gpus.OrderByDescending(g => g.Score).FirstOrDefault()
                      ?? new ComponentInfo { Name = "Unknown GPU", Type = "GPU" };
        var integrated = gpus.Count > 1 ? gpus.OrderBy(g => g.Score).First() : null;

        return new SystemProfile
        {
            Cpu = cpuTask.Result,
            Gpu = primary,
            IntegratedGpu = integrated,
            Ram = ramTask.Result,
            Storage = storageTask.Result,
            IsAdmin = _isAdmin,
            PowerPlan = powerInfo.powerPlan,
            OnAcPower = powerInfo.onAc,
            IsLaptop = DetectLaptop()
        };
    }

    // ---------------- CPU ----------------
    private ComponentInfo ScanCpu()
    {
        var info = new ComponentInfo { Name = "Unknown CPU", Type = "CPU" };
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                info = new ComponentInfo
                {
                    Name = name,
                    Type = "CPU",
                    Details = new Dictionary<string, string>
                    {
                        ["Cores"] = obj["NumberOfCores"]?.ToString() ?? "?",
                        ["Threads"] = obj["NumberOfLogicalProcessors"]?.ToString() ?? "?",
                        ["MaxClock"] = $"{obj["MaxClockSpeed"]} MHz",
                        ["Architecture"] = obj["Architecture"]?.ToString() ?? "?"
                    }
                };
                break;
            }
        }
        catch { /* WMI blocked or timed out */ }

        // Prefer the known-tier score, fall back to a heuristic.
        var tier = _tiers.MatchCpu(info.Name);
        if (tier != null)
        {
            info.Score = tier.Score;
        }
        else
        {
            try
            {
                if (info.Details.TryGetValue("Cores", out var coresStr) &&
                    info.Details.TryGetValue("MaxClock", out var clockStr))
                {
                    int cores = int.Parse(coresStr);
                    int clock = int.Parse(clockStr.Replace(" MHz", "").Trim());
                    info.Score = Math.Min(100, (cores * clock) / 400.0 * 10);
                }
            }
            catch { }
        }

        info.Tooltip = "Higher score = faster processing. Gaming depends most on single-core speed; " +
                       "Graphic work depends most on core count.";
        return info;
    }

    // ---------------- GPU (all adapters) ----------------
    private List<ComponentInfo> ScanGpus()
    {
        var list = new List<ComponentInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) continue;

                var vramBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                var vram = GetVramFromRegistry(name) ?? (vramBytes > 0 ? vramBytes : 0);

                var info = new ComponentInfo
                {
                    Name = name,
                    Type = "GPU",
                    Details = new Dictionary<string, string>
                    {
                        ["VRAM"] = $"{vram / (1024 * 1024)} MB",
                        ["DriverVersion"] = obj["DriverVersion"]?.ToString() ?? "?"
                    }
                };

                var tier = _tiers.MatchGpu(name);
                double score = tier?.Score ?? Math.Min(100, (vram / (1024.0 * 1024 * 1024)) * 10);

                // Small VRAM bonus/penalty so two equal-tier cards still differ.
                if (vram > 0)
                {
                    int vramMb = (int)(vram / (1024 * 1024));
                    score += Math.Min(6, vramMb / 2048.0);
                }

                info.Score = Math.Clamp(score, 1, 100);
                info.Tooltip = "The GPU does the heavy lifting for Gaming and video/animation work.";
                list.Add(info);
            }
        }
        catch { /* WMI blocked or timed out */ }

        // Resolution comes from the display settings, not the adapter.
        var (width, height, refresh) = GetDisplayMode();
        if (width > 0 && list.Count > 0)
        {
            list[0].Details["Resolution"] = $"{width}x{height} @ {refresh}Hz";
        }

        if (list.Count == 0)
            list.Add(new ComponentInfo { Name = "Unknown GPU", Type = "GPU" });

        return list;
    }

    private long? GetVramFromRegistry(string gpuName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (key == null) return null;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                var desc = subKey?.GetValue("DriverDesc") as string;
                if (desc != null && gpuName.Length > 0 && desc.Contains(gpuName.Split(' ')[0]))
                {
                    var vram = subKey?.GetValue("HardwareInformation.qwMemorySize") as long?;
                    if (vram.HasValue && vram.Value > 0)
                        return vram.Value;

                    var vramOld = subKey?.GetValue("HardwareInformation.MemorySize") as int?;
                    if (vramOld.HasValue && vramOld.Value > 0)
                        return (uint)vramOld.Value;
                }
            }
        }
        catch { /* registry access denied */ }

        return null;
    }

    // ---------------- RAM ----------------
    private ComponentInfo ScanRam()
    {
        var info = new ComponentInfo { Name = "System RAM", Type = "RAM" };
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            long totalBytes = 0;
            var speeds = new List<string>();
            var types = new List<string>();

            foreach (ManagementObject obj in searcher.Get())
            {
                totalBytes += Convert.ToInt64(obj["Capacity"] ?? 0);
                var speed = obj["Speed"]?.ToString();
                if (speed != null) speeds.Add(speed);
                var type = obj["SMBIOSMemoryType"]?.ToString();
                if (!string.IsNullOrEmpty(type)) types.Add(type);
            }

            int totalGb = (int)(totalBytes / (1024 * 1024 * 1024));
            var speedStr = speeds.Count > 0 ? speeds[0] : "Unknown";

            info = new ComponentInfo
            {
                Name = $"{totalGb} GB RAM",
                Type = "RAM",
                Details = new Dictionary<string, string>
                {
                    ["Capacity"] = $"{totalGb} GB",
                    ["Speed"] = $"{speedStr} MHz",
                    ["Sticks"] = speeds.Count.ToString()
                },
                Score = Math.Min(100, totalGb * 6.25) // 16GB = 100, 8GB = 50
            };
        }
        catch { /* WMI blocked or timed out */ }

        info.Tooltip = "More RAM keeps many apps open smoothly. 8 GB is entry level, 16 GB is comfortable, 32 GB is future-proof.";
        return info;
    }

    // ---------------- Storage ----------------
    private ComponentInfo ScanStorage()
    {
        var info = new ComponentInfo { Name = "Unknown Storage", Type = "Storage" };
        try
        {
            // Preferred source: MSFT_PhysicalDisk exposes a real MediaType.
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, MediaType, Size, HealthStatus FROM MSFT_PhysicalDisk");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["FriendlyName"]?.ToString()?.Trim() ?? "Unknown";
                var sizeBytes = Convert.ToInt64(obj["Size"] ?? 0);
                var sizeGb = (int)(sizeBytes / (1024 * 1024 * 1024));
                int mediaType = obj["MediaType"] != null ? Convert.ToInt32(obj["MediaType"]) : 0;
                int health = obj["HealthStatus"] != null ? Convert.ToInt32(obj["HealthStatus"]) : 0;

                var type = mediaType switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => GuessTypeFromName(name)
                };

                info = new ComponentInfo
                {
                    Name = name,
                    Type = "Storage",
                    Details = new Dictionary<string, string>
                    {
                        ["Capacity"] = $"{sizeGb} GB",
                        ["Type"] = type,
                        ["Health"] = health switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" }
                    },
                    Score = type == "SSD" ? 90 : (type == "SCM" ? 100 : 40)
                };
                break;
            }
        }
        catch
        {
            info = FallbackStorageScan();
        }

        AddSmartHealth(info);

        info.Tooltip = "SSDs are far faster than HDDs for startup and load times. " +
                       "A healthy SSD keeps your whole PC feeling responsive.";
        return info;
    }

    private static string GuessTypeFromName(string name)
    {
        var upper = name.ToUpperInvariant();
        // Explicit markers first.
        if (upper.Contains("NVME") || upper.Contains("SSD") || upper.Contains("M.2")) return "SSD";
        // Known SSD model lines (covers drives that don't say "SSD").
        string[] ssdMarkers =
        {
            "MICRON 1100", "MICRON 1300", "CRUCIAL", "SAMSUNG SSD", "860 EVO", "870 EVO", "850 EVO",
            "960 EVO", "970 EVO", "980", "990", "WD BLUE", "WD BLACK", "SN5", "SN7", "SN8",
            "KINGSTON", "SANDISK", "BARRACUDA SSD", "INTEL SSD", "SK HYNIX", "KIOXIA", "SOLID STATE"
        };
        if (ssdMarkers.Any(m => upper.Contains(m))) return "SSD";
        return "HDD";
    }

    private static ComponentInfo FallbackStorageScan()
    {
        var info = new ComponentInfo { Name = "Unknown Storage", Type = "Storage" };
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Model"]?.ToString()?.Trim() ?? "Unknown";
                var sizeGb = (int)(Convert.ToInt64(obj["Size"] ?? 0) / (1024 * 1024 * 1024));
                var type = GuessTypeFromName(name);
                info = new ComponentInfo
                {
                    Name = name,
                    Type = "Storage",
                    Details = new Dictionary<string, string>
                    {
                        ["Capacity"] = $"{sizeGb} GB",
                        ["Type"] = type
                    },
                    Score = type == "SSD" ? 90 : 40
                };
                break;
            }
        }
        catch { }
        return info;
    }

    private void AddSmartHealth(ComponentInfo info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT Temperature, PowerOnHours, Wear FROM MSFT_StorageReliabilityCounter");
            foreach (ManagementObject obj in searcher.Get())
            {
                var temp = obj["Temperature"];
                var hours = obj["PowerOnHours"];
                var wear = obj["Wear"];

                if (temp != null) info.Details["Temperature"] = $"{temp} C";
                if (hours != null) info.Details["PowerOnHours"] = hours.ToString()!;
                if (wear != null) info.Details["Wear"] = $"{wear}%";
                break;
            }
        }
        catch { /* SMART/reliability data usually needs admin — skip quietly */ }
    }

    // ---------------- Form factor ----------------
    /// <summary>
    /// True when the machine is a laptop/tablet. Used to avoid suggesting CPU/GPU upgrades,
    /// which are soldered to the mainboard on portable machines.
    /// </summary>
    private static bool DetectLaptop()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["ChassisTypes"] is not ushort[] types) continue;
                // 8=Portable 9=Laptop 10=Notebook 11=Handheld 14=Sub Notebook 30=Tablet
                // 31=Convertible 32=Detachable
                if (types.Any(t => t is 8 or 9 or 10 or 11 or 14 or 30 or 31 or 32))
                    return true;
            }
        }
        catch { }

        // Fallback: if the system reports a battery, treat it as a portable device.
        try
        {
            if (GetSystemPowerStatus(out var status) &&
                status.BatteryFlag != 128 && status.BatteryFlag != 255)
                return true;
        }
        catch { }

        return false;
    }

    // ---------------- Power ----------------
    private (string powerPlan, bool onAc) GetPowerInfo()
    {
        string plan = "Unknown";
        bool onAc = true;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
            var guid = key?.GetValue("ActivePowerScheme")?.ToString()?.ToUpperInvariant();
            plan = guid switch
            {
                "381B4222-F694-41F0-9685-FF5BB260DF2E" => "Balanced",
                "8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C" => "High performance",
                "A1841308-3541-4FAB-BC81-F71556F20B4A" => "Power saver",
                null => "Unknown",
                _ => "Custom"
            };
        }
        catch { }

        try
        {
            if (GetSystemPowerStatus(out var status))
                onAc = status.ACLineStatus == 1;
        }
        catch { }

        return (plan, onAc);
    }

    // ---------------- Display ----------------
    private static (int width, int height, int refresh) GetDisplayMode()
    {
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                return (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
        }
        catch { }
        return (0, 0, 0);
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
