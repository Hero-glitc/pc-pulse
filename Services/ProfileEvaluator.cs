using PCPulse.Models;

namespace PCPulse.Services;

public static class ProfileEvaluator
{
    /// <summary>How much each component matters for a profile (0 = not relevant).</summary>
    public static Dictionary<string, double> Weights(UsageProfile profile) => profile switch
    {
        // Gaming: GPU 50%, single-core CPU 25%, RAM 15%, fast storage 10%
        UsageProfile.Gaming => new()
        {
            ["GPU"] = 0.50, ["CPU"] = 0.25, ["RAM"] = 0.15, ["Storage"] = 0.10
        },
        // Graphic work: multi-core CPU 40%, RAM 35%, scratch-disk speed 25%
        UsageProfile.Graphic => new()
        {
            ["CPU"] = 0.40, ["RAM"] = 0.35, ["Storage"] = 0.25, ["GPU"] = 0.0
        },
        // Normal use: disk health 40%, memory 35%, light CPU 25%
        _ => new()
        {
            ["Storage"] = 0.40, ["RAM"] = 0.35, ["CPU"] = 0.25, ["GPU"] = 0.0
        }
    };

    public static void Evaluate(SystemProfile profile, UsageProfile selectedProfile)
    {
        // Reset bottlenecks and stale impact labels.
        foreach (var c in Components(profile))
        {
            c.IsBottleneck = false;
            RemoveImpactKeys(c);
        }

        var weights = Weights(selectedProfile);

        // Only components this profile actually uses can be a bottleneck.
        var relevant = Components(profile)
            .Where(c => weights.GetValueOrDefault(c.Type, 0) > 0)
            .ToList();

        // Show a single "impact" value per relevant component for the current profile.
        foreach (var c in relevant)
            c.Details[$"{selectedProfile} Impact"] = $"{c.Score * weights[c.Type]:F0}%";

        // Bottleneck = weakest relevant component, if it is far below the strongest relevant one.
        var lowest = relevant.OrderBy(c => c.Score).FirstOrDefault();
        var highest = relevant.OrderByDescending(c => c.Score).FirstOrDefault();
        if (lowest != null && highest != null && highest.Score > 0 &&
            lowest.Score < highest.Score * 0.6)
        {
            lowest.IsBottleneck = true;
        }
    }

    public static ComponentInfo? GetBottleneck(SystemProfile profile, UsageProfile selectedProfile)
    {
        var weights = Weights(selectedProfile);
        return Components(profile)
            .Where(c => weights.GetValueOrDefault(c.Type, 0) > 0)
            .FirstOrDefault(c => c.IsBottleneck);
    }

    public static string GetBottleneckMessage(SystemProfile profile, UsageProfile selectedProfile)
    {
        var bottleneck = GetBottleneck(profile, selectedProfile);
        return bottleneck == null
            ? "No significant bottleneck detected. Your system is well balanced for this profile."
            : $"{bottleneck.Name} is limiting your {selectedProfile} performance.";
    }

    private static IEnumerable<ComponentInfo> Components(SystemProfile p)
    {
        yield return p.Cpu;
        yield return p.Gpu;
        yield return p.Ram;
        yield return p.Storage;
    }

    private static void RemoveImpactKeys(ComponentInfo c)
    {
        foreach (var key in c.Details.Keys.Where(k => k.EndsWith(" Impact")).ToList())
            c.Details.Remove(key);
    }
}
