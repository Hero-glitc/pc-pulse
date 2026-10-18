using System.IO;
using System.Text.Json;
using PCPulse.Models;

namespace PCPulse.Services;

public class RequirementsDatabase
{
    private readonly string _filePath;
    private List<SoftwareRequirement>? _requirements;

    public RequirementsDatabase()
    {
        _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "requirements.json");
    }

    public async Task<List<SoftwareRequirement>> LoadAsync()
    {
        if (_requirements != null) return _requirements;

        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                _requirements = JsonSerializer.Deserialize<List<SoftwareRequirement>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            else
            {
                _requirements = new();
            }
        }
        catch
        {
            _requirements = new();
        }

        return _requirements;
    }

    public async Task<SoftwareRequirement?> FindAsync(string query)
    {
        var all = await LoadAsync();
        var lowerQuery = query.ToLowerInvariant().Trim();

        // Exact match first
        var exact = all.FirstOrDefault(r =>
            r.Name.ToLowerInvariant() == lowerQuery);
        if (exact != null) return exact;

        // Contains match
        var contains = all.FirstOrDefault(r =>
            r.Name.ToLowerInvariant().Contains(lowerQuery));
        if (contains != null) return contains;

        // Fuzzy: match if all query words appear in name
        var words = lowerQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fuzzy = all.FirstOrDefault(r =>
            words.All(w => r.Name.ToLowerInvariant().Contains(w)));
        if (fuzzy != null) return fuzzy;

        return null;
    }

    public async Task<List<SoftwareRequirement>> SearchAsync(string query, int maxResults = 5)
    {
        var all = await LoadAsync();
        var lowerQuery = query.ToLowerInvariant().Trim();

        return all
            .Where(r => r.Name.ToLowerInvariant().Contains(lowerQuery))
            .OrderBy(r => r.Name)
            .Take(maxResults)
            .ToList();
    }

    public RunResult CanRun(SystemProfile profile, SoftwareRequirement req)
    {
        // Simple comparison: check each min requirement against hardware
        bool allMet = true;
        bool someMet = true;

        foreach (var kvp in req.Min)
        {
            var key = kvp.Key.ToLowerInvariant();
            var minVal = ParseSize(kvp.Value);

            switch (key)
            {
                case "ram":
                    int ramGb = ParseRam(profile.Ram.Name);
                    if (ramGb < minVal) { allMet = false; someMet = false; }
                    else if (ramGb < minVal * 1.5) { someMet = true; }
                    break;
                case "gpu":
                    int vramMb = ParseVram(profile.Gpu.Details.GetValueOrDefault("VRAM", "0 MB"));
                    if (vramMb < minVal) { allMet = false; someMet = false; }
                    else if (vramMb < minVal * 2) { someMet = true; }
                    break;
                case "storage":
                    int storageGb = ParseStorage(profile.Storage.Name);
                    if (storageGb < minVal) { allMet = false; someMet = false; }
                    break;
                case "cpu":
                    // CPU comparison is fuzzy — use score threshold
                    if (profile.Cpu.Score < 40) { allMet = false; someMet = false; }
                    break;
            }
        }

        if (allMet) return RunResult.Ready;
        if (someMet) return RunResult.LowSettings;
        return RunResult.Upgrade;
    }

    private int ParseSize(string value)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.EndsWith("GB")) return int.Parse(value.Replace("GB", "").Trim());
        if (value.EndsWith("MB")) return int.Parse(value.Replace("MB", "").Trim()) / 1024;
        if (value.EndsWith("GHZ")) return (int)(double.Parse(value.Replace("GHZ", "").Trim()) * 10);
        return 0;
    }

    private int ParseRam(string ramName)
    {
        var parts = ramName.Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0], out int gb))
            return gb;
        return 0;
    }

    private int ParseVram(string vramStr)
    {
        var parts = vramStr.Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0], out int mb))
            return mb;
        return 0;
    }

    private int ParseStorage(string storageName)
    {
        // Try to find capacity in details instead
        var parts = storageName.Split(' ');
        foreach (var part in parts)
        {
            if (part.EndsWith("GB") && int.TryParse(part.Replace("GB", ""), out int gb))
                return gb;
            if (part.EndsWith("TB") && int.TryParse(part.Replace("TB", ""), out int tb))
                return tb * 1024;
        }
        return 500; // Assume 500GB if unknown
    }
}
