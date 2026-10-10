using System.Text.RegularExpressions;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>
/// Parses rows copied (Ctrl+C) from a colonisation planner spreadsheet tab and pasted into
/// NewProjectDialog. Real sheet exports are messy: a title cell and "Planned/Current System
/// Totals" summary rows sit above the real header row, some cells are wrapped in quotes and can
/// contain embedded newlines/delimiters (e.g. a multi-line "External Links" cell), and column
/// names/order vary per sheet (this app's own convention is Body/Slot/Name/StructureType/Notes/
/// Active, but a real tab uses "Facility Construction", "Build Active" and adds "Layout Variant").
/// </summary>
public static class SheetPasteParser
{
    private static readonly Regex PrimarySuffixPattern = new(@"\s*\((?:P|Primary)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>The default community sheet marks a system's first/primary construction site with
    /// a "(Primary)"/"(P)" suffix on its Structure Type/Station Type - stripped before matching
    /// against the catalog so it still resolves to the right entry instead of falling through as
    /// unrecognized (which would leave that slot contributing zero to the aggregated cargo-hauling
    /// totals). The primary site's real costs are randomised and typically higher than a repeat
    /// build of the same type, so the catalog is necessarily just an approximation for this one
    /// slot - same as it is for every slot before the real ColonisationConstructionDepot event is
    /// seen live in-game, which then takes over as the authoritative source anyway.</summary>
    private static string StripPrimarySuffix(string? value) =>
        value is null ? "" : PrimarySuffixPattern.Replace(value.Trim(), "").Trim();

    /// <summary>Reads the system name from cell D1 (row 1, column D) - the default community
    /// sheet's convention for where it's placed. Returns null if that cell is blank, missing, or
    /// the text doesn't parse into at least 4 columns on its first row.</summary>
    public static string? ExtractSystemName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var delimiter = text.Contains('\t') ? '\t' : ',';
        var rows = Tokenize(text, delimiter);
        if (rows.Count == 0 || rows[0].Count < 4) return null;
        var name = rows[0][3].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static (List<ConstructionSlot> Slots, List<string> Warnings) Parse(
        string text, IReadOnlyList<string> knownStructureTypes, IReadOnlyDictionary<string, List<string>> layoutVariantCatalog)
    {
        var slots = new List<ConstructionSlot>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return (slots, warnings);

        var delimiter = text.Contains('\t') ? '\t' : ',';
        var rows = Tokenize(text, delimiter)
            .Select(r => r.Select(c => c.Trim()).ToList())
            .Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
            .ToList();
        if (rows.Count == 0) return (slots, warnings);

        // A real header row always names both Body and Slot columns - title/totals rows above it
        // don't, so searching for that pair (rather than assuming row 0) skips them automatically.
        var headerRowIndex = rows.FindIndex(r =>
            r.Any(c => string.Equals(c, "body", StringComparison.OrdinalIgnoreCase)) &&
            r.Any(c => string.Equals(c, "slot", StringComparison.OrdinalIgnoreCase)));

        int bodyCol, slotCol, nameCol, typeCol, notesCol, activeCol, variantCol;
        int startRow;

        if (headerRowIndex >= 0)
        {
            bodyCol = slotCol = nameCol = typeCol = notesCol = activeCol = variantCol = -1;
            var header = rows[headerRowIndex];
            for (int i = 0; i < header.Count; i++)
            {
                switch (header[i].ToLowerInvariant())
                {
                    case "body": bodyCol = i; break;
                    case "slot" or "slot #" or "slot#" or "slot number": slotCol = i; break;
                    case "name": nameCol = i; break;
                    case "structure type" or "structuretype" or "type" or "facility construction": typeCol = i; break;
                    case "notes": notesCol = i; break;
                    case "active" or "build active": activeCol = i; break;
                    case "station type" or "layout variant": variantCol = i; break;
                }
            }
            startRow = headerRowIndex + 1;
        }
        else
        {
            // No recognizable header - assume a plain data-only paste in the app's own fixed
            // column order (matches Data/seed-project.json, the original one-time sheet import).
            bodyCol = 0; slotCol = 1; nameCol = 2; typeCol = 3; notesCol = 4; activeCol = 5; variantCol = -1;
            startRow = 0;
        }

        for (int i = startRow; i < rows.Count; i++)
        {
            var row = rows[i];
            var body = Cell(row, bodyCol);
            var name = Cell(row, nameCol);
            var typeRaw = Cell(row, typeCol);
            if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(typeRaw))
                continue; // blank spacer row

            var joined = string.Join(" ", row);
            if (joined.Contains("Totals", StringComparison.OrdinalIgnoreCase) || joined.Contains('➔'))
                continue; // a summary row that slipped past header detection (e.g. one placed below the data)

            int? slotNumber = int.TryParse(Cell(row, slotCol), out var n) ? n : null;
            var notes = Cell(row, notesCol);
            var variant = Cell(row, variantCol);
            var activeRaw = Cell(row, activeCol)?.ToLowerInvariant();
            var active = activeRaw is "true" or "yes" or "y" or "1" or "x" or "✓";

            // Try an exact match first - a "(Primary)"-suffixed structure type gets its own real,
            // higher-cost catalog entry where one has been added (e.g. "Orbital - Starport -
            // Coriolis (Primary)"). Only fall back to the stripped/base-type match (an
            // approximation - see StripPrimarySuffix's own doc comment) for a structure type that
            // doesn't have a dedicated Primary entry yet.
            var matchedType = knownStructureTypes.FirstOrDefault(t => string.Equals(t, typeRaw, StringComparison.OrdinalIgnoreCase))
                ?? knownStructureTypes.FirstOrDefault(t => string.Equals(t, StripPrimarySuffix(typeRaw), StringComparison.OrdinalIgnoreCase));
            if (matchedType is null && !string.IsNullOrWhiteSpace(typeRaw))
                warnings.Add($"Row {i + 1}: \"{typeRaw}\" doesn't match a known structure type.");

            string? matchedVariant = null;
            if (!string.IsNullOrWhiteSpace(variant))
            {
                var validVariants = matchedType is not null && layoutVariantCatalog.TryGetValue(matchedType, out var v) ? v : null;
                matchedVariant = validVariants?.FirstOrDefault(x => string.Equals(x, variant, StringComparison.OrdinalIgnoreCase))
                    ?? validVariants?.FirstOrDefault(x => string.Equals(x, StripPrimarySuffix(variant), StringComparison.OrdinalIgnoreCase));
                if (matchedVariant is null)
                    warnings.Add($"Row {i + 1}: \"{variant}\" doesn't match a known Station Type for \"{matchedType ?? typeRaw}\".");
            }

            slots.Add(new ConstructionSlot
            {
                Body = string.IsNullOrWhiteSpace(body) ? null : body,
                SlotNumber = slotNumber,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                StructureType = matchedType ?? typeRaw ?? "",
                StationType = matchedVariant ?? (string.IsNullOrWhiteSpace(variant) ? null : variant),
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                Active = active
            });
        }

        return (slots, warnings);
    }

    private static string? Cell(List<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    /// <summary>Minimal RFC4180-style tokenizer: a field wrapped in "..." can contain the delimiter
    /// and literal newlines (a doubled "" inside it is an escaped quote), so those don't get
    /// mistaken for a delimiter or a row break the way a plain line/split-by-delimiter would.</summary>
    private static List<List<string>> Tokenize(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        bool inQuotes = false;

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < normalized.Length && normalized[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
