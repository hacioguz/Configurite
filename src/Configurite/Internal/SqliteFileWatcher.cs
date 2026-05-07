namespace Configurite.Internal;

/// <summary>
/// EN: Watches a SQLite database file (and its -journal / -wal / -shm sidecars) for changes
///     and invokes a callback after a short debounce window. Thread-safe.
/// TR: Bir SQLite veritabanı dosyasını (ve -journal / -wal / -shm yan dosyalarını) değişiklikler
///     için izler; kısa bir debounce penceresi sonrası geri çağrımı tetikler. Thread-safe'dir.
/// </summary>
internal sealed class SqliteFileWatcher : IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly FileSystemWatcher _watcher;
    private readonly Action _onChanged;
    private readonly Timer _debounceTimer;
    private readonly TimeSpan _debounce;
    private readonly string _baseFileName;
#if NET9_0_OR_GREATER
    // EN: net9.0+ ships System.Threading.Lock — better codegen than the legacy Monitor-on-object pattern.
    // TR: net9.0+ System.Threading.Lock'u getirir — eski Monitor-on-object desenine göre daha iyi codegen.
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private bool _pending;
    private bool _disposed;

    /// <summary>
    /// EN: Creates a watcher for <paramref name="databasePath"/>.
    /// TR: <paramref name="databasePath"/> için bir izleyici oluşturur.
    /// </summary>
    public SqliteFileWatcher(string databasePath, Action onChanged, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(onChanged);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Database path must include a directory component.", nameof(databasePath));
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _baseFileName = Path.GetFileName(databasePath);
        _onChanged = onChanged;
        _debounce = debounce ?? DefaultDebounce;
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(directory)
        {
            // EN: No filter — SQLite writes to .db plus -journal / -wal / -shm sidecars.
            // TR: Filtre yok — SQLite hem .db hem de -journal / -wal / -shm yan dosyalarına yazar.
            NotifyFilter = NotifyFilters.LastWrite
                         | NotifyFilters.CreationTime
                         | NotifyFilters.Size
                         | NotifyFilters.FileName,
            EnableRaisingEvents = false,
        };

        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.Deleted += OnFileEvent;

        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileEvent(object? sender, FileSystemEventArgs e)
    {
        if (!IsRelevant(e.Name))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = true;
            _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private bool IsRelevant(string? changedName)
    {
        if (string.IsNullOrEmpty(changedName))
        {
            return false;
        }

        // Match base file name and known SQLite sidecars.
        // Temel dosya adı ve bilinen SQLite yan dosyalarıyla eşleştir.
        return changedName.Equals(_baseFileName, StringComparison.OrdinalIgnoreCase)
            || changedName.Equals(_baseFileName + "-journal", StringComparison.OrdinalIgnoreCase)
            || changedName.Equals(_baseFileName + "-wal", StringComparison.OrdinalIgnoreCase)
            || changedName.Equals(_baseFileName + "-shm", StringComparison.OrdinalIgnoreCase);
    }

    private void OnDebounceElapsed(object? state)
    {
        bool shouldFire;
        lock (_gate)
        {
            if (_disposed || !_pending)
            {
                return;
            }

            _pending = false;
            shouldFire = true;
        }

        if (shouldFire)
        {
            try
            {
                _onChanged();
            }
            catch
            {
                // EN: Swallow exceptions inside the callback — the watcher must keep working.
                // TR: Geri çağrım içindeki istisnaları yut — izleyici çalışmaya devam etmeli.
            }
        }
    }

    /// <summary>
    /// EN: Test seam — fires the debounced callback immediately if a change is pending.
    /// TR: Test için kanca — bekleyen bir değişiklik varsa debounce'lu geri çağrımı hemen tetikler.
    /// </summary>
    internal void TriggerForTesting() => OnDebounceElapsed(null);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileEvent;
        _watcher.Created -= OnFileEvent;
        _watcher.Renamed -= OnFileEvent;
        _watcher.Deleted -= OnFileEvent;
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
