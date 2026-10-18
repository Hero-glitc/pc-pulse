using PCPulse.Models;

namespace PCPulse.Services;

/// <summary>
/// Produces human-readable upgrade recommendations based on the bottleneck for a profile.
/// </summary>
public static class UpgradeAdvisor
{
    public static string Build(SystemProfile profile, UsageProfile usage, HardwareTiers tiers)
    {
        var bottleneck = ProfileEvaluator.GetBottleneck(profile, usage);
        if (bottleneck == null)
            return "Your system is well balanced for this profile. No upgrade is needed right now.";

        // Laptop CPUs and GPUs are soldered to the mainboard, so a "faster part" isn't an option.
        if (profile.IsLaptop && bottleneck.Type is "GPU" or "CPU")
        {
            return $"This looks like a laptop, so the {bottleneck.Type} is soldered in and can't be replaced. " +
                   "To get more performance instead: switch to the High performance power plan, keep it plugged in " +
                   "while gaming, clear dust from the cooling vents, and install the latest graphics driver.";
        }

        return bottleneck.Type switch
        {
            "GPU" => BuildGpuAdvice(profile, usage, tiers),
            "CPU" => BuildCpuAdvice(profile, usage, tiers),
            "RAM" => $"More memory would help {usage}. Aim for 16 GB or more for smooth multitasking; " +
                     $"you currently have {profile.Ram.Details.GetValueOrDefault(\"Capacity\", \"less than needed\")}.",
            "Storage" => $"Upgrading to an NVMe SSD would noticeably speed up {usage} " +
                         "(startup, load times and file access).",
            _ => "Consider upgrading your weakest component."
        };
    }

    private static string BuildGpuAdvice(SystemProfile profile, UsageProfile usage, HardwareTiers tiers)
    {
        var current = tiers.MatchGpu(profile.Gpu.Name);
        int currentScore = current?.Score ?? (int)profile.Gpu.Score;
        var next = tiers.NextGpuUpgrade(currentScore);

        if (next == null)
            return $"Your GPU is the limiting factor for {usage}. It is already near the top of our list — " +
                   "consider a newer-generation card for the biggest gains.";

        // The GPU carries ~50% of the Gaming score and ~25% of the Graphic score.
        double weight = usage == UsageProfile.Gaming ? 0.50 : 0.25;
        int gain = (int)Math.Round((next.Score - currentScore) * weight);

        return $"Upgrading the GPU from {profile.Gpu.Name} to a {next.Name} would raise your {usage} score " +
               $"by about {gain} points (GPU tier {currentScore} \u2192 {next.Score}).";
    }

    private static string BuildCpuAdvice(SystemProfile profile, UsageProfile usage, HardwareTiers tiers)
    {
        var current = tiers.MatchCpu(profile.Cpu.Name);
        int currentScore = current?.Score ?? (int)profile.Cpu.Score;

        if (currentScore >= 80)
            return $"Your CPU is the limiting factor for {usage}, but it is already strong. " +
                   "Check thermals and power settings before spending money.";

        return $"A faster multi-core CPU would improve {usage}. Your current CPU tier is about " +
               $"{currentScore}/100 — a modern 8-core chip would be a large jump.";
    }
}
