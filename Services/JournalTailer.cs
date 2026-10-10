using System.IO;
using System.Text;
using System.Text.Json;

namespace ColonyTracker.Services;

/// <summary>
/// Tails the newest Journal.*.log file in a Saved Games\...\Elite Dangerous folder, raising
/// EventReceived once per parsed JSON line and following file rotation as new game sessions
/// start. Ported from the proven WineRunner/edminingbook journalTailer.ts pattern: poll on a
/// timer as the reliable path, with FileSystemWatcher as a low-latency nudge (Windows can drop
/// watcher events under rapid file churn, so it's never relied on alone).
///
/// All file access is serialized behind a single non-reentrant lock: unlike the single-threaded
/// JS original, the timer and the watcher callback can fire concurrently here, and both mutate
/// the same read-offset state.
/// </summary>
public sealed class JournalTailer : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(3);

    // A real journal grows by at most a few KB between poll ticks - this just bounds a single
    // allocation against an anomalous/corrupt file, not normal use. Any excess beyond one chunk is
    // picked up on the next poll tick, since _offset only ever advances by what was actually read.
    private const int MaxChunkBytes = 10 * 1024 * 1024;

    public event Action<JsonElement>? EventReceived;
    public event Action<Exception>? ErrorOccurred;

    private readonly string _journalDir;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();

    private string? _currentFile;
    private long _offset;
    private string _pendingLine = "";
    private Timer? _pollTimer;
    private FileSystemWatcher? _watcher;

    public JournalTailer(string journalDir, TimeSpan? pollInterval = null)
    {
        _journalDir = journalDir;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public void Start()
    {
        Poll();
        _pollTimer = new Timer(_ => Poll(), null, _pollInterval, _pollInterval);
        try
        {
            _watcher = new FileSystemWatcher(_journalDir, "Journal.*.log")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += (_, _) => Poll();
            _watcher.Created += (_, _) => Poll();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();

    /// <summary>Forces the next read to replay the current journal file from the very beginning,
    /// as if it had just been discovered - a manual recovery path for a user-suspected missed
    /// event (the file watcher can drop events under rapid file churn, and while the poll timer
    /// is a fallback, it only picks up from wherever the offset already is, not from scratch).</summary>
    public void Rescan()
    {
        lock (_gate)
        {
            _currentFile = null;
            _offset = 0;
            _pendingLine = "";
        }
        Poll();
    }

    /// <summary>Current read position - which file, and how many bytes into it. Used by callers
    /// that need to persist "how far we've read" across app restarts (see SeedPosition).</summary>
    public (string? File, long Offset) Position { get { lock (_gate) return (_currentFile, _offset); } }

    /// <summary>Resumes reading from a previously-persisted position instead of the very start of
    /// the newest file - call before Start(). If the newest journal file at startup still matches
    /// <paramref name="file"/> (i.e. the same Elite Dangerous session is still being written to),
    /// the next poll continues from <paramref name="offset"/> rather than replaying everything
    /// already read; if a newer file exists instead (a new game session), CheckForNewestFile's
    /// normal file-change handling discards this seed and replays the new file from the top, same
    /// as if this had never been called.</summary>
    public void SeedPosition(string file, long offset)
    {
        lock (_gate)
        {
            _currentFile = file;
            _offset = Math.Max(0, offset);
            _pendingLine = "";
        }
    }

    private void Poll()
    {
        if (!Monitor.TryEnter(_gate)) return; // a check is already in flight; the next tick will catch up
        try
        {
            CheckForNewestFile();
            ReadNewData();
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void CheckForNewestFile()
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(_journalDir, "Journal.*.log");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
            return;
        }

        string? newestPath = null;
        var newestTime = DateTime.MinValue;
        foreach (var path in files)
        {
            try
            {
                var time = File.GetLastWriteTimeUtc(path);
                if (newestPath is null || time > newestTime)
                {
                    newestPath = path;
                    newestTime = time;
                }
            }
            catch
            {
                // file disappeared between enumerate and stat; ignore
            }
        }
        if (newestPath is null) return;

        if (newestPath != _currentFile)
        {
            _currentFile = newestPath;
            _offset = 0;
            _pendingLine = "";
        }
    }

    private void ReadNewData()
    {
        if (_currentFile is null) return;
        try
        {
            using var fs = new FileStream(_currentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = fs.Length;
            if (length < _offset)
            {
                // file was replaced/truncated unexpectedly (new session); restart from the top
                _offset = 0;
                _pendingLine = "";
            }
            if (length <= _offset) return;

            fs.Seek(_offset, SeekOrigin.Begin);
            var toRead = (int)Math.Min(length - _offset, MaxChunkBytes);
            var buffer = new byte[toRead];
            var totalRead = 0;
            int read;
            while (totalRead < buffer.Length && (read = fs.Read(buffer, totalRead, buffer.Length - totalRead)) > 0)
                totalRead += read;
            _offset += totalRead;

            var chunk = _pendingLine + Encoding.UTF8.GetString(buffer, 0, totalRead);
            var lines = chunk.Split('\n');
            // The last split segment may be an incomplete line if we read mid-write; hold onto
            // it and prepend it to the next read instead of parsing it now.
            _pendingLine = lines[^1];
            for (var i = 0; i < lines.Length - 1; i++)
                HandleLine(lines[i]);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    private void HandleLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            EventReceived?.Invoke(doc.RootElement.Clone());
        }
        catch
        {
            // shouldn't happen for a complete line, but a torn write is possible; skip it
        }
    }
}
