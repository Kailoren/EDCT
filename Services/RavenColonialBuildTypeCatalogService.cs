using System.IO;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>Loads the read-only Raven Colonial buildType -> (StructureType, StationType) map
/// shipped in Data\ravencolonial-buildtypes.json.</summary>
public static class RavenColonialBuildTypeCatalogService
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "ravencolonial-buildtypes.json");

    public static IReadOnlyDictionary<string, RavenColonialBuildType> Load() =>
        JsonDataFile.LoadCaseInsensitiveMap<RavenColonialBuildType>(FilePath);
}
