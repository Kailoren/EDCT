using System.Net.Http;
using System.Text.RegularExpressions;

namespace ColonyTracker.Services;

/// <summary>
/// Fetches a single tab of a Google Sheet as plain TSV via its public "export" endpoint - works for
/// any sheet shared "Anyone with the link" (Viewer), no API key/OAuth/service account needed. This
/// returns the same tab-separated shape SheetPasteParser already parses from a clipboard paste, so
/// that parser needed no changes at all.
/// </summary>
public static class GoogleSheetImportService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Generous for even a large real sheet tab - this exists to bound a pathological response
    // (or an unexpectedly enormous export), not normal use.
    private const long MaxResponseBytes = 10 * 1024 * 1024;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Regex IdPattern = new(@"/spreadsheets/d/([a-zA-Z0-9-_]+)", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex GidPattern = new(@"[?&#]gid=(\d+)", RegexOptions.Compiled, RegexTimeout);

    public static async Task<(string? Text, string? Error)> FetchTabAsync(string sheetUrl)
    {
        var idMatch = IdPattern.Match(sheetUrl);
        if (!idMatch.Success)
            return (null, "That doesn't look like a Google Sheets link - couldn't find a spreadsheet ID in it.");

        // gid identifies which tab within the spreadsheet; Google shows it in the URL as either
        // ?gid=... or #gid=... depending on how the link was copied. Defaults to 0 (the first tab)
        // if the link doesn't name one.
        var gid = GidPattern.Match(sheetUrl) is { Success: true } gidMatch ? gidMatch.Groups[1].Value : "0";
        var exportUrl = $"https://docs.google.com/spreadsheets/d/{idMatch.Groups[1].Value}/export?format=tsv&gid={gid}";

        try
        {
            using var response = await Http.GetAsync(exportUrl);

            // A sheet that isn't shared "Anyone with the link" doesn't fail with an error status -
            // Google redirects to an HTML sign-in/access page instead (still a 200), so content type
            // is the real signal, not just IsSuccessStatusCode. Checked before reading the body so a
            // large error page isn't buffered pointlessly either.
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return (null, "Couldn't read that sheet - make sure its sharing setting is \"Anyone with the link\" (Viewer), not restricted to specific people.");

            var body = await CappedResponseReader.ReadTextAsync(response, MaxResponseBytes);
            if (body is null)
                return (null, "That sheet's export was too large to read safely - try a smaller tab.");

            return (body, null);
        }
        catch (Exception ex)
        {
            return (null, $"Couldn't reach Google Sheets:\n\n{ex.Message}");
        }
    }
}
