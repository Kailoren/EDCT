using System.Text.Json;

namespace ColonyTracker.Services;

/// <summary>
/// Tracks ship-to-carrier cargo moved via the game's own "Transfer" screen while docked at your
/// fleet carrier (the CargoTransfer journal event) - separate from Cargo.json (which only ever
/// reports what's currently on the SHIP) and from ColonisationConstructionDepot (delivery to a
/// construction site). Without this, moving cargo straight from ship to carrier - skipping the
/// commodity market entirely - makes the ship total correctly drop to 0 while nothing else picks
/// up the slack, so "still needed" would incorrectly jump back up as if the cargo had vanished.
///
/// Purely session-scoped and in-memory, recomputed fresh from a full replay every time the app
/// starts (matching JournalTailer's own by-design "replay the whole current file from offset 0"
/// behavior, and the same pattern ConstructionTrackerService/ShipCargoReader already rely on)
/// rather than accumulated into a persisted running total - CargoTransfer is a delta/transaction
/// event, not an absolute snapshot the way ColonisationConstructionDepot is, so persisting an
/// incrementally-added sum would double-count every previously-seen transfer on every app restart
/// mid-session.
///
/// A live transfer in EITHER direction is held back until Cargo.json visibly reflects the ship's
/// expected change, rather than applied the instant the journal line arrives - the game writes the
/// CargoTransfer line before Cargo.json catches up (a real several-second gap observed in
/// practice). Applying "tocarrier" immediately (carrier up right away, ship not yet down) is just
/// as visible a mismatch as applying "toship" immediately (carrier down right away, ship not yet
/// up) - the first round of this fix only covered "toship" since that was the direction reported,
/// but both are the same underlying asynchrony and both need the same treatment. Only applies to
/// LIVE transfers - judged by the event's own timestamp being recent - since a startup/Rescan()
/// replay of journal history has no "in progress" moment to smooth over; those apply immediately.
/// </summary>
public sealed class CarrierCargoTracker : IDisposable
{
    private static readonly TimeSpan LiveEventWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PendingConfirmationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PendingCheckInterval = TimeSpan.FromSeconds(5);

    private readonly JournalTailer _tailer;
    private readonly ShipCargoReader _shipCargoReader;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _net = [];
    private readonly Dictionary<string, PendingTransfer> _pending = [];
    private Timer? _pendingCheckTimer;

    // Debounces Updated the same way ConstructionTrackerService debounces DepotUpdated:
    // JournalTailer.Start() replays the whole current journal file synchronously on the calling
    // thread (MainViewModel's constructor, on the UI thread at startup), and a session with many
    // ship<->carrier transfers can replay a lot of CargoTransfer events in that pass. Notifying
    // immediately on every one meant a synchronous MainViewModel.RefreshLiveAmounts per event,
    // inline on the UI thread, for the entire replay. Current is re-read fresh at flush time
    // (already fully up to date, since _net/_pending are updated immediately either way), so
    // coalescing the outward notification loses no data.
    private readonly object _notifyGate = new();
    private Timer? _notifyTimer;
    private static readonly TimeSpan NotifyDebounce = TimeSpan.FromMilliseconds(200);

    public event Action<IReadOnlyDictionary<string, int>>? Updated;
    public event Action<Exception>? ErrorOccurred;

    /// <summary>Net units moved to the carrier this session so far, keyed by normalized commodity
    /// name - a "toship" transfer (pulling cargo back off the carrier) subtracts, so a round trip
    /// nets to zero rather than double-counting. Returns a defensive copy since a background
    /// thread (journal/ship-cargo events, the pending-check timer) can be mid-update while this is
    /// read from the UI thread.</summary>
    public IReadOnlyDictionary<string, int> Current { get { lock (_gate) return new Dictionary<string, int>(_net); } }

    /// <summary>The underlying tailer's current (file, byte offset) - see SeedPosition.</summary>
    public (string? File, long Offset) TailerPosition => _tailer.Position;

    /// <summary>Resumes the underlying tailer from a previously-persisted position instead of
    /// replaying the whole current journal file from scratch - call before Start(). Without this,
    /// baking a session's auto-detected total into the persisted baseline (see
    /// CommodityRowViewModel.BakeInAutoDelta) and then restarting the app while Elite Dangerous is
    /// still writing the SAME journal file would replay and re-count the exact transfers that were
    /// just baked in, doubling the total. Seeding to exactly where the last bake left off means the
    /// replay only ever picks up transfers that happened after that point.</summary>
    public void SeedPosition(string file, long offset) => _tailer.SeedPosition(file, offset);

    public CarrierCargoTracker(string journalDirectory, ShipCargoReader shipCargoReader)
    {
        _shipCargoReader = shipCargoReader;
        _shipCargoReader.CargoUpdated += OnShipCargoUpdated;

        _tailer = new JournalTailer(journalDirectory);
        _tailer.EventReceived += OnEvent;
        _tailer.ErrorOccurred += ex => ErrorOccurred?.Invoke(ex);
    }

