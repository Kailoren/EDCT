namespace ColonyTracker.Models;

public sealed record CommodityInfo(string Name, string? Category);

/// <summary>One buildable structure type and how many units of each commodity it costs, keyed by commodity display name.</summary>
public sealed record StructureDefinition(string Name, Dictionary<string, int> Costs);

/// <summary>Static reference data imported once from the community colonisation spreadsheet's Commodities sheet.</summary>
public sealed record StructureCatalog(List<CommodityInfo> Commodities, List<StructureDefinition> Structures);

/// <summary>
/// One planned construction site within a colony project: a body/slot pairing, an optional
/// station name, the structure type to build there, and whether it's currently an active build
/// (i.e. should count toward commodity totals). Mirrors a row of a colony tab in the spreadsheet.
/// </summary>
public sealed class ConstructionSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Body { get; set; }
    public int? SlotNumber { get; set; }
    public string? Name { get; set; }
    public string StructureType { get; set; } = "";

    /// <summary>The specific named layout/architecture the structure was actually built as (the
    /// sheet's "Layout Variant" column) - one of a fixed set of names valid for this slot's
    /// StructureType, per Data/layout-variants.json.</summary>
    public string? StationType { get; set; }

    public string? Notes { get; set; }
    public bool Active { get; set; }

    /// <summary>Manually toggleable, but also auto-set true once this slot's live depot data shows
    /// nothing remaining (see MainViewModel.RebuildUnloadingSites) - never auto-cleared back to
    /// false, so a manual uncheck sticks unless the player unchecks it again after a later delivery
    /// re-triggers the same fully-delivered state.</summary>
    public bool Completed { get; set; }
}

/// <summary>A colonisation project: one star system with a plan of construction slots.</summary>
public sealed class ColonyProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SystemName { get; set; } = "";
    public List<ConstructionSlot> Slots { get; set; } = [];

    /// <summary>Manually-entered personal fleet carrier stock, keyed by CommodityNameMatcher-normalized commodity name.</summary>
    public Dictionary<string, int> CarrierStock { get; set; } = [];
}

/// <summary>Shape of Data\seed-project.json, the one-time import from the user's existing spreadsheet.</summary>
public sealed class SeedProjectFile
{
    public string SystemName { get; set; } = "";
    public List<ConstructionSlot> Slots { get; set; } = [];
    public int LargeShipCapacity { get; set; }
    public int MediumShipCapacity { get; set; }
    public int FleetCarrierCapacity { get; set; }
}

/// <summary>One entry of Data\ravencolonial-buildtypes.json: what a Raven Colonial site's
/// "buildType" code (e.g. "vesta", "dodec") means in this app's own StructureType/StationType terms.
/// StationType is null for the one case ("asteroid") Raven Colonial doesn't disambiguate further.</summary>
public sealed record RavenColonialBuildType(string StructureType, string? StationType);
