using System.Text.Json;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>
/// Consumes raw journal events from a JournalTailer and turns them into ConstructionSiteState
/// updates, one per MarketID. A single system can have several active construction sites at
/// once (the player picks which slots are "active" in their build plan), and the journal only
/// ever reports on whichever depot you're currently docked at/interacting with - so every depot
/// ever seen this session is kept, keyed by MarketID, and the caller aggregates whichever ones
/// belong to a given project's star system.
///
/// ColonisationConstructionDepot carries a full required/provided snapshot, but only fires when
/// the player opens that station's colonisation contact - it does not refire on its own as cargo
/// is delivered. ColonisationContribution fires on every actual drop-off instead, carrying just
/// what was contributed, so it's applied as an incremental ProvidedAmount update on top of
/// whichever snapshot ColonisationConstructionDepot last established for that MarketID.
/// Docked/Location events are cached by MarketID purely to attach a human-readable
/// station/system name, since neither of those events carries one itself.
/// </summary>
public sealed class ConstructionTrackerService : IDisposable
{
    private readonly JournalTailer _tailer;
    private readonly Dictionary<long, (string Station, string System)> _stationsByMarket = new();
    private readonly Dictionary<long, ConstructionSiteState> _depotsByMarket = new();

    // Debounces DepotUpdated: JournalTailer.Start() replays the *entire* current journal file
    // synchronously on whatever thread calls it (MainViewModel's constructor, on the UI thread at
    // startup), and a long play session can carry hundreds of ColonisationContribution events in
    // that replay. Notifying immediately on every one of them meant hundreds of synchronous
    // RefreshLiveAmounts/RebuildUnloadingSites passes inline on the UI thread during startup -
    // freezing the window until the whole replay finished. Coalescing into one notification, fired
    // off a timer (a different thread) shortly after the burst quiets down, fixes that: the replay
    // loop itself now only ever does cheap dictionary updates. RefreshLiveAmounts/
    // RebuildUnloadingSites always re-read the full live dictionary rather than using the passed
    // state, so collapsing multiple markets' updates into a single (most recent) notification loses
    // no data - each update is still recorded in _depotsByMarket immediately, only the outward
    // notification is delayed/coalesced.
    private readonly object _notifyGate = new();
    private Timer? _notifyTimer;
    private ConstructionSiteState? _pendingNotify;
    private static readonly TimeSpan NotifyDebounce = TimeSpan.FromMilliseconds(200);

    public event Action<ConstructionSiteState>? DepotUpdated;
    public event Action<Exception>? ErrorOccurred;
    public event Action<DateTimeOffset>? EventObserved;

    public ConstructionTrackerService(string journalDirectory)
    {
        _tailer = new JournalTailer(journalDirectory);
        _tailer.EventReceived += OnEvent;
        _tailer.ErrorOccurred += ex => ErrorOccurred?.Invoke(ex);
    }

    public void Start() => _tailer.Start();
    public void Stop() => _tailer.Stop();

    public void Dispose()
    {
        _tailer.Dispose();
        lock (_notifyGate) _notifyTimer?.Dispose();
    }

    private void ScheduleNotify(ConstructionSiteState state)
    {
        lock (_notifyGate)
        {
            _pendingNotify = state;
            _notifyTimer ??= new Timer(_ => FlushNotify());
            _notifyTimer.Change(NotifyDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushNotify()
    {
        ConstructionSiteState? state;
        lock (_notifyGate)
        {
            state = _pendingNotify;
            _pendingNotify = null;
        }
        if (state is not null) DepotUpdated?.Invoke(state);
    }

    /// <summary>Forces a fresh full replay of the current journal session - safe to call any time
    /// since ColonisationConstructionDepot carries an absolute snapshot each time, not a delta, so
    /// reprocessing already-seen events just re-applies the same values rather than double-counting.</summary>
    public void Rescan() => _tailer.Rescan();

    /// <summary>All known depots whose cached station context matches the given star system (case-insensitive).</summary>
    public IEnumerable<ConstructionSiteState> GetDepotsForSystem(string systemName) =>
        _depotsByMarket.Values.Where(d => string.Equals(d.StarSystem, systemName, StringComparison.OrdinalIgnoreCase));

    private void OnEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventProp)) return;
        var eventName = eventProp.GetString();
        EventObserved?.Invoke(DateTimeOffset.Now);

        switch (eventName)
        {
            case "Docked":
            case "Location":
                CacheStation(root);
                break;
            case "ColonisationConstructionDepot":
                HandleConstructionDepot(root);
                break;
            case "ColonisationContribution":
                HandleContribution(root);
                break;
        }
    }

