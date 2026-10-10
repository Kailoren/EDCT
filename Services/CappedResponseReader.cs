using System.IO;
using System.Net.Http;

namespace ColonyTracker.Services;

/// <summary>
/// Reads an HTTP response body as text up to a byte cap, aborting before the cap is exceeded
/// rather than buffering an unbounded amount first and discarding it afterward - a response
/// that declares an oversized Content-Length is rejected immediately; one that doesn't declare a
/// length (or under-declares it) is still bounded because the read loop itself stops as soon as
/// the running total would exceed the cap, never allocating past it.
/// </summary>
public static class CappedResponseReader
{
    public static async Task<string?> ReadTextAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct = default)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