    public void Start()
    {
        _tailer.Start();
        _pendingCheckTimer = new Timer(_ => CheckPending(), null, PendingCheckInterval, PendingCheckInterval);
    }

    public void Stop()
    {
        _tailer.Stop();
        _pendingCheckTimer?.Dispose();
        _pendingCheckTimer = null;
    }

    public void Dispose()
    {
        Stop();
        _shipCargoReader.CargoUpdated -= OnShipCargoUpdated;
        _tailer.Dispose();
        lock (_notifyGate) _notifyTimer?.Dispose();
    }

    private void ScheduleNotify()
    {
        lock (_notifyGate)
        {
            _notifyTimer ??= new Timer(_ => Updated?.Invoke(Current));
            _notifyTimer.Change(NotifyDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Forces a fresh full replay of the current journal session. Unlike
    /// ConstructionTrackerService's rescan, CargoTransfer is a delta (not an absolute snapshot),
    /// so the accumulated net total (and anything still awaiting ship confirmation) has to be
    /// cleared first - otherwise replaying the same already-seen transfers would double-count them
    /// on top of what's already there.</summary>
    public void Rescan()
    {
        lock (_gate)
        {
            _net.Clear();
            _pending.Clear();
        }
        _tailer.Rescan();
    }

    private void OnEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventProp) || eventProp.GetString() != "CargoTransfer") return;
        if (!root.TryGetProperty("Transfers", out var transfers) || transfers.ValueKind != JsonValueKind.Array) return;

        // A replay (startup, or a manual Rescan) has no "in progress" moment for the player to
        // see, so historical events skip the pending/confirm dance entirely and apply straight
        // away - only a transfer that just happened gets held back for Cargo.json to catch up.
        var isLive = root.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTimeOffset(out var eventTime)
            && DateTimeOffset.UtcNow - eventTime < LiveEventWindow;

        var changed = false;
        lock (_gate)
        {
            foreach (var transfer in transfers.EnumerateArray())
            {
                // Only ship<->carrier legs matter here - CargoTransfer also covers ship<->SRV
                // ("tosrv"/"fromsrv"), which this deliberately ignores.
                var direction = transfer.TryGetProperty("Direction", out var d) ? d.GetString() : null;
                // Ship's own quantity moves opposite to the carrier's: "tocarrier" means the ship
                // is about to lose this many units, "toship" means it's about to gain them.
                var shipDelta = direction switch { "tocarrier" => -1, "toship" => 1, _ => 0 };
                if (shipDelta == 0) continue;

                var type = transfer.TryGetProperty("Type", out var t) ? t.GetString() : null;
                var count = transfer.TryGetProperty("Count", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
                if (string.IsNullOrEmpty(type) || count <= 0) continue;

                var key = CommodityNameMatcher.Normalize(type);
                if (isLive)
                {
                    var shipNow = _shipCargoReader.Current.GetValueOrDefault(key);
                    _pending[key] = _pending.TryGetValue(key, out var existing)
                        ? existing with { ShipDelta = existing.ShipDelta + shipDelta * count }
                        : new PendingTransfer(shipNow, shipDelta * count, DateTime.UtcNow);
                }
                else
                {
                    // Carrier moves opposite to the ship - what the ship loses, the carrier gains.
                    _net[key] = _net.GetValueOrDefault(key) - shipDelta * count;
                    changed = true;
                }
            }
        }
        if (changed) ScheduleNotify();
    }

    private void OnShipCargoUpdated(IReadOnlyDictionary<string, int> shipTotals) => CheckPending();

    /// <summary>Applies any pending transfer's carrier-side effect once its commodity has either
    /// visibly caught up in Cargo.json (the ship's amount reaching baseline + the pending signed
    /// delta - negative for a "tocarrier" decrease, positive for a "toship" increase), or has been
    /// waiting long enough that something's clearly not going to confirm it normally
    /// (PendingConfirmationTimeout) - a safety net against a stuck pending entry permanently
    /// mis-stating "still needed" if Cargo.json's own update is ever missed or coalesced past the
    /// exact expected value.</summary>
    private void CheckPending()
    {
        var changed = false;
        lock (_gate)
        {
            if (_pending.Count == 0) return;

            var shipTotals = _shipCargoReader.Current;
            foreach (var key in _pending.Keys.ToList())
            {
                var pending = _pending[key];
                var expectedShip = pending.ShipBaseline + pending.ShipDelta;
                var caughtUp = pending.ShipDelta >= 0
                    ? shipTotals.GetValueOrDefault(key) >= expectedShip
                    : shipTotals.GetValueOrDefault(key) <= expectedShip;
                var timedOut = DateTime.UtcNow - pending.Since > PendingConfirmationTimeout;
                if (!caughtUp && !timedOut) continue;

                // Carrier moves opposite to the ship.
                _net[key] = _net.GetValueOrDefault(key) - pending.ShipDelta;
                _pending.Remove(key);
                changed = true;
            }
        }
        if (changed) ScheduleNotify();
    }

    private readonly record struct PendingTransfer(int ShipBaseline, int ShipDelta, DateTime Since);
}
