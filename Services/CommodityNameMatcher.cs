namespace ColonyTracker.Services;

/// <summary>
/// Matches commodity names across three differently-formatted sources: the catalog's display
/// names ("Ceramic Composites"), the journal's Name_Localised ("Ceramic Composites"), and
/// Cargo.json's internal tokens ("ceramiccomposites"). Stripping to lowercase alphanumerics
/// makes all three comparable without needing a maintained internal-name lookup table.
/// </summary>
public static class CommodityNameMatcher
{
    /// <summary>A handful of commodities whose real EDDN/game symbol doesn't reduce from the
    /// catalog's display name via simple strip-and-lowercase - confirmed by querying EDCF.Relay's
    /// live data directly (2026-07-28). "H.E. Suits" and "Land Enrichment Systems" are just
    /// differently-worded from their actual symbol; "Muon Imager" -> "mutomimager" looks like a
    /// genuine typo baked into Frontier's own commodity data (it's the only "%imag%" match across
    /// 8.9M rows). Keyed by the generic-normalized (wrong) form, mapping to the real symbol.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hesuits"] = "hazardousenvironmentsuits",
        ["landenrichmentsystems"] = "terrainenrichmentsystems",
        ["muonimager"] = "mutomimager",
    };

    // Real commodity names (catalog display names, journal Name_Localised, Cargo.json tokens) are
    // never anywhere near this long - this only exists to cap the stackalloc below so an
    // oversized/corrupt input (a malformed local Cargo.json entry, a stray journal line) can't
    // trigger an uncatchable StackOverflowException instead of just producing a harmless long key.
    private const int StackallocThreshold = 256;

    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        Span<char> buffer = name.Length <= StackallocThreshold ? stackalloc char[name.Length] : new char[name.Length];
        var count = 0;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                buffer[count++] = char.ToLowerInvariant(c);
        }
        var normalized = new string(buffer[..count]);
        return Aliases.GetValueOrDefault(normalized, normalized);
    }
}
