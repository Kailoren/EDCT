using System.IO;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>Loads the read-only structure/commodity build-cost catalog shipped in Data\structure-catalog.json.</summary>
public static class StructureCatalogService
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "structure-catalog.json");

    public static StructureCatalog Load() => JsonDataFile.LoadRequired<StructureCatalog>(FilePath);
}
