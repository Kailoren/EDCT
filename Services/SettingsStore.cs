using System.IO;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>Persists AppSettings to Data\settings.json next to the exe.</summary>
public static class SettingsStore
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");

    /// <summary>True after Load() if settings.json existed but couldn't be parsed - see
    /// ProjectStore.LastLoadFailed for the same pattern and why it exists.</summary>
    public static bool LastLoadFailed { get; private set; }

    public static AppSettings Load()
    {
        LastLoadFailed = false;
        if (!File.Exists(FilePath))
            return NewWithSeedCapacities();

        try
        {
            return JsonDataFile.LoadRequired<AppSettings>(FilePath);
        }
        catch
        {
            LastLoadFailed = true;
            PreserveCorruptFile();
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            JsonDataFile.SaveAtomic(FilePath, settings);
        }
        catch
        {
            // best effort
        }
    }

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

    private static AppSettings NewWithSeedCapacities()
    {
        var settings = new AppSettings();
        if (ProjectStore.ReadSeedCapacities() is { } seed)
        {
            settings.LargeShipCapacity = seed.LargeShip;
            settings.MediumShipCapacity = seed.MediumShip;
            settings.FleetCarrierCapacity = seed.Carrier;
        }
        return settings;
    }
}
