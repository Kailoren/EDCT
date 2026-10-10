using System.Net;
using System.Net.Http;
using System.Text.Json;
using ColonyTracker.Models;

namespace ColonyTracker.Services;

/// <summary>
/// Queries EDCF.Relay's /hub-listings endpoint - a small dedicated addition to that existing
/// service (already ingesting EDDN commodity/3 messages into a CommodityListings table) that
/// filters by station name in SQL rather than fetching every galaxy-wide listing for the
/// requested commodities and filtering client-side. That distinction matters: a common
/// commodity like Aluminium has 30,000+ rows galaxy-wide but only a handful at six named
/// stations, and the unfiltered approach was measured hanging past 30s against production.
///
/// The relay stores commodity names as raw EDDN symbols (lowercase, no spaces/punctuation -
/// e.g. "liquidoxygen", not "Liquid Oxygen"), which is exactly what CommodityNameMatcher.Normalize
/// already produces from the catalog's display names, so no separate name-mapping table is needed
/// here - both the query and the response-matching side reuse the same normalizer already relied
/// on for journal/Cargo.json matching elsewhere in this app.
/// </summary>
public sealed class RelayCommodityService : IDisposable
{
    // Generous even for a maximally abusive response (the endpoint's own commodity/station/system
    // count caps keep a real reply tiny) - this exists to bound a compromised or malicious relay,
    // not normal use.
    private const long MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>Shown beside Search Again when the relay answers 429: too many requests from this
    /// address in the last minute. Its limit is a fixed one-minute window, so a minute is the
    /// longest the wait can be.</summary>
    public const string BusyMessage = "The relay is busy, try again in a minute.";

    private static readonly HubListingsRead NoData = new([], RelayBusy: false);

    // Automatic decompression is what makes the client send Accept-Encoding, and Caddy only
    // gzips a reply when asked. The cap above still holds on the decompressed bytes: the handler
    // hands CappedResponseReader the inflated stream with Content-Length removed, so its read
    // loop is what stops an oversized reply, compressed or not.
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
    })
    { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<HubListingsRead> GetHubListingsAsync(
        string baseUrl, IReadOnlyList<HubSystem> hubs, IEnumerable<string> catalogCommodityNames, CancellationToken ct = default)
    {
        var symbols = catalogCommodityNames
            .Select(CommodityNameMatcher.Normalize)
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        var stationNames = hubs.SelectMany(h => h.Stations.Concat(h.PlanetaryStations)).Distinct().ToList();
        var systemNames = hubs.Select(h => h.SystemName).Distinct().ToList();
        if (symbols.Count == 0 || stationNames.Count == 0 || string.IsNullOrWhiteSpace(baseUrl)) return NoData;

        var url = $"{baseUrl.TrimEnd('/')}/hub-listings" +
                   $"?commodities={Uri.EscapeDataString(string.Join(",", symbols))}" +
                   $"&stations={Uri.EscapeDataString(string.Join(",", stationNames))}" +
                   $"&systems={Uri.EscapeDataString(string.Join(",", systemNames))}";

        List<HubListingDto>? dtos;
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new([], RelayBusy: true);
            var body = await CappedResponseReader.ReadTextAsync(response, MaxResponseBytes, ct);
            dtos = body is null ? null : JsonSerializer.Deserialize<List<HubListingDto>>(body, JsonDataFile.ReadOptions);
        }
        catch
        {
            return NoData; // relay unreachable/timed out/oversized - caller just sees no hub data this round, not a crash
        }
        if (dtos is null) return NoData;

        return new(dtos.Select(d => new CommodityListing(d.Commodity, d.StationName, d.StarSystem, d.BuyPrice, d.Stock)).ToList(), RelayBusy: false);
    }

    public void Dispose() => _http.Dispose();

    private sealed record HubListingDto(string Commodity, string StationName, string StarSystem, int BuyPrice, int Stock);
}
