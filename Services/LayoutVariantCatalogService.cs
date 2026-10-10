using System.IO;

namespace ColonyTracker.Services;

/// <summary>Loads the read-only Structure -> valid Layout Variant names map shipped in
/// Data\layout-variants.json, imported from the community sheet's own reference table
/// (Structure/Layout Variant columns, A48:B159).</summary>
public static class LayoutVariantCatalogService
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "layout-variants.json");

    public static IReadOnlyDictionary<string, List<string>> Load() =>
        JsonDataFile.LoadCaseInsensitiveMap<List<string>>(FilePath);
}
