using Configurite.Internal;
using Microsoft.Data.Sqlite;

namespace Configurite.Encryption;

/// <summary>
/// EN: Rotates the Configurite master key by re-encrypting every <c>IsEncrypted = 1</c> row
///     under a new key + freshly generated salt. The whole operation runs in a single SQLite
///     transaction, so a crash mid-rotation leaves the database fully on the old key.
/// TR: Configurite ana anahtarını rotasyona sokar: her <c>IsEncrypted = 1</c> satırı yeni
///     anahtar + taze üretilmiş salt ile yeniden şifreler. Tüm işlem tek bir SQLite işleminde
///     (transaction) çalışır; ortada bir çökme olursa veritabanı tamamen eski anahtarda kalır.
/// </summary>
public sealed class ConfiguriteKeyRotator
{
    private readonly string _connectionString;

    /// <summary>
    /// EN: Creates a rotator targeting <paramref name="databasePath"/>.
    /// TR: <paramref name="databasePath"/> hedefli bir rotator oluşturur.
    /// </summary>
    public ConfiguriteKeyRotator(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    /// <summary>
    /// EN: Performs an atomic master-key rotation. All encrypted rows are decrypted with
    ///     <paramref name="oldMasterKey"/> and re-encrypted with <paramref name="newMasterKey"/>
    ///     under a fresh per-database salt. If any row fails to decrypt or re-encrypt, the
    ///     transaction is rolled back and the database is unchanged.
    /// TR: Atomik bir ana anahtar rotasyonu yapar. Tüm şifreli satırlar
    ///     <paramref name="oldMasterKey"/> ile çözülür ve <paramref name="newMasterKey"/> +
    ///     yeni veritabanı saltı ile yeniden şifrelenir. Herhangi bir satır çözülmez veya
    ///     yeniden şifrelenmezse işlem geri alınır ve veritabanı değişmez.
    /// </summary>
    public KeyRotationResult Rotate(string oldMasterKey, string newMasterKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldMasterKey);
        ArgumentException.ThrowIfNullOrEmpty(newMasterKey);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var oldSalt = ReadExistingSalt(connection)
            ?? throw new InvalidOperationException(
                "Database does not have an encryption salt yet — nothing has been encrypted, so there is nothing to rotate.");

        // EN: Snapshot all encrypted rows up-front.
        // TR: Tüm şifreli satırları önce belleğe al.
        var encryptedRows = ReadEncryptedRows(connection);

        if (encryptedRows.Count == 0)
        {
            // EN: Nothing to rotate; we still update the salt so the next encrypt uses the new key cleanly.
            // TR: Rotasyon edilecek bir şey yok; bir sonraki şifrelemenin temiz başlaması için yine de salt'ı yenileriz.
            using var emptyTx = connection.BeginTransaction();
            var freshSalt = AesGcmConfigEncryptor.GenerateSalt();
            WriteSalt(connection, emptyTx, freshSalt);
            emptyTx.Commit();
            return new KeyRotationResult(RowsRotated: 0);
        }

        using var oldEncryptor = new AesGcmConfigEncryptor(oldMasterKey, oldSalt);

        // EN: Decrypt outside the write transaction — a failure here is recoverable and never touches the DB.
        // TR: Yazma işleminin dışında çöz — buradaki hata kurtarılabilir ve DB'ye dokunmaz.
        var plaintexts = new List<(long Id, string Plaintext)>(encryptedRows.Count);
        foreach (var (id, ciphertext) in encryptedRows)
        {
            plaintexts.Add((id, oldEncryptor.Decrypt(ciphertext)));
        }

        // EN: Now write everything atomically.
        // TR: Şimdi her şeyi atomik olarak yaz.
        using var transaction = connection.BeginTransaction();

        var newSalt = AesGcmConfigEncryptor.GenerateSalt();
        WriteSalt(connection, transaction, newSalt);

        using var newEncryptor = new AesGcmConfigEncryptor(newMasterKey, newSalt);

        foreach (var (id, plaintext) in plaintexts)
        {
            UpdateRowValue(connection, transaction, id, newEncryptor.Encrypt(plaintext));
        }

        transaction.Commit();

        return new KeyRotationResult(RowsRotated: plaintexts.Count);
    }

    private static byte[]? ReadExistingSalt(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Metadata WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", EncryptionMetadata.SaltMetadataKey);
        var result = cmd.ExecuteScalar();
        if (result is null or DBNull)
        {
            return null;
        }
        return Convert.FromBase64String((string)result);
    }

    private static List<(long Id, string Ciphertext)> ReadEncryptedRows(SqliteConnection connection)
    {
        var rows = new List<(long, string)>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Value FROM Configuration WHERE IsEncrypted = 1;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }
        return rows;
    }

    private static void WriteSalt(SqliteConnection connection, SqliteTransaction transaction, byte[] salt)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Metadata (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", EncryptionMetadata.SaltMetadataKey);
        cmd.Parameters.AddWithValue("$value", Convert.ToBase64String(salt));
        cmd.ExecuteNonQuery();
    }

    private static void UpdateRowValue(SqliteConnection connection, SqliteTransaction transaction, long id, string newCiphertext)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE Configuration
            SET Value = $value, UpdatedUtc = datetime('now')
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$value", newCiphertext);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// EN: Summary of a successful key rotation.
/// TR: Başarılı bir anahtar rotasyonunun özeti.
/// </summary>
/// <param name="RowsRotated">
/// EN: Number of encrypted rows re-encrypted under the new master key.
/// TR: Yeni ana anahtar altında yeniden şifrelenen şifreli satır sayısı.
/// </param>
public sealed record KeyRotationResult(int RowsRotated);
