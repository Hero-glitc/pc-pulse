using System.IO;
using System.Text.Json;
using PCPulse.Models;

namespace PCPulse.Services;

/// <summary>
/// Loads known hardware tiers and maps scanned component names to a performance score.
/// </summary>
public class HardwareTiers
{
    private TierData _data = new();

    private class TierData
    {
        public List<HardwareTier> Gpus { get; set; } = new();
        public List<HardwareTier> Cpus { get; set; } = new();
    }

    public void Load()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "hardware-tiers.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            _data = JsonSerializer.Deserialize<TierData>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new TierData();
        }
        catch { /* tiers are optional */ }
    }

    public HardwareTier? MatchGpu(string name) => Match(_data.Gpus, name);
    public HardwareTier? MatchCpu(string name) => Match(_data.Cpus, name);

    private static HardwareTier? Match(List<HardwareTier> tiers, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var upper = name.ToUpperInvariant();
        // First pass: exact/specific match tokens (order in JSON is most-specific first).
        foreach (var tier in tiers)
        {
            foreach (var token in tier.Match)
            {
                if (upper.Contains(token.ToUpperInvariant()))
                    return tier;
            }
        }
        return null;
    }

    /// <summary>Returns the cheapest tier that is meaningfully better than the current score.</summary>
    public HardwareTier? NextGpuUpgrade(double currentScore)
    {
        return _data.Gpus
            .Where(t => t.Score >= currentScore + 12)
            .OrderBy(t => t.Score)
            .FirstOrDefault();
    }
}
