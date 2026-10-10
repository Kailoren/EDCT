namespace ColonyTracker.Models;

/// <summary>
/// Persisted app configuration. Saved to Data\settings.json next to the exe. Cargo capacities
/// seed from the user's spreadsheet on first run and are editable afterwards (fleet carriers in
/// particular can be reconfigured with different cargo bay counts).
/// </summary>
public sealed class AppSettings
{
    public int LargeShipCapacity { get; set; } = 794;
    public int MediumShipCapacity { get; set; } = 400;
    public int FleetCarrierCapacity { get; set; } = 25000;

    /// <summary>Overrides auto-detection of the Saved Games\...\Elite Dangerous journal folder.</summary>
    public string? JournalDirectoryOverride { get; set; }

    /// <summary>Where CarrierCargoTracker's journal replay left off as of the last clean exit
    /// (after that session's auto-detected transfers were baked into each project's CarrierStock
    /// baseline). Lets the next launch resume from this exact point instead of replaying the whole
    /// file again, which would otherwise re-count and double the same transfers if Elite Dangerous
    /// is still writing the same journal file across the restart. Null/0 until the first clean
    /// exit after this feature shipped.</summary>
    public string? CarrierTrackerJournalFile { get; set; }
    public long CarrierTrackerJournalOffset { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>DataGrid column widths, keyed by column x:Name (e.g. "LayoutBodyColumn").</summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = [];

    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double OverlayWidth { get; set; } = 260;
    public double OverlayHeight { get; set; } = 360;
    public double OverlayScale { get; set; } = 1.0;

    /// <summary>Click-through (mouse events pass to whatever's underneath, e.g. the game) - toggled from the main window, not the overlay itself, since a locked overlay can't be clicked.</summary>
    public bool OverlayLocked { get; set; }

    /// <summary>Fraction (0.2-1.0, enforced by the Settings slider) of the background layer's own
    /// alpha - NOT the whole window's opacity, so text/buttons/border stay fully crisp regardless
    /// of how see-through the background panel is. Default 0.9 matches the original hardcoded
    /// background's own baked-in alpha (0xE6 of 0xFF ~= 90%), so an existing settings.json with
    /// none of these keys yet renders identically to before.</summary>
    public double OverlayOpacity { get; set; } = 0.9;

    // Overlay appearance - independently customizable from the rest of the app's fixed theme,
    // since the overlay sits over the game and users may want it to stand out differently (or
    // blend in more) than the main window does. Defaults match the app's original hardcoded
    // overlay colors, so an existing settings.json with none of these keys yet renders identically
    // to before. All stored as opaque RGB hex - OverlayOpacity above is what actually controls the
    // background layer's transparency, not any alpha baked into these strings.
    public string OverlayBackgroundColor { get; set; } = "#0A0705";
    public string OverlayBorderColor { get; set; } = "#FF7500";
    public string OverlayHeaderColor { get; set; } = "#FF7500";
    public string OverlayTextColor { get; set; } = "#EAE4DA";
    public string OverlayMutedColor { get; set; } = "#9C7A55";
    public string OverlayButtonColor { get; set; } = "#FF7500";
    /// <summary>The Loading/Unloading toggle's non-selected fill - was hardcoded to the app-wide
    /// Border brown before this was made customizable.</summary>
    public string OverlayInactiveButtonColor { get; set; } = "#5A3A15";
    public string OverlayButtonTextColor { get; set; } = "#000000";

    /// <summary>Global (system-wide, via Win32 RegisterHotKey) keybinds so the overlay can be
    /// shown/hidden, locked/unlocked, and switched between Loading/Unloading without alt-tabbing
    /// out of the game. Null means unbound - shipped unbound by default rather than pre-claiming a
    /// combo the user never asked for.</summary>
    public HotkeyBinding? OverlayToggleHotkey { get; set; }
    public HotkeyBinding? OverlayLockToggleHotkey { get; set; }
    public HotkeyBinding? OverlayModeToggleHotkey { get; set; }

    /// <summary>EDCF.Relay's existing /listings endpoint (already EDDN-fed with per-station buy price/stock) - reused rather than standing up a new data source.</summary>
    public string RelayBaseUrl { get; set; } = "https://edcf-relay.77-42-73-218.sslip.io";

    /// <summary>Stations to check for buy price/stock when ranking where to load each commodity. Primary hub first, backup system(s) after.</summary>
    public List<HubSystem> HubSystems { get; set; } =
    [
        new HubSystem
        {
            SystemName = "Col 285 Sector RC-B b14-3",
            Stations = ["Minippisch", "Cannon Beacon"],
            PlanetaryStations = ["Dunlop Survey", "Thorn Nook", "Holloway Territories"]
        },
        new HubSystem
        {
            SystemName = "LTT 1873",
            Stations = ["Lippisch Hub", "Celsius Gateway", "Hadid City"],
            PlanetaryStations = ["Morgan Base"]
        },
        // Added specifically for Combat Stabilisers/Battle Weapons, which had no listings anywhere
        // among the other tracked stations.
        new HubSystem
        {
            SystemName = "Col 285 Sector TS-A b14-2",
            Stations = ["Akiyama Horizons"]
        }
    ];
}
