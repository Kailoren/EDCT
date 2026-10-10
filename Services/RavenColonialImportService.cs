using System.Text.Json;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>
/// Parses a Raven Colonial system export (JSON downloaded from ravencolonial.com) into a
/// ColonyProject. Raven Colonial's own site data has no Body/Slot/Notes columns like the
/// community sheet - it identifies a build by (bodyNum, buildType, status) plus the player's
/// custom site name, so those are derived: Body from resolving bodyNum against the export's own
/// bodies[] list and stripping the system name prefix, Slot # by numbering builds within the same
/// body in the order Raven Colonial lists them (bodyNum can repeat - one body may host several
/// builds), and StructureType/StationType via Data\ravencolonial-buildtypes.json.
/// </summary>
public static class RavenColonialImportService
{
    public static (string SystemName, List<ConstructionSlot> Slots, List<string> Warnings) Parse(
        string json, IReadOnlyDictionary<string, RavenColonialBuildType> buildTypeCatalog)
    {
        var warnings = new List<string>();
        var slots = new List<ConstructionSlot>();

        RcExport? export;
        try
        {
            export = JsonSerializer.Deserialize<RcExport>(json, JsonDataFile.ReadOptions);
        }
        catch (JsonException ex)
        {
            return ("", slots, [$"Couldn't parse the file as JSON: {ex.Message}"]);
        }

        if (export?.Sites is null || export.Sites.Count == 0)
            return (export?.Name ?? "", slots, ["No sites found - this doesn't look like a Raven Colonial system export."]);

        // Indexer assignment (not ToDictionary) so a duplicate bodyNum in a malformed/hand-edited
        // export overwrites rather than throwing - last one wins instead of crashing the import.
        var bodyNames = new Dictionary<int, string>();
        if (export.Bodies is not null)
            foreach (var b in export.Bodies)
                bodyNames[b.Num] = b.Name;
        var namePrefix = export.Name + " ";
        var slotNumbersByBody = new Dictionary<int, int>();

        foreach (var site in export.Sites)
        {
            var bodyLabel = bodyNames.TryGetValue(site.BodyNum, out var fullName)
                ? (fullName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) ? fullName[namePrefix.Length..] : fullName)
                : null;

            var slotNumber = slotNumbersByBody[site.BodyNum] = slotNumbersByBody.GetValueOrDefault(site.BodyNum) + 1;

            string structureType;
            string? stationType = null;
            if (site.BuildType is not null && buildTypeCatalog.TryGetValue(site.BuildType, out var buildType))
            {
                structureType = buildType.StructureType;
                stationType = buildType.StationType;
            }
            else
            {
                structureType = site.BuildType ?? "";
                warnings.Add($"Site \"{site.Name}\": build type \"{site.BuildType}\" isn't recognized - kept as typed.");
            }

            slots.Add(new ConstructionSlot
            {
                Body = bodyLabel,
                SlotNumber = slotNumber,
                Name = string.IsNullOrWhiteSpace(site.Name) ? null : site.Name,
                StructureType = structureType,
                StationType = stationType,
                Active = string.Equals(site.Status, "build", StringComparison.OrdinalIgnoreCase)
            });
        }

        return (export.Name, slots, warnings);
    }

    private sealed class RcExport
    {
        public string Name { get; set; } = "";
        public List<RcBody>? Bodies { get; set; }
        public List<RcSite>? Sites { get; set; }
    }

    private sealed class RcBody
    {
        public string Name { get; set; } = "";
        public int Num { get; set; }
    }

    private sealed class RcSite
    {
        public string? Name { get; set; }
        public int BodyNum { get; set; }
        public string? BuildType { get; set; }
        public string? Status { get; set; }
    }
}
