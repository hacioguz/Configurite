using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Configurite.Audit;

/// <summary>
/// EN: SQLite-backed implementation of <see cref="IConfiguriteAuditLog"/>. Stateless across calls.
/// TR: <see cref="IConfiguriteAuditLog"/>'un SQLite tabanlı uygulaması. Çağrılar arasında stateless.
/// </summary>
public sealed class SqliteConfiguriteAuditLog : IConfiguriteAuditLog
{
    private readonly string _connectionString;

    /// <summary>
    /// EN: Creates an audit log targeting the given SQLite database.
    /// TR: Verilen SQLite veritabanını hedefleyen bir denetim günlüğü oluşturur.
    /// </summary>
    public SqliteConfiguriteAuditLog(string databasePath)
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
    public void Record(string operation, string? key, string? environment, string? user)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AuditLog (Operation, Key, Environment, User, Timestamp)
            VALUES ($op, $key, $env, $user, datetime('now'));
            """;
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$key", (object?)key ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$env", (object?)environment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$user", (object?)user ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<AuditEntry> ReadRecent(int limit, int offset = 0)
    {
        if (limit <= 0)
        {
            return Array.Empty<AuditEntry>();
        }

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Operation, Key, Environment, User, Timestamp
            FROM AuditLog
            ORDER BY Id DESC
            LIMIT $limit OFFSET $offset;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));

        var result = new List<AuditEntry>(capacity: Math.Min(limit, 256));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new AuditEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTime.SpecifyKind(
                    DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    DateTimeKind.Utc)));
        }

        return result;
    }

    /// <inheritdoc />
    public long Count()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AuditLog;";
        return (long)cmd.ExecuteScalar()!;
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
