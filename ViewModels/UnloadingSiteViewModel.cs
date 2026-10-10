namespace ColonyTracker.ViewModels;

public sealed record RemainingCommodity(string Name, int Remaining);

/// <summary>
/// One active, not yet completed construction slot's live delivery status for the overlay's Unloading mode -
/// correlated to a live-tracked depot by matching the slot's own Name against the depot's
/// journal-reported StationName (the same assumption the Raven Colonial import already relies on:
/// the player's own custom site name becomes the station's actual in-game name once built).
/// HasLiveData is false until the player has actually docked there this session - the journal
/// never reports on a depot that hasn't been visited.
/// </summary>
public sealed class UnloadingSiteViewModel(string displayName, string? body, IReadOnlyList<RemainingCommodity> remainingCommodities, bool hasLiveData)
{
    public string DisplayName { get; } = displayName;
    public string? Body { get; } = body;
    public bool HasLiveData { get; } = hasLiveData;
    public IReadOnlyList<RemainingCommodity> RemainingCommodities { get; } = remainingCommodities;
}
