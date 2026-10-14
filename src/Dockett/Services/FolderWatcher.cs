namespace Dockett.Services;

/// <summary>
/// Watches one folder (not its subfolders) and raises <see cref="Changed"/> after things settle.
/// Bursts are coalesced: it fires 250 ms after the last change, but at least once a second while
/// changes keep coming (e.g. a download in progress). Raised on a thread-pool thread.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);

    readonly Lock _gate = new();
    readonly Timer _timer;
    FileSystemWatcher? _watcher;
    DateTime? _pendingSince;
    bool _disposed;

    public FolderWatcher()
    {
        _timer = new Timer(_ => Fire());
    }

    public event Action? Changed;

    public string? Path { get; private set; }

    /// <summary>Starts watching <paramref name="path"/>, replacing any previous folder. Unwatchable paths are ignored.</summary>
    public void Watch(string path)
    {
        lock (_gate)
        {
            if (_disposed || string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)) return;
            Stop();
            Path = path;
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                   | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                };
                watcher.Created += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Changed += OnEvent;
                watcher.Renamed += OnEvent;
                watcher.Error += (_, _) => Poke(); // buffer overflow or the folder went away: just reload
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                _watcher = null; // e.g. a folder that no longer exists, or a share that doesn't support notifications
            }
        }
    }

    void OnEvent(object sender, FileSystemEventArgs e) => Poke();

    void Poke()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            _pendingSince ??= now;
            var untilMax = MaxWait - (now - _pendingSince.Value);
            var due = untilMax < Quiet ? untilMax : Quiet;
            _timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    void Fire()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pendingSince = null;
        }
        Changed?.Invoke();
    }

    void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _pendingSince = null;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
        _timer.Dispose();
    }
}
