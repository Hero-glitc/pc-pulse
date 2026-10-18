using System.Management;
using System.Runtime.InteropServices;
using PCPulse.Models;

namespace PCPulse.Services;

/// <summary>
/// Samples real-time system usage (CPU, RAM, disk) using WMI + a Win32 memory call.
/// </summary>
public class LiveMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public LiveStats Sample()
    {
        double cpu = SampleCpu();
        double disk = SampleDisk();
        (double usedGb, double totalGb) = SampleRam();

        return new LiveStats
        {
            CpuUsagePercent = cpu,
            DiskActivityPercent = disk,
            RamUsedGb = usedGb,
            RamTotalGb = totalGb
        };
    }

    private double SampleCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
            foreach (ManagementObject obj in searcher.Get())
                return Convert.ToDouble(obj["PercentProcessorTime"] ?? 0);
        }
        catch { }
        return 0;
    }

    private double SampleDisk()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PercentDiskTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
            foreach (ManagementObject obj in searcher.Get())
                return Math.Min(100, Convert.ToDouble(obj["PercentDiskTime"] ?? 0));
        }
        catch { }
        return 0;
    }

    private (double usedGb, double totalGb) SampleRam()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double total = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
                double avail = mem.ullAvailPhys / (1024.0 * 1024 * 1024);
                return (total - avail, total);
            }
        }
        catch { }
        return (0, 0);
    }
}
