namespace ColonyTracker.ViewModels;

/// <summary>
/// One aggregated commodity row in the cargo hauling view. Required/Ship/Delivered are
/// recomputed and pushed in by MainViewModel; Carrier is the field the user edits directly,
/// round-tripping back into the project's own stock dictionary via its callback. Carrier is
/// itself a blend of that manual entry plus a live auto-detected delta - see its own comment below.
/// </summary>
public sealed class CommodityRowViewModel(
    string name, string? category, string normalizedName,
    Action<string, int> onCarrierChanged)
    : ObservableObject
{
    public string Name { get; } = name;
    public string? Category { get; } = category;
    public string NormalizedName { get; } = normalizedName;

    private int _required;
    public int Required
    {
        get => _required;
        set { if (SetProperty(ref _required, value)) RaiseStillNeededChanged(); }
    }

    // Carrier is a blend of two sources: a manually-entered baseline (persisted, round-trips to
    // the project's own stock dictionary) and a live, session-scoped delta auto-detected from
    // CarrierCargoTracker (ship-to-carrier transfers via the game's "Transfer" screen, not the
    // commodity market - never persisted, recomputed fresh each session). They're kept as one
    // displayed/editable number rather than two separate columns, per explicit user preference -
    // editing it adjusts just the baseline, so the field "sticks" at whatever was typed while
    // still responding to any further transfers detected afterward.
    private int _carrierBaseline;
    private int _carrierAutoDelta;
    public int Carrier
    {
        get => _carrierBaseline + _carrierAutoDelta;
        set => SetCarrierBaseline(value - _carrierAutoDelta, onCarrierChanged);
    }

    private int _ship;
    public int Ship
    {
        get => _ship;
        set { if (SetProperty(ref _ship, value)) RaiseStillNeededChanged(); }
    }

    private int _delivered;
    public int Delivered
    {
        get => _delivered;
        set { if (SetProperty(ref _delivered, value)) RaiseStillNeededChanged(); }
    }

    public int StillNeeded => Math.Max(0, Required - Carrier - Ship - Delivered);

    /// <summary>Drives row visibility in the hauling overlay, which only shows commodities still outstanding.</summary>
    public bool IsNeeded => StillNeeded > 0;

    /// <summary>Sets Carrier's manual baseline directly, without the onCarrierChanged round-trip
    /// (used when loading/refreshing from the model).</summary>
    public void SetCarrierSilently(int value) => SetCarrierBaseline(value, null);

    /// <summary>Updates the live, session-scoped auto-detected transfer delta (from
    /// CarrierCargoTracker) - adjusts the combined Carrier value without touching the persisted
    /// baseline or round-tripping to the model, the same way Ship/Delivered work.</summary>
    public void SetCarrierAutoDelta(int value)
    {
        if (_carrierAutoDelta == value) return;
        _carrierAutoDelta = value;
        OnPropertyChanged(nameof(Carrier));
        RaiseStillNeededChanged();
    }

    /// <summary>Folds the live, session-scoped auto-detected delta into the persisted manual
    /// baseline and zeroes the delta, writing straight into <paramref name="stock"/> - called once
    /// at app shutdown. Without this, CarrierCargoTracker's auto-tracked total is silently lost the
    /// next time the app starts, since it only ever replays the CURRENT journal file - once Elite
    /// Dangerous rotates to a new one (e.g. after quitting and relaunching the game), this session's
    /// transfers become permanently unrecoverable from the journal. Returns whether anything
    /// actually changed, so the caller only needs to save when it did.</summary>
    public bool BakeInAutoDelta(Dictionary<string, int> stock)
    {
        if (_carrierAutoDelta == 0) return false;

        _carrierBaseline += _carrierAutoDelta;
        _carrierAutoDelta = 0;
        if (_carrierBaseline <= 0) stock.Remove(NormalizedName);
        else stock[NormalizedName] = _carrierBaseline;

        OnPropertyChanged(nameof(Carrier));
        RaiseStillNeededChanged();
        return true;
    }

    private void SetCarrierBaseline(int newBaseline, Action<string, int>? onChanged)
    {
        if (_carrierBaseline == newBaseline) return;
        _carrierBaseline = newBaseline;
        OnPropertyChanged(nameof(Carrier));
        onChanged?.Invoke(NormalizedName, newBaseline);
        RaiseStillNeededChanged();
    }

    private void RaiseStillNeededChanged()
    {
        OnPropertyChanged(nameof(StillNeeded));
        OnPropertyChanged(nameof(IsNeeded));
    }

    // Best hub station to load this commodity from, per MainViewModel's periodic relay refresh:
    // highest stock wins first (fewer separate visits for a big order), price only breaks ties.
    private string? _bestSourceStation;
    public string? BestSourceStation
    {
        get => _bestSourceStation;
        set => SetProperty(ref _bestSourceStation, value);
    }

    // The two configured hub systems each have several stations, so the station name alone doesn't
    // say which system to fly to - this is what makes that unambiguous.
    private string? _bestSourceSystem;
    public string? BestSourceSystem
    {
        get => _bestSourceSystem;
        set => SetProperty(ref _bestSourceSystem, value);
    }

    private int? _bestSourcePrice;
    public int? BestSourcePrice
    {
        get => _bestSourcePrice;
        set => SetProperty(ref _bestSourcePrice, value);
    }

    private int? _bestSourceStock;
    public int? BestSourceStock
    {
        get => _bestSourceStock;
        set => SetProperty(ref _bestSourceStock, value);
    }

    // True = orbital station (Assets/Orbital.png), false = surface settlement (Assets/Planetary.png),
    // null = no best source yet. Looked up from AppSettings.HubSystems by MainViewModel.
    private bool? _bestSourceIsOrbital;
    public bool? BestSourceIsOrbital
    {
        get => _bestSourceIsOrbital;
        set => SetProperty(ref _bestSourceIsOrbital, value);
    }

    public void ClearBestSource()
    {
        BestSourceStation = null;
        BestSourceSystem = null;
        BestSourcePrice = null;
        BestSourceStock = null;
        BestSourceIsOrbital = null;
    }

    public void SetBestSource(string station, string system, int price, int stock, bool isOrbital)
    {
        BestSourceStation = station;
        BestSourceSystem = system;
        BestSourcePrice = price;
        BestSourceStock = stock;
        BestSourceIsOrbital = isOrbital;
    }
}
