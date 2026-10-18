namespace PCPulse.Models;

public record ComponentInfo
{
    public required string Name { get; init; }
    public required string Type { get; init; } // "CPU", "GPU", "RAM", "Storage"
    public double Score { get; set; } = 50; // 0-100
    public Dictionary<string, string> Details { get; init; } = new();
    public bool IsBottleneck { get; set; }
    public string? Tooltip { get; set; }
}

public record SystemProfile
{
    public required ComponentInfo Cpu { get; init; }
    public required ComponentInfo Gpu { get; init; }
    public ComponentInfo? IntegratedGpu { get; init; }
    public required ComponentInfo Ram { get; init; }
    public required ComponentInfo Storage { get; init; }
    public DateTime ScannedAt { get; init; } = DateTime.Now;
    public bool IsAdmin { get; init; }
    public string PowerPlan { get; init; } = "Unknown";
    public bool OnAcPower { get; init; } = true;
    public bool IsLaptop { get; init; }
    public string? UpgradeAdvice { get; set; }
}

public enum UsageProfile
{
    Gaming,
    Graphic,
    Normal
}

public record SoftwareRequirement
{
    public required string Name { get; init; }
    public required string Type { get; init; } // "game" or "app"
    public required Dictionary<string, string> Min { get; init; }
    public required Dictionary<string, string> Rec { get; init; }
}

public enum RunResult
{
    Ready,      // Green
    LowSettings, // Yellow
    Upgrade      // Red
}

/// <summary>Live, real-time system usage sampled while the app is open.</summary>
public record LiveStats
{
    public double CpuUsagePercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double RamUsagePercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100 : 0;
    public double DiskActivityPercent { get; init; }
}

/// <summary>A single hardware tier used for upgrade recommendations.</summary>
public record HardwareTier
{
    public required string Name { get; init; }
    public required int Score { get; init; }
    public string[] Match { get; init; } = Array.Empty<string>();
}
