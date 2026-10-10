using System.IO;
using System.Text.Json;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>
/// Persists colony projects to Data\projects.json. On a genuinely first run (no projects.json
/// yet), seeds a single project from Data\seed-project.json - the one-time import of the user's
/// existing spreadsheet plan. Every subsequent load/save only touches projects.json; the seed
/// file is never written back to.
/// </summary>
public static class ProjectStore
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "projects.json");
    private static string SeedPath => Path.Combine(AppContext.BaseDirectory, "Data", "seed-project.json");

    /// <summary>True after Load() if projects.json existed but couldn't be parsed - the corrupt
    /// file is preserved alongside (renamed, not overwritten) rather than silently discarded, and
    /// Load() falls back to an empty list rather than losing the file's presence entirely. The
    /// caller should warn the user rather than treating this as a normal fresh install.</summary>
    public static bool LastLoadFailed { get; private set; }

    public static List<ColonyProject> Load()
    {
        LastLoadFailed = false;
        if (!File.Exists(FilePath)) return SeedFromSpreadsheetImport();

        try
        {
            return JsonDataFile.LoadRequired<List<ColonyProject>>(FilePath);
        }
        catch
        {
            LastLoadFailed = true;
            PreserveCorruptFile();
            return [];
        }
    }

    public static void Save(List<ColonyProject> projects)
    {
        try
        {
            JsonDataFile.SaveAtomic(FilePath, projects);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Ship/carrier capacities from the seed file, used to prime AppSettings on a genuinely first run.</summary>
    public static (int LargeShip, int MediumShip, int Carrier)? ReadSeedCapacities()
    {
        if (!File.Exists(SeedPath)) return null;
        try
        {
            var seed = JsonDataFile.LoadRequired<SeedProjectFile>(SeedPath);
            return (seed.LargeShipCapacity, seed.MediumShipCapacity, seed.FleetCarrierCapacity);
        }
        catch
        {
            return null;
        }
    }

    private static List<ColonyProject> SeedFromSpreadsheetImport()
    {
        if (!File.Exists(SeedPath)) return [];
        SeedProjectFile seed;
        try
        {
            seed = JsonDataFile.LoadRequired<SeedProjectFile>(SeedPath);
        }
        catch
        {
            return [];
        }

        var project = new ColonyProject { SystemName = seed.SystemName, Slots = seed.Slots };
        var list = new List<ColonyProject> { project };
        Save(list);
        return list;
    }

    /// <summary>Copies (doesn't move) the corrupt file aside with a timestamp suffix so a future
    /// Save() can proceed normally without permanently destroying whatever was in the bad file -
    /// best effort, a failure here shouldn't block the app from starting.</summary>
    private static void PreserveCorruptFile()
    {
        try
        {
            File.Copy(FilePath, $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch
        {
            // best effort
        }
    }
}
