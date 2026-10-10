using System.IO;
using System.Text.Json;

namespace ColonyTracker.Services;

/// <summary>
/// Watches Cargo.json in the journal folder (rewritten in full by the game whenever the ship's
/// hold changes) and exposes current contents keyed by normalized commodity name so it lines up
/// with CommodityNameMatcher-normalized catalog/journal names. Unlike the append-only journal
/// log, this file is a full snapshot each time, so it's just re-read on change - no offset
/// tracking needed.
/// </summary>
public sealed class ShipCargoReader : IDisposable
{
    private readonly string _path;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;

    public event Action<IReadOnlyDictionary<string, int>>? CargoUpdated;

    public IReadOnlyDictionary<string, int> Current { get; private set; } = new Dictionary<string, int>();

    public ShipCargoReader(string journalDirectory)
    {
        _path = Path.Combine(journalDirectory, "Cargo.json");
    }

    public void Start()
    {
        ReadNow();
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            _watcher = new FileSystemWatcher(dir, "Cargo.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += (_, _) => ReadNow();
            _watcher.Created += (_, _) => ReadNow();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // Cargo.json watching is a bonus feature; if it can't be set up, "stored on ship"
            // just stays at whatever was last read (or empty) and the user can enter it manually.
        }
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();

    private void ReadNow()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (!File.Exists(_path)) return;

            // The game can be mid-write when the watcher fires; a couple of short retries
            // absorbs that without needing real locking coordination with an external process.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var doc = JsonDocument.Parse(fs);
                    var result = new Dictionary<string, int>();
                    if (doc.RootElement.TryGetProperty("Inventory", out var inv) && inv.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in inv.EnumerateArray())
                        {
                            var name = item.TryGetProperty("Name_Localised", out var nl) ? nl.GetString()
                                : item.TryGetProperty("Name", out var n) ? n.GetString() : null;
                            var count = item.TryGetProperty("Count", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
                            if (string.IsNullOrEmpty(name) || count <= 0) continue;

                            var key = CommodityNameMatcher.Normalize(name);
                            result[key] = result.GetValueOrDefault(key) + count;
                        }
                    }
                    Current = result;
                    CargoUpdated?.Invoke(result);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (JsonException)
                {
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }
}