    private void CacheStation(JsonElement root)
    {
        if (!root.TryGetProperty("MarketID", out var marketProp) || !marketProp.TryGetInt64(out var marketId))
            return;
        var station = root.TryGetProperty("StationName", out var sn) ? sn.GetString() : null;
        var system = root.TryGetProperty("StarSystem", out var ss) ? ss.GetString() : null;
        if (station is not null && system is not null)
            _stationsByMarket[marketId] = (station, system);
    }

    private void HandleConstructionDepot(JsonElement root)
    {
        if (!root.TryGetProperty("MarketID", out var marketProp) || !marketProp.TryGetInt64(out var marketId))
            return;

        var resources = new List<JournalResource>();
        if (root.TryGetProperty("ResourcesRequired", out var resArray) && resArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var res in resArray.EnumerateArray())
            {
                var internalName = res.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                var displayName = res.TryGetProperty("Name_Localised", out var nl) ? nl.GetString() : null;
                var required = res.TryGetProperty("RequiredAmount", out var ra) && ra.TryGetInt32(out var raVal) ? raVal : 0;
                var provided = res.TryGetProperty("ProvidedAmount", out var pa) && pa.TryGetInt32(out var paVal) ? paVal : 0;
                var payment = res.TryGetProperty("Payment", out var pay) && pay.TryGetInt64(out var payVal) ? payVal : 0L;
                resources.Add(new JournalResource(internalName, displayName ?? CleanInternalName(internalName), required, provided, payment));
            }
        }

        var progress = root.TryGetProperty("ConstructionProgress", out var cp) && cp.TryGetDouble(out var cpVal) ? cpVal : 0d;
        var complete = root.TryGetProperty("ConstructionComplete", out var cc) && cc.ValueKind == JsonValueKind.True;
        var failed = root.TryGetProperty("ConstructionFailed", out var cf) && cf.ValueKind == JsonValueKind.True;
        _stationsByMarket.TryGetValue(marketId, out var station);

        var state = new ConstructionSiteState(
            marketId, station.System, station.Station, progress, complete, failed, resources, DateTimeOffset.Now);
        _depotsByMarket[marketId] = state;
        ScheduleNotify(state);
    }

    /// <summary>
    /// ColonisationConstructionDepot only refreshes when the player reopens that station's
    /// colonisation contact, so a drop-off made without doing that never updates the tracked
    /// snapshot. This event fires on every actual cargo delivery and carries exactly what was just
    /// contributed, so it's applied as an incremental ProvidedAmount update against whatever
    /// snapshot is already cached for that MarketID. Silently ignored if no depot has been seen yet
    /// for that market - there's no RequiredAmount baseline to update against until the player has
    /// opened the contact at least once.
    /// </summary>
    private void HandleContribution(JsonElement root)
    {
        if (!root.TryGetProperty("MarketID", out var marketProp) || !marketProp.TryGetInt64(out var marketId))
            return;
        if (!_depotsByMarket.TryGetValue(marketId, out var existing))
            return;
        if (!root.TryGetProperty("Contributions", out var contributions) || contributions.ValueKind != JsonValueKind.Array)
            return;

        // OrdinalIgnoreCase: ColonisationConstructionDepot reports internal names lowercase
        // ("$battleweapons_name;") while ColonisationContribution reports the same commodities in
        // PascalCase ("$BattleWeapons_name;") - confirmed via diagnostic logging 2026-07-31, a
        // case-sensitive lookup here matched nothing at all, ever.
        var deltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in contributions.EnumerateArray())
        {
            var internalName = c.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            var amount = c.TryGetProperty("Amount", out var a) && a.TryGetInt32(out var aVal) ? aVal : 0;
            if (internalName.Length == 0 || amount == 0) continue;
            deltas[internalName] = deltas.GetValueOrDefault(internalName) + amount;
        }
        if (deltas.Count == 0) return;

        var updatedResources = existing.Resources
            .Select(r => deltas.TryGetValue(r.InternalName, out var delta) ? r with { ProvidedAmount = r.ProvidedAmount + delta } : r)
            .ToList();

        var state = existing with { Resources = updatedResources, UpdatedAt = DateTimeOffset.Now };
        _depotsByMarket[marketId] = state;
        ScheduleNotify(state);
    }

    /// <summary>Best-effort fallback if a future game update ever omits Name_Localised: "$aluminium_name;" -> "Aluminium".</summary>
    private static string CleanInternalName(string internalName)
    {
        var trimmed = internalName.Trim('$', ';');
        const string suffix = "_name";
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^suffix.Length];
        return trimmed.Length == 0 ? internalName : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
