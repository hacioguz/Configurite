using System.Diagnostics;
using Configurite.Diagnostics;
using Configurite.Encryption;
using Configurite.Internal;
using Configurite.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Configurite;

/// <summary>
/// EN: Reads configuration key-value pairs from a SQLite database into the .NET configuration system.
///     Decrypts AES-256-GCM-protected values transparently when a master key is available.
/// TR: SQLite veritabanından yapılandırma anahtar-değer çiftlerini .NET yapılandırma sistemine aktarır.
///     Ana anahtar mevcutsa AES-256-GCM ile korunan değerleri şeffafça çözer.
/// </summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly ConfiguriteOptions _options;
    private readonly SqliteConfiguriteStore _store;
    private readonly Lazy<AesGcmConfigEncryptor?> _encryptor;
    private readonly ILogger _logger;
    private SqliteFileWatcher? _watcher;
    private bool _disposed;

    /// <summary>
    /// EN: Creates a new provider instance, ensuring the schema exists when allowed by options.
    /// TR: Yeni bir sağlayıcı örneği oluşturur; seçenekler izin verdiğinde şemayı hazırlar.
    /// </summary>
    /// <param name="options">
    /// EN: The Configurite options.
    /// TR: Configurite seçenekleri.
    /// </param>
    public SqliteConfigurationProvider(ConfiguriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = options.ResolveLogger();

        var dbPath = options.ResolveDatabasePath();

        if (!File.Exists(dbPath))
        {
            if (!options.CreateIfMissing && !options.Optional)
            {
                throw new FileNotFoundException(
                    $"Configurite database not found and CreateIfMissing is false: {dbPath}",
                    dbPath);
            }

            if (options.CreateIfMissing)
            {
                EnsureDirectory(dbPath);
            }
        }

        _store = new SqliteConfiguriteStore(dbPath);

        if (options.CreateIfMissing || File.Exists(dbPath))
        {
            _store.EnsureSchema();
        }

        _encryptor = new Lazy<AesGcmConfigEncryptor?>(BuildEncryptor, isThreadSafe: true);

        if (options.ReloadOnChange)
        {
            _watcher = new SqliteFileWatcher(dbPath, OnFileChanged);
        }
    }

    private void OnFileChanged()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Load();
            OnReload();
            ConfiguriteTelemetry.ReloadsTotal.Add(1);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var dbPath = _options.ResolveDatabasePath();
                LogMessages.LogReloadCompleted(_logger, dbPath);
            }
        }
        catch (Exception ex)
        {
            // EN: A failed reload must not crash the watcher.
            // TR: Başarısız bir yeniden yükleme izleyiciyi çökertmemeli.
            ConfiguriteTelemetry.WatcherErrorsTotal.Add(1);
            LogMessages.LogReloadFailed(_logger, ex);
        }
    }

    /// <summary>
    /// EN: Test seam — synchronously triggers the watcher's debounced callback path.
    /// TR: Test için kanca — izleyicinin debounce'lu geri çağrım yolunu senkron tetikler.
    /// </summary>
    internal void TriggerReloadForTesting() => OnFileChanged();

    /// <summary>
    /// EN: Public read/write store wrapping the same database the provider loads from.
    /// TR: Provider'ın yüklediği veritabanını saran public okuma/yazma store'u.
    /// </summary>
    public IConfiguriteStore Store => _store;

    /// <summary>
    /// EN: Lazily-resolved encryptor; <see langword="null"/> when no master key is available.
    /// TR: İhtiyaç hâlinde çözümlenen encryptor; ana anahtar yoksa <see langword="null"/>.
    /// </summary>
    internal AesGcmConfigEncryptor? Encryptor => _encryptor.Value;

    /// <summary>
    /// EN: Loads all applicable configuration rows from the SQLite database, decrypting as needed.
    /// TR: SQLite veritabanından geçerli tüm yapılandırma satırlarını yükler; gerekirse çözer.
    /// </summary>
    public override void Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = ConfiguriteTelemetry.ActivitySource.StartActivity("Configurite.Load");
        var stopwatch = Stopwatch.StartNew();
        ConfiguriteTelemetry.LoadsTotal.Add(1);

        var dbPath = _options.ResolveDatabasePath();
        activity?.SetTag("configurite.database", dbPath);
        activity?.SetTag("configurite.environment", _options.Environment ?? "(global)");

        if (!File.Exists(dbPath))
        {
            if (_options.Optional)
            {
                LogMessages.LogOptionalMissing(_logger, dbPath);
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            LogMessages.LogDatabaseMissing(_logger, dbPath);
            throw new FileNotFoundException(
                $"Configurite database not found: {dbPath}",
                dbPath);
        }

        var entries = _store.ReadAll(_options.Environment);
        var data = new Dictionary<string, string?>(entries.Count, StringComparer.OrdinalIgnoreCase);
        var decrypted = 0;

        foreach (var (key, entry) in entries)
        {
            if (entry.IsEncrypted)
            {
                var enc = _encryptor.Value
                    ?? throw new InvalidOperationException(
                        $"Configuration key '{key}' is encrypted but no master key was supplied. " +
                        $"Set ConfiguriteOptions.MasterKey, environment variable {MasterKeyResolver.EnvironmentVariableName}, " +
                        $"or place a key at {MasterKeyResolver.DefaultKeyFilePath()}.");

                data[key] = enc.Decrypt(entry.Value);
                decrypted++;
            }
            else
            {
                data[key] = entry.Value;
            }
        }

        Data = data;
        stopwatch.Stop();

        if (decrypted > 0)
        {
            ConfiguriteTelemetry.DecryptionsTotal.Add(decrypted);
        }
        ConfiguriteTelemetry.LoadDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);

        activity?.SetTag("configurite.rows.total", entries.Count);
        activity?.SetTag("configurite.rows.decrypted", decrypted);

        LogMessages.LogLoadCompleted(_logger, entries.Count, decrypted, dbPath, stopwatch.Elapsed.TotalMilliseconds);
    }

    private AesGcmConfigEncryptor? BuildEncryptor()
        => ConfiguriteEncryption.TryCreateEncryptor(_store, _options.MasterKey);

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// EN: Disposes the encryptor (zeroing the derived data key).
    /// TR: Encryptor'ı serbest bırakır (türetilmiş veri anahtarını sıfırlar).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _watcher?.Dispose();
        _watcher = null;

        if (_encryptor.IsValueCreated)
        {
            _encryptor.Value?.Dispose();
        }

        _disposed = true;
    }
}
