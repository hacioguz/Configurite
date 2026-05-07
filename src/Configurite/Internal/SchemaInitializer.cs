using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Configurite.Internal;

/// <summary>
/// EN: Creates the Configurite schema (tables, indexes, metadata) on a SQLite database.
///     Idempotent — safe to run on every startup.
/// TR: Configurite şemasını (tablolar, indeksler, meta veri) SQLite veritabanında oluşturur.
///     Idempotent — her başlatmada güvenle çağrılabilir.
/// </summary>
internal static class SchemaInitializer
{
    /// <summary>
    /// EN: Current Configurite schema version. Bumped when the schema changes.
    /// TR: Geçerli Configurite şema sürümü. Şema değiştiğinde artırılır.
    /// </summary>
    internal const int CurrentSchemaVersion = 2;

    private const string CreateConfigurationTable = """
        CREATE TABLE IF NOT EXISTS Configuration (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Key         TEXT    NOT NULL,
            Value       TEXT    NOT NULL,
            IsEncrypted INTEGER NOT NULL DEFAULT 0,
            Environment TEXT,
            CreatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
            UpdatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
            UNIQUE(Key, Environment)
        );
        """;

    private const string CreateEnvironmentIndex =
        "CREATE INDEX IF NOT EXISTS IX_Configuration_Environment ON Configuration(Environment);";

    private const string CreateMetadataTable = """
        CREATE TABLE IF NOT EXISTS Metadata (
            Key   TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;

    // Schema v2 — added in Configurite 8.4 / 9.4 / 10.4. Idempotent CREATE so v1 databases
    // upgrade silently on the next EnsureSchema() call.
    // Şema v2 — Configurite 8.4 / 9.4 / 10.4'te eklendi. Idempotent CREATE; v1 veritabanları
    // bir sonraki EnsureSchema() çağrısında sessizce yükselir.
    private const string CreateAuditLogTable = """
        CREATE TABLE IF NOT EXISTS AuditLog (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Operation   TEXT    NOT NULL,
            Key         TEXT,
            Environment TEXT,
            User        TEXT,
            Timestamp   TEXT    NOT NULL DEFAULT (datetime('now'))
        );
        """;

    private const string CreateAuditLogTimestampIndex =
        "CREATE INDEX IF NOT EXISTS IX_AuditLog_Timestamp ON AuditLog(Timestamp);";

    /// <summary>
    /// EN: Creates the schema on the supplied open connection inside a single transaction.
    /// TR: Verilen açık bağlantı üzerinde, tek bir işlem (transaction) içinde şemayı oluşturur.
    /// </summary>
    /// <param name="connection">
    /// EN: An open <see cref="SqliteConnection"/>.
    /// TR: Açık bir <see cref="SqliteConnection"/> nesnesi.
    /// </param>
    public static void Initialize(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var transaction = connection.BeginTransaction();

        Execute(connection, transaction, CreateConfigurationTable);
        Execute(connection, transaction, CreateEnvironmentIndex);
        Execute(connection, transaction, CreateMetadataTable);
        Execute(connection, transaction, CreateAuditLogTable);
        Execute(connection, transaction, CreateAuditLogTimestampIndex);

        UpsertMetadata(connection, transaction, "SchemaVersion", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));

        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void UpsertMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Metadata (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
}
