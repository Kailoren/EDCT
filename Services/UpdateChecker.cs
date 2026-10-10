using System.Net.Http;
using System.Text.Json;

namespace ColonyTracker.Services;

/// <summary>
/// Checks the GitHub Releases API for a newer published version than the one currently running,
/// so the app can say so in a banner instead of relying on people noticing a new release.
/// Ported from FleetFinder's UpdateChecker, with the same rules.
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Kailoren/EDCT/releases/latest";

    /// <summary>Where the banner goes when the API's own <c>html_url</c> is missing or fails
    /// <see cref="SafeReleaseUrl"/>. Constant, so this is always somewhere sane.</summary>
    public const string ReleasesPageUrl = "https://github.com/Kailoren/EDCT/releases/latest";

    private const string ReleasesHost = "github.com";

    /// <summary>Path every release of this project lives under.</summary>
    private const string ReleasesPathPrefix = "/Kailoren/EDCT/";

    /// <summary>Comfortably above any real release payload, and small enough that a hostile or
    /// broken answer cannot be read into memory unbounded. Enforced while reading, by
    /// CappedResponseReader, not just from the declared Content-Length.</summary>
    private const long MaxResponseBytes = 1 * 1024 * 1024;

    /// <summary>A tag is "v1.1.3"-shaped. Anything longer is not a version, and this string is
    /// displayed, so it gets a ceiling rather than being trusted to be short.</summary>
    private const int MaxDisplayVersionLength = 24;

    /// <summary>Ceiling on the whole check, headers and body together. HttpClient.Timeout stops
    /// applying once the headers arrive under ResponseHeadersRead, and a byte cap can only fire on
    /// bytes that do arrive, so a sender that answers and then stalls needs this.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>Most redirects one check will follow, and only within the origin first asked.</summary>
    private const int MaxRedirects = 5;

    private static readonly HttpClient Http = CreateClient();

    public sealed record UpdateInfo(Version Version, string DisplayVersion, string HtmlUrl);

    /// <summary>The version of this build, which a release has to beat to be offered.</summary>
    public static Version RunningVersion => typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    private static HttpClient CreateClient()
    {
        // Redirects are walked by hand in FetchAsync, so each hop is checked before it's followed
        // rather than after HttpClient has already contacted wherever the chain pointed.
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's API rejects requests with no User-Agent header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EDCT-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// Returns info about a newer release, or null if already current or the check failed for any
    /// reason (offline, rate-limited, malformed or oversized response, no releases yet). Never
    /// throws - a failed check should be silently invisible, same as the app's other optional
    /// network call (the hub search).
    /// </summary>
    public static Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) =>
        CheckAsync(ReleasesApiUrl, RunningVersion, ct);

    /// <summary>The check itself, against any releases endpoint and running version. The app only
    /// ever calls it with GitHub's URL and its own version (the overload above); the self-test
    /// points it at a loopback listener.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string releasesApiUrl, Version current, CancellationToken ct = default)
    {
        try
        {
            var json = await FetchAsync(new Uri(releasesApiUrl, UriKind.Absolute), ct).ConfigureAwait(false);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("tag_name", out var tagEl)
                || tagEl.ValueKind != JsonValueKind.String) return null;
            var tag = tagEl.GetString();
            if (string.IsNullOrWhiteSpace(tag)) return null;

            // Release tags are "vX.Y.Z", matching <Version> in ColonyTracker.csproj.
            if (!Version.TryParse(StripVersionPrefix(tag), out var latest)) return null;
            if (latest <= current) return null;

            string htmlUrl = root.TryGetProperty("html_url", out var urlEl)
                && urlEl.ValueKind == JsonValueKind.String
                ? SafeReleaseUrl(urlEl.GetString())
                : ReleasesPageUrl;

            return new UpdateInfo(latest, DisplayVersionFor(tag, latest), htmlUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// GETs the endpoint and returns its body as text, or null when it isn't a usable answer: a
    /// non-success status, a redirect off the origin first asked (or more than MaxRedirects of
    /// them), a content type that isn't text, or a body over MaxResponseBytes.
    /// </summary>
    private static async Task<string?> FetchAsync(Uri requested, CancellationToken ct)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timed.CancelAfter(Deadline);
        var next = requested;

        for (int hop = 0; ; hop++)
        {
            using var response = await Http.GetAsync(next, HttpCompletionOption.ResponseHeadersRead, timed.Token).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null || hop >= MaxRedirects) return null;
                // Relative Locations resolve against the current hop, as a browser does; either way
                // the result has to stay on the origin first asked, so a chain can't walk elsewhere.
                next = location.IsAbsoluteUri ? location : new Uri(next, location);
                if (!SameOrigin(next, requested)) return null;
                continue;
            }

            if (!response.IsSuccessStatusCode) return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !IsTextual(mediaType)) return null;
            return await CappedResponseReader.ReadTextAsync(response, MaxResponseBytes, timed.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Scheme, host and port all match.</summary>
    private static bool SameOrigin(Uri candidate, Uri requested) =>
        string.Equals(candidate.Scheme, requested.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == requested.Port;

    private static bool IsTextual(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes a single leading "v". TrimStart('v', 'V') would remove every leading one, so
    /// "vvvV9.9.9" would parse as a release, and a version this app will offer to send someone to
    /// should be a version and not merely something a lenient parser accepted.
    /// </summary>
    private static string StripVersionPrefix(string tag) =>
        tag.Length > 0 && (tag[0] == 'v' || tag[0] == 'V') ? tag[1..] : tag;

    /// <summary>
    /// Returns <paramref name="value"/> only if it is an absolute https URL into this project's
    /// own GitHub repository; anything else falls back to <see cref="ReleasesPageUrl"/>. This
    /// value is handed to the shell when the banner's Download is clicked, so what it is allowed
    /// to be matters more than that it is non-null. MainViewModel.OpenUpdate checks the same thing
    /// again at the point it opens. The parsed Uri is returned rather than the original string, so
    /// the value that was checked is the value that gets used.
    /// </summary>
    private static string SafeReleaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ReleasesPageUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return ReleasesPageUrl;
        if (!IsThisRepository(uri)) return ReleasesPageUrl;
        return uri.AbsoluteUri;
    }

    /// <summary>
    /// True if <paramref name="uri"/> is an https link into this project's own GitHub repository:
    /// exactly github.com (no subdomains), this repository's path, and no user-info segment.
    /// "https://someone.example.com@github.com/..." has github.com as its Host while reading as
    /// another domain, and a release link has no business carrying credentials anyway.
    /// </summary>
    public static bool IsThisRepository(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host.Equals(ReleasesHost, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(ReleasesPathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The tag as shown in the banner, reduced to characters a version tag can contain and capped
    /// at <see cref="MaxDisplayVersionLength"/>. Falls back to the parsed version when nothing
    /// usable survives, so the banner always names something.
    /// </summary>
    private static string DisplayVersionFor(string tag, Version parsed)
    {
        var kept = new string(tag
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '+' || c == '_')
            .Take(MaxDisplayVersionLength)
            .ToArray());

        return string.IsNullOrWhiteSpace(kept) ? "v" + parsed.ToString(3) : kept;
    }
}
