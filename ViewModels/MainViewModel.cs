using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using ColonyTracker.Models;
using ColonyTracker.Services;

namespace ColonyTracker.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly StructureCatalog _catalog;
    private readonly IReadOnlyDictionary<string, List<string>> _layoutVariantCatalog;
    private readonly ConstructionTrackerService _tracker;
    private readonly ShipCargoReader _cargoReader;
    private readonly CarrierCargoTracker _carrierCargoTracker;
    private readonly RelayCommodityService _relayService = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, CommodityRowViewModel> _rowsByCommodity = new();
    private bool _relayRefreshInFlight;
    private bool _relayRefreshPending;

    // Set while AutoCompleteFinishedSites ticks Completed on several rows at once, so each row's
    // change callback doesn't run its own full recompute; the caller does one afterwards.
    private bool _applyingAutoComplete;

    public AppSettings Settings { get; }
    public string JournalDirectory { get; }

    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];
    public ObservableCollection<SlotRowViewModel> SlotRows { get; } = [];
    public ObservableCollection<CommodityRowViewModel> CommodityRows { get; } = [];
    public ObservableCollection<UnloadingSiteViewModel> UnloadingSites { get; } = [];
    public List<string> StructureTypeNames { get; }
    public IReadOnlyDictionary<string, List<string>> LayoutVariantCatalog => _layoutVariantCatalog;

    private ProjectRowViewModel? _selectedProject;
    public ProjectRowViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(HasSelectedProject));
                RebuildForSelectedProject();
            }
        }
    }

    public bool HasSelectedProject => SelectedProject is not null;

    private string _journalStatus = "Starting...";
    public string JournalStatus { get => _journalStatus; set => SetProperty(ref _journalStatus, value); }

    private string _lastEventText = "No events seen yet this session";
    public string LastEventText { get => _lastEventText; set => SetProperty(ref _lastEventText, value); }

    private int _totalStillNeeded;
    public int TotalStillNeeded
    {
        get => _totalStillNeeded;
        set { if (SetProperty(ref _totalStillNeeded, value)) OnPropertyChanged(nameof(ShowEverythingDeliveredMessage)); }
    }

    private int _tripsLargeShip;
    public int TripsLargeShip { get => _tripsLargeShip; set => SetProperty(ref _tripsLargeShip, value); }

    private int _tripsMediumShip;
    public int TripsMediumShip { get => _tripsMediumShip; set => SetProperty(ref _tripsMediumShip, value); }

    private int _tripsCarrier;
    public int TripsCarrier { get => _tripsCarrier; set => SetProperty(ref _tripsCarrier, value); }

    // Brief inline confirmation next to the copy button - ColonyTracker's status bar is already
    // busy with live journal text (unlike FleetFinder's, which sits mostly idle), so it would get
    // overwritten almost immediately during actual play instead of actually being seen.
    private bool _systemNameCopied;
    public bool SystemNameCopied { get => _systemNameCopied; set => SetProperty(ref _systemNameCopied, value); }

    /// <summary>Drives the Cargo Hauling grid's loading overlay - true for the full duration of a
    /// hub-price refresh, including any chained re-run triggered by RefreshHubPricesAsync's own
    /// in-flight-request coalescing, so the overlay doesn't flicker off between them.</summary>
    private bool _isRefreshingHubPrices;
    public bool IsRefreshingHubPrices { get => _isRefreshingHubPrices; set => SetProperty(ref _isRefreshingHubPrices, value); }

    // Beside Search Again rather than in the status bar, for the same reason as SystemNameCopied:
    // live journal text would overwrite it within seconds. Empty unless the last refresh was
    // turned away by the relay's rate limit.
    private string _hubPricesStatus = "";
    public string HubPricesStatus { get => _hubPricesStatus; set => SetProperty(ref _hubPricesStatus, value); }

    /// <summary>Toggles the overlay between the existing "Loading" view (where to buy still-needed
    /// commodities) and "Unloading" (per-active-station remaining delivery, for players hauling
    /// cargo TO a build site rather than shopping for it).</summary>
    private bool _showUnloadingOverlay;
    public bool ShowUnloadingOverlay
    {
        get => _showUnloadingOverlay;
        set
        {
            if (SetProperty(ref _showUnloadingOverlay, value))
            {
                OnPropertyChanged(nameof(IsLoadingMode));
                OnPropertyChanged(nameof(ShowNoActiveStationsMessage));
                OnPropertyChanged(nameof(ShowEverythingDeliveredMessage));
            }
        }
    }

    public bool IsLoadingMode => !ShowUnloadingOverlay;

    /// <summary>Drives the overlay's "No active stations in the System Plan tab." placeholder -
    /// only while in Unloading mode AND there's genuinely nothing to show (no slot that is Active
    /// and not yet Completed), not just whenever Unloading mode is selected.</summary>
    public bool ShowNoActiveStationsMessage => ShowUnloadingOverlay && UnloadingSites.Count == 0;

    /// <summary>Drives the overlay's "Everything delivered!" placeholder - only while in Loading
    /// mode AND nothing is still needed, not just whenever Loading mode is selected.</summary>
    public bool ShowEverythingDeliveredMessage => IsLoadingMode && TotalStillNeeded == 0;

    public RelayCommand DeleteProjectCommand { get; }
    public RelayCommand AddSlotCommand { get; }
    public RelayCommand<SlotRowViewModel> DeleteSlotCommand { get; }
    public RelayCommand UndoDeleteSlotCommand { get; }
    public RelayCommand SearchAgainCommand { get; }
    public RelayCommand RescanJournalCommand { get; }

    /// <summary>Single-level undo for a Delete-key slot removal (its row's own Remove button was
    /// replaced by the Completed checkbox column, so this is the only safety net against an
    /// accidental delete). Cleared on project switch - restoring into a different project's Slots
    /// than the one it was deleted from would be wrong.</summary>
    private (ConstructionSlot Model, int Index)? _lastDeletedSlot;
    public bool CanUndoDeleteSlot => _lastDeletedSlot is not null;

    public MainViewModel(AppSettings settings, StructureCatalog catalog, IReadOnlyDictionary<string, List<string>> layoutVariantCatalog, List<ColonyProject> initialProjects)
    {
        Settings = settings;
        _catalog = catalog;
        _layoutVariantCatalog = layoutVariantCatalog;
        StructureTypeNames = catalog.Structures.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        JournalDirectory = JournalDirectoryLocator.Resolve(settings.JournalDirectoryOverride);

        _tracker = new ConstructionTrackerService(JournalDirectory);
        _tracker.DepotUpdated += OnDepotUpdated;
        _tracker.EventObserved += OnEventObserved;
        _tracker.ErrorOccurred += OnTrackerError;

        _cargoReader = new ShipCargoReader(JournalDirectory);
        _cargoReader.CargoUpdated += OnCargoUpdated;

        _carrierCargoTracker = new CarrierCargoTracker(JournalDirectory, _cargoReader);
        _carrierCargoTracker.Updated += OnCarrierCargoUpdated;
        if (settings.CarrierTrackerJournalFile is { } seededFile)
            _carrierCargoTracker.SeedPosition(seededFile, settings.CarrierTrackerJournalOffset);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); ProjectStore.Save(Projects.Select(p => p.Model).ToList()); };

        foreach (var project in initialProjects)
            Projects.Add(new ProjectRowViewModel(project, ScheduleSave));

        DeleteProjectCommand = new RelayCommand(DeleteProject, () => SelectedProject is not null);
        AddSlotCommand = new RelayCommand(AddSlot, () => SelectedProject is not null);
        DeleteSlotCommand = new RelayCommand<SlotRowViewModel>(DeleteSlot, row => row is not null);
        UndoDeleteSlotCommand = new RelayCommand(UndoDeleteSlot, () => CanUndoDeleteSlot);
        SearchAgainCommand = new RelayCommand(RefreshHubPricesAsync, () => SelectedProject is not null);
        RescanJournalCommand = new RelayCommand(RescanJournal);

        SelectedProject = Projects.FirstOrDefault();

        _tracker.Start();
        _cargoReader.Start();
        _carrierCargoTracker.Start();
        JournalStatus = $"Watching {JournalDirectory}";
    }

    /// <summary>Called once from Window_Closing, right before the final ProjectStore.Save - folds
    /// the currently-selected project's live Carrier auto-deltas into their persisted baselines so
    /// this session's auto-tracked carrier transfers survive into the save about to happen, instead
    /// of evaporating the next time the app starts against a new journal file (see
    /// CommodityRowViewModel.BakeInAutoDelta for why that would otherwise happen). Scoped to the
    /// selected project only - CommodityRows only ever reflects whichever project is currently
    /// selected, so that's the only one with a live delta to bake in.</summary>
    public void BakeInCarrierDeltasBeforeExit()
    {
        if (SelectedProject is not null)
        {
            var stock = SelectedProject.Model.CarrierStock;
            foreach (var row in CommodityRows)
                row.BakeInAutoDelta(stock);
        }

        var (file, offset) = _carrierCargoTracker.TailerPosition;
        if (file is not null)
        {
            Settings.CarrierTrackerJournalFile = file;
            Settings.CarrierTrackerJournalOffset = offset;
        }
    }

    public void Dispose()
    {
        _tracker.Dispose();
        _cargoReader.Dispose();
        _carrierCargoTracker.Dispose();
        _relayService.Dispose();
    }

    /// <summary>Call after the Settings window closes with changes - ship/carrier capacities affect trip counts.
    /// A changed journal directory override only takes effect on next launch.</summary>
    public void OnSettingsChanged()
    {
        RecomputeTripSummary();
    }

    /// <summary>Adds an already-built project (empty, or pre-populated from a pasted sheet by
    /// NewProjectDialog) and selects it.</summary>
    public void AddProject(ColonyProject project)
    {
        var row = new ProjectRowViewModel(project, ScheduleSave);
        Projects.Add(row);
        SelectedProject = row;
        ScheduleSave();
    }

    private void DeleteProject()
    {
        if (SelectedProject is null) return;
        var index = Projects.IndexOf(SelectedProject);
        Projects.Remove(SelectedProject);
        SelectedProject = Projects.Count == 0 ? null : Projects[Math.Min(index, Projects.Count - 1)];
        ScheduleSave();
    }

    private void AddSlot()
    {
        if (SelectedProject is null) return;
        var slot = new ConstructionSlot { StructureType = StructureTypeNames.FirstOrDefault() ?? "" };
        SelectedProject.Model.Slots.Add(slot);
        SlotRows.Add(new SlotRowViewModel(slot, OnSlotChanged, _layoutVariantCatalog));
        RecomputeAndRefreshHubPrices();
        ScheduleSave();
    }

    private void DeleteSlot(SlotRowViewModel? row)
    {
        if (row is null || SelectedProject is null) return;
        var index = SelectedProject.Model.Slots.IndexOf(row.Model);
        SelectedProject.Model.Slots.Remove(row.Model);
        SlotRows.Remove(row);

        _lastDeletedSlot = (row.Model, index);
        OnPropertyChanged(nameof(CanUndoDeleteSlot));
        UndoDeleteSlotCommand.RaiseCanExecuteChanged();

        RecomputeAndRefreshHubPrices();
        ScheduleSave();
    }

    /// <summary>Restores the single most recently deleted slot back to its original position, in
    /// both the persisted Slots list and the SlotRows the grid is bound to.</summary>
    private void UndoDeleteSlot()
    {
        if (_lastDeletedSlot is not { } deleted || SelectedProject is null) return;
        var insertIndex = Math.Clamp(deleted.Index, 0, SelectedProject.Model.Slots.Count);
        SelectedProject.Model.Slots.Insert(insertIndex, deleted.Model);
        SlotRows.Insert(insertIndex, new SlotRowViewModel(deleted.Model, OnSlotChanged, _layoutVariantCatalog));

        _lastDeletedSlot = null;
        OnPropertyChanged(nameof(CanUndoDeleteSlot));
        UndoDeleteSlotCommand.RaiseCanExecuteChanged();

        RecomputeAndRefreshHubPrices();
        ScheduleSave();
    }

    /// <summary>Reorders a dragged slot to just before <paramref name="target"/>, or to the end when
    /// <paramref name="target"/> is null (dropped past the last row). Model.Slots is rewritten to
    /// match so the new order survives a save/reload, not just the in-memory grid.</summary>
    public bool MoveSlot(SlotRowViewModel dragged, SlotRowViewModel? target)
    {
        if (SelectedProject is null) return false;
        int oldIndex = SlotRows.IndexOf(dragged);
        if (oldIndex < 0) return false;

        int newIndex = target is null ? SlotRows.Count - 1 : SlotRows.IndexOf(target);
        if (newIndex < 0 || newIndex == oldIndex) return false;

        SlotRows.Move(oldIndex, newIndex);
        SelectedProject.Model.Slots.Clear();
        SelectedProject.Model.Slots.AddRange(SlotRows.Select(r => r.Model));
        ScheduleSave();
        return true;
    }

    /// <summary>Any edit to a slot row, including ticking Active or Completed in the grid (and the
    /// auto-tick, via AutoCompleteFinishedSites). Recomputes every Cargo Hauling figure and both
    /// overlay lists straight away. When that brings commodities back (Active ticked, Completed
    /// unticked), their rows are new and have no hub source yet, so the hub search runs for them
    /// too: the overlay's Loading list only shows rows that have one.</summary>
    private void OnSlotChanged()
    {
        if (_applyingAutoComplete) return;
        if (RecomputeCommodityRows())
            _ = RefreshHubPricesAsync();
        ScheduleSave();
    }

    private void RebuildForSelectedProject()
    {
        SlotRows.Clear();
        _rowsByCommodity.Clear();
        CommodityRows.Clear();

        // Restoring into the wrong project's Slots would be wrong, so a pending delete doesn't
        // survive a project switch.
        _lastDeletedSlot = null;
        OnPropertyChanged(nameof(CanUndoDeleteSlot));
        UndoDeleteSlotCommand.RaiseCanExecuteChanged();

        if (SelectedProject is null) return;

        foreach (var slot in SelectedProject.Model.Slots)
            SlotRows.Add(new SlotRowViewModel(slot, OnSlotChanged, _layoutVariantCatalog));

        AutoCompleteFinishedSites(); // depots already seen this session may show a build in this project is done
        RecomputeAndRefreshHubPrices(); // one-time search on load/project switch - no more periodic timer
    }

    /// <summary>
    /// Whether a slot still needs hauling for: an Active build that isn't Completed yet. Every
    /// Cargo Hauling figure (required, delivered, still needed, trip counts, the hub search) and
    /// both overlay lists go through this, so a finished build drops out of all of them at once,
    /// whether Completed was ticked by hand or by AutoCompleteFinishedSites.
    /// </summary>
    private static bool NeedsHauling(ConstructionSlot slot) => slot.Active && !slot.Completed;

    /// <summary>
    /// Ticks Completed on every slot that still needs hauling but whose live depot shows nothing
    /// left to deliver. Runs when live depot data arrives (OnDepotUpdated) and on a project switch,
    /// not on every grid edit, so unticking Completed by hand sticks until the next depot update;
    /// it only ever sets Completed, never clears it. Goes through the row view model so the grid's
    /// checkbox updates too, with each row's own recompute held off until every tick is in.
    /// Returns whether anything was ticked; the caller recomputes the hauling figures.
    /// </summary>
    private bool AutoCompleteFinishedSites()
    {
        if (SelectedProject is null) return false;
        var depots = _tracker.GetDepotsForSystem(SelectedProject.Model.SystemName).ToList();
        var finished = SelectedProject.Model.Slots
            .Where(s => NeedsHauling(s) && MatchDepot(s, depots) is { } depot && depot.Resources.All(r => r.Remaining == 0))
            .ToList();
        if (finished.Count == 0) return false;

        _applyingAutoComplete = true;
        try
        {
            foreach (var slot in finished)
            {
                var row = SlotRows.FirstOrDefault(r => r.Model == slot);
                if (row is not null) row.Completed = true;
                else slot.Completed = true;
            }
        }
        finally
        {
            _applyingAutoComplete = false;
        }
        ScheduleSave();
        return true;
    }

    private void RecomputeAndRefreshHubPrices()
    {
        RecomputeCommodityRows();
        _ = RefreshHubPricesAsync();
    }

    /// <summary>Rebuilds required/carrier totals from the slots that still need hauling (Active and
    /// not Completed), then the live amounts and the overlay's Unloading list. Existing row objects
    /// are reused where possible so an in-progress Carrier edit isn't disrupted by unrelated
    /// updates. Returns whether any commodity row was added (those have no hub source yet).</summary>
    private bool RecomputeCommodityRows()
    {
        if (SelectedProject is null) return false;
        var project = SelectedProject.Model;
        var added = false;

        var requiredTotals = new Dictionary<string, int>();
        foreach (var slot in project.Slots.Where(NeedsHauling))
        {
            var structure = _catalog.Structures.FirstOrDefault(s => string.Equals(s.Name, slot.StructureType, StringComparison.OrdinalIgnoreCase));
            if (structure is null) continue;
            foreach (var (commodity, amount) in structure.Costs)
                requiredTotals[commodity] = requiredTotals.GetValueOrDefault(commodity) + amount;
        }

        var stillNeededNames = new HashSet<string>();
        foreach (var commodity in _catalog.Commodities)
        {
            if (!requiredTotals.TryGetValue(commodity.Name, out var required) || required <= 0) continue;
            stillNeededNames.Add(commodity.Name);

            if (!_rowsByCommodity.TryGetValue(commodity.Name, out var row))
            {
                var key = CommodityNameMatcher.Normalize(commodity.Name);
                row = new CommodityRowViewModel(commodity.Name, commodity.Category, key, OnCarrierEdited);
                row.SetCarrierSilently(project.CarrierStock.GetValueOrDefault(key));
                _rowsByCommodity[commodity.Name] = row;
                CommodityRows.Add(row);
                added = true;
            }
            row.Required = required;
        }

        // drop rows for commodities no longer required by any slot that still needs hauling
        foreach (var name in _rowsByCommodity.Keys.Where(n => !stillNeededNames.Contains(n)).ToList())
        {
            CommodityRows.Remove(_rowsByCommodity[name]);
            _rowsByCommodity.Remove(name);
        }

        RefreshLiveAmounts();
        RebuildUnloadingSites();
        return added;
    }

    /// <summary>Rebuilds the overlay's Unloading-mode list: one entry per slot that still needs
    /// hauling (Active, not Completed), correlated to a live depot (if the player has docked there
    /// this session) by matching the slot's own Name against the depot's journal-reported station
    /// name. Ticking Completed, by hand or automatically, takes a site off this list.</summary>
    private void RebuildUnloadingSites()
    {
        UnloadingSites.Clear();
        if (SelectedProject is null)
        {
            OnPropertyChanged(nameof(ShowNoActiveStationsMessage));
            return;
        }
        var project = SelectedProject.Model;
        var depots = _tracker.GetDepotsForSystem(project.SystemName).ToList();

        foreach (var slot in project.Slots.Where(NeedsHauling))
        {
            var depot = MatchDepot(slot, depots);

            var displayName = !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name
                : !string.IsNullOrWhiteSpace(slot.StructureType) ? slot.StructureType
                : "Unnamed site";

            List<RemainingCommodity> remaining;
            if (depot is not null)
            {
                remaining = depot.Resources
                    .Where(r => r.Remaining > 0)
                    .OrderByDescending(r => r.Remaining)
                    .Select(r => new RemainingCommodity(r.DisplayName, r.Remaining))
                    .ToList();
            }
            else
            {
                // Not docked there yet this session, so there's no live depot snapshot - fall back
                // to the structure's known static cost list (the same catalog lookup
                // RecomputeCommodityRows uses for the Loading tab) so the site still shows the
                // totals it needs instead of sitting empty until the player actually visits.
                var structure = _catalog.Structures.FirstOrDefault(s => string.Equals(s.Name, slot.StructureType, StringComparison.OrdinalIgnoreCase));
                remaining = structure?.Costs
                    .Where(c => c.Value > 0)
                    .OrderByDescending(c => c.Value)
                    .Select(c => new RemainingCommodity(c.Key, c.Value))
                    .ToList() ?? [];
            }

            // A site with live data and nothing remaining is normally ticked Completed by
            // AutoCompleteFinishedSites before this runs. It can only be here if the player
            // unticked it by hand since, and then it shows the overlay's "Fully delivered!" note.
            UnloadingSites.Add(new UnloadingSiteViewModel(displayName, slot.Body, remaining, depot is not null));
        }

        OnPropertyChanged(nameof(ShowNoActiveStationsMessage));
    }

    /// <summary>Finds the live depot (if any) matching a slot's own Name field, tolerating the
    /// game's actual station-name format of "&lt;Orbital/Planetary&gt; Construction Site:
    /// &lt;player-given name&gt;" - an exact match against the slot's own (shorter) Name never
    /// succeeds (confirmed via diagnostic logging 2026-07-31). Shared by RefreshLiveAmounts (to
    /// scope Delivered to only the depots of slots that still need hauling), RebuildUnloadingSites
    /// (the overlay's per-site view) and AutoCompleteFinishedSites.</summary>
    private static ConstructionSiteState? MatchDepot(ConstructionSlot slot, IReadOnlyList<ConstructionSiteState> depots) =>
        !string.IsNullOrWhiteSpace(slot.Name)
            ? depots.FirstOrDefault(d => d.StationName is not null && d.StationName.EndsWith(slot.Name, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// Queries EDCF.Relay for current buy price/stock at the configured hub stations and picks the
    /// best one per still-needed commodity: orbital stations first (easier to reach/dock at than a
    /// surface settlement), then highest stock, then lowest price as the final tiebreaker -
    /// distance from the star doesn't matter since loading happens via a fleet carrier parked
    /// wherever. Best effort: a slow/unreachable relay just leaves rows showing their previous (or
    /// no) source rather than blocking or erroring the UI.
    /// </summary>
    private async Task RefreshHubPricesAsync()
    {
        if (SelectedProject is null || CommodityRows.Count == 0) return;

        // A refresh already in flight was dispatched with whatever commodity set existed at the
        // time - any row added/changed since then (e.g. a burst of slots activating on project
        // load) would otherwise never get queried until the next 2-minute timer tick. Remembering
        // the request and re-running once the current one finishes closes that gap.
        if (_relayRefreshInFlight)
        {
            _relayRefreshPending = true;
            return;
        }

        _relayRefreshInFlight = true;
        IsRefreshingHubPrices = true;
        try
        {
            var names = CommodityRows.Select(r => r.Name).ToList();
            var read = await _relayService.GetHubListingsAsync(Settings.RelayBaseUrl, Settings.HubSystems, names);

            RunOnUiThread(() =>
            {
                HubPricesStatus = read.RelayBusy ? RelayCommodityService.BusyMessage : "";

                // A turned-away request says nothing about the hubs, so the rows keep whatever
                // source they were already showing.
                if (read.RelayBusy) return;

                var bestByCommodity = read.Listings
                    .GroupBy(l => CommodityNameMatcher.Normalize(l.Commodity))
                    .ToDictionary(g => g.Key, g => g
                        .OrderByDescending(l => IsOrbitalStation(l.StationName))
                        .ThenByDescending(l => l.Stock)
                        .ThenBy(l => l.BuyPrice)
                        .First());

                foreach (var row in CommodityRows)
                {
                    if (bestByCommodity.TryGetValue(row.NormalizedName, out var best))
                        row.SetBestSource(best.StationName, best.StarSystem, best.BuyPrice, best.Stock, IsOrbitalStation(best.StationName));
                    else
                        row.ClearBestSource();
                }
            });
        }
        finally
        {
            _relayRefreshInFlight = false;
        }

        if (_relayRefreshPending)
        {
            _relayRefreshPending = false;
            await RefreshHubPricesAsync(); // awaited (not fire-and-forget) so IsRefreshingHubPrices stays true across the whole chain
        }
        else
        {
            IsRefreshingHubPrices = false;
        }
    }

    /// <summary>Looks up whether a hub station is orbital or a surface settlement, per how it's
    /// listed in Settings.HubSystems. Defaults to orbital for an unrecognized name - that's the
    /// common case today (all six currently-configured stations are orbital).</summary>
    private bool IsOrbitalStation(string stationName)
    {
        foreach (var hub in Settings.HubSystems)
        {
            if (hub.PlanetaryStations.Contains(stationName, StringComparer.OrdinalIgnoreCase)) return false;
            if (hub.Stations.Contains(stationName, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return true;
    }

    /// <summary>Updates Ship/Delivered on existing rows only - doesn't touch Required/Carrier, so it's safe to call on every journal/cargo event.</summary>
    private void RefreshLiveAmounts()
    {
        if (SelectedProject is null) return;
        var project = SelectedProject.Model;

        // Scoped to only the matched depots of slots that still need hauling (Active, not
        // Completed) - GetDepotsForSystem alone returns every depot ever seen in the system this
        // session regardless of either flag, so a completed or no-longer-active station's
        // delivered total would otherwise keep counting toward whatever's still being built
        // forever (e.g. still showing StillNeeded=0 for a brand new station's commodities just
        // because a *previous, unrelated* station already delivered that much of them).
        var allDepots = _tracker.GetDepotsForSystem(project.SystemName).ToList();
        var haulingDepots = project.Slots.Where(NeedsHauling)
            .Select(s => MatchDepot(s, allDepots))
            .Where(d => d is not null);

        var deliveredTotals = new Dictionary<string, int>();
        foreach (var depot in haulingDepots)
            foreach (var res in depot!.Resources)
            {
                var key = CommodityNameMatcher.Normalize(res.DisplayName);
                deliveredTotals[key] = deliveredTotals.GetValueOrDefault(key) + res.ProvidedAmount;
            }

        var shipTotals = _cargoReader.Current;
        var carrierAutoTotals = _carrierCargoTracker.Current;

        foreach (var row in CommodityRows)
        {
            row.Ship = shipTotals.GetValueOrDefault(row.NormalizedName);
            row.Delivered = deliveredTotals.GetValueOrDefault(row.NormalizedName);
            row.SetCarrierAutoDelta(carrierAutoTotals.GetValueOrDefault(row.NormalizedName));
        }

        RecomputeTripSummary();
    }

    private void RecomputeTripSummary()
    {
        var total = CommodityRows.Sum(r => r.StillNeeded);
        TotalStillNeeded = total;
        TripsLargeShip = TripsFor(total, Settings.LargeShipCapacity);
        TripsMediumShip = TripsFor(total, Settings.MediumShipCapacity);
        TripsCarrier = TripsFor(total, Settings.FleetCarrierCapacity);

        static int TripsFor(int total, int capacity) =>
            capacity > 0 ? (int)Math.Ceiling(total / (double)capacity) : 0;
    }

    private void OnCarrierEdited(string normalizedName, int value)
    {
        var stock = SelectedProject?.Model.CarrierStock;
        if (stock is null) return;
        if (value <= 0) stock.Remove(normalizedName);
        else stock[normalizedName] = value;
        RecomputeTripSummary();
        ScheduleSave();
    }

    private void OnDepotUpdated(ConstructionSiteState state) =>
        RunOnUiThread(() =>
        {
            LastEventText = $"Construction update at {state.DisplayLocation} - {state.ConstructionProgress:P0} complete ({DateTimeOffset.Now:T})";
            // A depot with nothing left to deliver means that build is done: tick it Completed and
            // take it out of every hauling figure. Otherwise only the live amounts and the
            // per-site list change.
            if (AutoCompleteFinishedSites())
            {
                RecomputeCommodityRows();
            }
            else
            {
                RefreshLiveAmounts();
                RebuildUnloadingSites();
            }
        });

    private void OnCargoUpdated(IReadOnlyDictionary<string, int> cargo) =>
        RunOnUiThread(RefreshLiveAmounts);

    private void OnCarrierCargoUpdated(IReadOnlyDictionary<string, int> carrierAuto) =>
        RunOnUiThread(RefreshLiveAmounts);

    private void OnEventObserved(DateTimeOffset at) =>
        RunOnUiThread(() => JournalStatus = $"Watching {JournalDirectory} - last event {at:T}");

    private void OnTrackerError(Exception ex) =>
        RunOnUiThread(() => JournalStatus = $"Journal watcher error: {ex.Message}");

    /// <summary>Manual recovery path (bottom-bar "Rescan Journal" button): forces both live
    /// trackers to replay the entire current journal session from scratch, for when tracking
    /// looks out of sync and the user doesn't want to restart the whole app to fix it.</summary>
    private void RescanJournal()
    {
        _tracker.Rescan();
        _carrierCargoTracker.Rescan();
        RefreshLiveAmounts();
        RebuildUnloadingSites();
        JournalStatus = $"Watching {JournalDirectory} - rescanned {DateTimeOffset.Now:T}";
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
