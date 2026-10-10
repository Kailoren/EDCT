namespace ColonyTracker.Models;

/// <summary>A star system with the specific stations there to check for hauling stock/prices.</summary>
public sealed class HubSystem
{
    public string SystemName { get; set; } = "";
    public List<string> Stations { get; set; } = [];

    /// <summary>Surface settlements at this system, kept separate from Stations (orbital) so the
    /// hauling grid/overlay can show the right icon - additive to an existing settings.json
    /// (defaults to empty), not a breaking change to the Stations shape.</summary>
    public List<string> PlanetaryStations { get; set; } = [];
}

/// <summary>One (commodity, station) buy listing pulled from the relay - only what the hub-priority ranking needs.</summary>
public sealed record CommodityListing(string Commodity, string StationName, string StarSystem, int BuyPrice, int Stock);

/// <summary>One /hub-listings read. RelayBusy means the relay turned the request away for coming
/// too often (HTTP 429): Listings is then empty but says nothing about what the hubs stock.</summary>
public sealed record HubListingsRead(IReadOnlyList<CommodityListing> Listings, bool RelayBusy);
