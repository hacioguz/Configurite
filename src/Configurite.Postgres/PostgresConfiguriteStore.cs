using Configurite.Storage;
using Npgsql;

namespace Configurite.Postgres;

/// <summary>
/// EN: PostgreSQL implementation of <see cref="IConfiguriteStore"/>. Use this when you need a
///     centrally-managed, multi-instance configuration backend instead of the per-host SQLite file.
///     The schema mirrors the SQLite layout: <c>configuration</c> + <c>metadata</c> + <c>audit_log</c>.
/// TR: <see cref="IConfiguriteStore"/> arayüzünün PostgreSQL uygulaması. Host başına SQLite
///     dosyası yerine merkezî, çok-instance yapılandırma arka ucuna ihtiyacınız olduğunda kullanın.
///     Şema SQLite düzenini yansıtır: <c>configuration</c> + <c>metadata</c> + <c>audit_log</c>.
/// </summary>
public sealed class PostgresConfiguriteStore : IConfiguriteStore
{
    private readonly string _connectionString;
    private readonly string _schemaName;

    /// <summary>
    /// EN: Creates a store using the given Npgsql connection string. Tables live under
    ///     <paramref name="schemaName"/> (default <c>configurite</c>) — qualifying every query
    ///     with the schema avoids surprises when multiple apps share a database.
    /// TR: Verilen Npgsql bağlantı dizesiyle bir store oluşturur. Tablolar
    ///     <paramref name="schemaName"/> şeması altında (varsayılan <c>configurite</c>) yer alır;
    ///     her sorguyu şema ile niteleyerek birden çok uygulamanın aynı veritabanını paylaştığında
    ///     sürpriz yaşamayız.
    /// </summary>
    public PostgresConfiguriteStore(string connectionString, string schemaName = "configurite")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        _connectionString = connectionString;
        _schemaName = schemaName;
    }

    private string Q(string table) => $"\"{_schemaName}\".\"{table}\"";

    /// <inheritdoc />
    public void EnsureSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE SCHEMA IF NOT EXISTS "{_schemaName}";

            CREATE TABLE IF NOT EXISTS {Q("configuration")} (
                id           BIGSERIAL    PRIMARY KEY,
                key          TEXT         NOT NULL,
                value        TEXT         NOT NULL,
                is_encrypted BOOLEAN      NOT NULL DEFAULT FALSE,
                environment  TEXT,
                created_utc  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_utc  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (key, environment)
            );

            CREATE INDEX IF NOT EXISTS ix_configuration_environment
                ON {Q("configuration")} (environment);

            CREATE TABLE IF NOT EXISTS {Q("metadata")} (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {Q("audit_log")} (
                id          BIGSERIAL   PRIMARY KEY,
                operation   TEXT        NOT NULL,
                key         TEXT,
                environment TEXT,
                "user"      TEXT,
                timestamp   TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS ix_audit_log_timestamp
                ON {Q("audit_log")} (timestamp);

            INSERT INTO {Q("metadata")} (key, value) VALUES ('SchemaVersion', '2')
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ConfigEntry> ReadAll(string? environment)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT key, value, is_encrypted, environment
            FROM {Q("configuration")}
            WHERE environment IS NULL OR environment = @env
            ORDER BY (environment IS NULL) DESC, environment;
            """;
        cmd.Parameters.AddWithValue("@env", (object?)environment ?? DBNull.Value);

        var result = new Dictionary<string, ConfigEntry>(64, StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = new ConfigEntry(
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }
        return result;
    }

    /// <inheritdoc />
    public bool TryGet(string key, string? environment, out ConfigEntry entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = environment is null
            ? $"SELECT value, is_encrypted, environment FROM {Q("configuration")} WHERE key = @key AND environment IS NULL LIMIT 1;"
            : $"SELECT value, is_encrypted, environment FROM {Q("configuration")} WHERE key = @key AND environment = @env LIMIT 1;";
        cmd.Parameters.AddWithValue("@key", key);
        if (environment is not null)
        {
            cmd.Parameters.AddWithValue("@env", environment);
        }

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            entry = new ConfigEntry(
                reader.GetString(0),
                reader.GetBoolean(1),
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

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Q("configuration")} (key, value, is_encrypted, environment, created_utc, updated_utc)
            VALUES (@key, @value, @enc, @env, now(), now())
            ON CONFLICT (key, environment) DO UPDATE
                SET value = EXCLUDED.value,
                    is_encrypted = EXCLUDED.is_encrypted,
                    updated_utc = now();
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@enc", isEncrypted);
        cmd.Parameters.AddWithValue("@env", (object?)environment ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public int Delete(string key, string? environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = environment is null
            ? $"DELETE FROM {Q("configuration")} WHERE key = @key AND environment IS NULL;"
            : $"DELETE FROM {Q("configuration")} WHERE key = @key AND environment = @env;";
        cmd.Parameters.AddWithValue("@key", key);
        if (environment is not null)
        {
            cmd.Parameters.AddWithValue("@env", environment);
        }
        return cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public string? ReadMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT value FROM {Q("metadata")} WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }

    /// <inheritdoc />
    public void WriteMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Q("metadata")} (key, value) VALUES (@key, @value)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    private NpgsqlConnection OpenConnection()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
