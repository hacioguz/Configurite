using Configurite.Internal;
using Microsoft.Data.Sqlite;

namespace Configurite.Storage;

/// <summary>
/// EN: SQLite-backed implementation of <see cref="IConfiguriteStore"/>. Opens a fresh connection
///     per operation; safe to share across threads.
/// TR: <see cref="IConfiguriteStore"/>'un SQLite tabanlı uygulaması. Her operasyon için yeni
///     bağlantı açar; thread'ler arası güvenle paylaşılabilir.
/// </summary>
public sealed class SqliteConfiguriteStore : IConfiguriteStore
{
    private readonly string _connectionString;

    /// <summary>
    /// EN: Creates a store targeting the given SQLite database file.
    /// TR: Verilen SQLite veritabanı dosyasını hedefleyen bir store oluşturur.
    /// </summary>
    public SqliteConfiguriteStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    /// <inheritdoc />
    public void EnsureSchema()
    {
        using var connection = OpenConnection();
        // Apply persistent PRAGMAs once during schema setup. WAL mode survives across
        // connections (it's stored in the database file itself), so subsequent reads/writes
        // benefit without per-connection PRAGMA overhead.
        // Kalıcı PRAGMA'ları şema kurulumunda bir kez uygula. WAL modu bağlantılar arasında
        // kalır (veritabanı dosyasında saklanır), sonraki okumalar/yazmalar bağlantı başına
        // PRAGMA overhead'i olmadan yararlanır.
        ApplyPersistentPragmas(connection);
        SchemaInitializer.Initialize(connection);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ConfigEntry> ReadAll(string? environment)
    {
        using var connection = OpenConnection();

        // Pre-size the dictionary with a generous starting capacity. Most app configs are
        // < 64 entries; resizes from 64 to higher are rare and cheap. Avoiding an extra
        // SELECT COUNT(*) round-trip is more important than perfect sizing.
        // Sözlüğü cömert bir başlangıç kapasitesiyle önceden boyutlandır. Çoğu uygulama
        // konfigürasyonu < 64 girdi; 64'ten üstüne resize nadir ve ucuzdur. Ekstra bir
        // SELECT COUNT(*) round-trip'inden kaçınmak, mükemmel boyutlandırmadan önemlidir.
        var result = new Dictionary<string, ConfigEntry>(capacity: 64, StringComparer.OrdinalIgnoreCase);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Key, Value, IsEncrypted, Environment
            FROM Configuration
            WHERE Environment IS NULL OR Environment = $env
            ORDER BY (Environment IS NULL) DESC, Environment;
            """;
        cmd.Parameters.AddWithValue("$env", (object?)environment ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Globals come first per ORDER BY; environment-specific rows overwrite them.
            // Globaller önce gelir; ortama özgü satırlar globalleri ezer.
            result[reader.GetString(0)] = new ConfigEntry(
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }

        return result;
    }

    /// <inheritdoc />
    public bool TryGet(string key, string? environment, out ConfigEntry entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = environment is null
            ? """
              SELECT Value, IsEncrypted, Environment FROM Configuration
              WHERE Key = $key AND Environment IS NULL LIMIT 1;
              """
            : """
              SELECT Value, IsEncrypted, Environment FROM Configuration
              WHERE Key = $key AND Environment = $env LIMIT 1;
              """;
        cmd.Parameters.AddWithValue("$key", key);
        if (environment is not null)
        {
            cmd.Parameters.AddWithValue("$env", environment);
        }

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            entry = new ConfigEntry(
                reader.GetString(0),
                reader.GetInt64(1) != 0,
                reader.IsDBNull(2) ? null : reader.GetString(2));
            return true;
        }

        entry = default;
        return false;
    }

    /// <inheritdoc />
    public void Upsert(string key, string value, bool isEncrypted, string? environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Configuration (Key, Value, IsEncrypted, Environment, CreatedUtc, UpdatedUtc)
            VALUES ($key, $value, $enc, $env, datetime('now'), datetime('now'))
            ON CONFLICT(Key, Environment) DO UPDATE SET
                Value = excluded.Value,
                IsEncrypted = excluded.IsEncrypted,
                UpdatedUtc = datetime('now');
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$enc", isEncrypted ? 1 : 0);
        cmd.Parameters.AddWithValue("$env", (object?)environment ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public int Delete(string key, string? environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = environment is null
            ? "DELETE FROM Configuration WHERE Key = $key AND Environment IS NULL;"
            : "DELETE FROM Configuration WHERE Key = $key AND Environment = $env;";
        cmd.Parameters.AddWithValue("$key", key);
        if (environment is not null)
        {
            cmd.Parameters.AddWithValue("$env", environment);
        }
        return cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public string? ReadMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Metadata WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);

        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }

    /// <inheritdoc />
    public void WriteMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Metadata (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// EN: Applies SQLite PRAGMAs once during schema setup. <c>journal_mode=WAL</c> is persisted
    ///     into the database file itself, so it survives across connections.
    ///     <c>synchronous=NORMAL</c> is WAL-safe and ~3x faster on writes than FULL — also
    ///     persisted at the database level.
    /// TR: Şema kurulumunda SQLite PRAGMA'larını bir kez uygular. <c>journal_mode=WAL</c>
    ///     veritabanı dosyasında kalıcıdır; bağlantılar arası kalır.
    ///     <c>synchronous=NORMAL</c> WAL-güvenli ve FULL'dan ~3x hızlı — veritabanı düzeyinde kalıcı.
    /// </summary>
    private static void ApplyPersistentPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            """;
        cmd.ExecuteNonQuery();
    }
}
