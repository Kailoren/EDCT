namespace ColonyTracker.Models;

/// <summary>
/// One entry from a ColonisationConstructionDepot event's ResourcesRequired array.
/// InternalName is the raw "$aluminium_name;" token; DisplayName is Name_Localised
/// (falls back to a cleaned-up InternalName if the game ever omits it).
/// </summary>
public sealed record JournalResource(
    string InternalName,
    string DisplayName,
    int RequiredAmount,
    int ProvidedAmount,
    long Payment)
{
    public int Remaining => Math.Max(0, RequiredAmount - ProvidedAmount);
}

/// <summary>
/// Live snapshot of the construction site currently being tracked, rebuilt each time a
/// ColonisationConstructionDepot event is seen in the journal. MarketID is the only stable
/// identifier the event itself carries; StarSystem/StationName are filled in from a matching
/// Docked event seen earlier in the same journal, when available.
/// </summary>
public sealed record ConstructionSiteState(
    long MarketId,
    string? StarSystem,
    string? StationName,
    double ConstructionProgress,
    bool ConstructionComplete,
    bool ConstructionFailed,
    IReadOnlyList<JournalResource> Resources,
    DateTimeOffset UpdatedAt)
{
    public string DisplayLocation =>
        StationName is not null && StarSystem is not null ? $"{StationName} ({StarSystem})"
        : StarSystem ?? $"Market {MarketId}";
}
