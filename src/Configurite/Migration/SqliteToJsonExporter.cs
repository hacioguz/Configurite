using System.Text.Encodings.Web;
using System.Text.Json;
using Configurite.Encryption;
using Configurite.Storage;
using Microsoft.Data.Sqlite;

namespace Configurite.Migration;

/// <summary>
/// EN: Reverse migrator: writes a Configurite SQLite database back out as one or more JSON files
///     (one per environment scope). Useful for backups, version-control snapshots, and migrating
///     to another configuration backend.
/// TR: Ters geçirici: Configurite SQLite veritabanını bir veya daha fazla JSON dosyasına geri yazar
///     (her ortam için bir dosya). Yedekler, sürüm kontrolü anlık görüntüleri ve başka bir
///     yapılandırma arka ucuna geçişler için kullanışlıdır.
/// </summary>
public sealed class SqliteToJsonExporter : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteConfiguriteStore _store;
    private AesGcmConfigEncryptor? _encryptor;
    private bool _disposed;

    /// <summary>
    /// EN: Creates an exporter targeting <paramref name="databasePath"/>.
    /// TR: <paramref name="databasePath"/>'i hedefleyen bir dışa aktarıcı oluşturur.
    /// </summary>
    public SqliteToJsonExporter(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _store = new SqliteConfiguriteStore(databasePath);
        _store.EnsureSchema();
    }

    /// <summary>
    /// EN: Exports rows whose environment is null (globals). Equivalent to
    ///     <see cref="ExportToFile"/> with <paramref name="environment"/> = null.
    /// TR: Ortam'ı null olan satırları (globaller) dışa aktarır. <see cref="ExportToFile"/>'ı
    ///     <paramref name="environment"/> = null ile çağırmaya eşdeğerdir.
    /// </summary>
    public ExportResult ExportToFile(string outputPath, ExportOptions? options = null, string? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        options ??= new ExportOptions();
        var rows = environment is null
            ? ReadEverything().Where(r => r.Environment is null)
            : ReadEverything().Where(r => string.Equals(r.Environment, environment, StringComparison.OrdinalIgnoreCase));

        var json = BuildJsonFromRows(rows, options);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, json);

        return new ExportResult(json.Length, environment);
    }

    /// <summary>
    /// EN: Exports the database into one file per environment, mirroring the
    ///     <c>appsettings.json</c> + <c>appsettings.{Env}.json</c> convention used by the migrator.
    /// TR: Veritabanını ortam başına bir dosyaya çıkarır; <c>appsettings.json</c> +
    ///     <c>appsettings.{Env}.json</c> kuralını yansıtır.
    /// </summary>
    public IReadOnlyList<ExportResult> ExportPerEnvironment(string directory, string baseFileName = "appsettings", ExportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Directory.CreateDirectory(directory);
        options ??= new ExportOptions();

        var allRows = ReadEverything();
        var environments = allRows.Where(r => r.Environment is not null)
                                  .Select(r => r.Environment!)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

        var results = new List<ExportResult>();

        var globalPath = Path.Combine(directory, baseFileName + ".json");
        File.WriteAllText(globalPath, BuildJsonFromRows(allRows.Where(r => r.Environment is null), options));
        results.Add(new ExportResult(new FileInfo(globalPath).Length, null));

        foreach (var env in environments)
        {
            var path = Path.Combine(directory, $"{baseFileName}.{env}.json");
            var rows = allRows.Where(r => string.Equals(r.Environment, env, StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(path, BuildJsonFromRows(rows, options));
            results.Add(new ExportResult(new FileInfo(path).Length, env));
        }

        return results;
    }

    private string BuildJsonFromRows(IEnumerable<RawRow> rows, ExportOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            // Build a nested tree from ":"-separated keys, then walk it as JSON.
            // ":" ile ayrılmış anahtarlardan iç içe ağaç kur, sonra JSON olarak yürü.
            var root = new TreeNode();
            foreach (var row in rows.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            {
                var value = ResolveValue(row, options);
                if (value is null)
                {
                    continue;
                }
                root.Insert(row.Key.Split(':'), value);
            }
            WriteNode(writer, root);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private string? ResolveValue(RawRow row, ExportOptions options)
    {
        if (!row.IsEncrypted)
        {
            return row.Value;
        }

        if (options.DecryptWithMasterKey)
        {
            _encryptor ??= ConfiguriteEncryption.CreateEncryptor(_store, options.MasterKey);
            return _encryptor.Decrypt(row.Value);
        }

        return options.IncludeEncrypted ? "(encrypted)" : null;
    }

    private static void WriteNode(Utf8JsonWriter writer, TreeNode node)
    {
        if (node.Leaf is not null)
        {
            writer.WriteStringValue(node.Leaf);
            return;
        }

        writer.WriteStartObject();
        foreach (var (key, child) in node.Children.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(key);
            WriteNode(writer, child);
        }
        writer.WriteEndObject();
    }

    private List<RawRow> ReadEverything()
    {
        // Direct SQL: ReadAll(env) merges globals into env results, which we don't want here —
        // we need each row exactly once with its true Environment column.
        // Direkt SQL: ReadAll(env) globalleri sonuçlara karıştırır; biz her satırı yalnız bir kez
        // ve gerçek Environment sütunuyla istiyoruz.
        var rows = new List<RawRow>();
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value, IsEncrypted, Environment FROM Configuration ORDER BY Environment, Key;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RawRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return rows;
    }

    /// <summary>
    /// EN: Disposes the lazily-created encryptor.
    /// TR: Lazy oluşturulan encryptor'u serbest bırakır.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _encryptor?.Dispose();
        _disposed = true;
    }

    private readonly record struct RawRow(string Key, string Value, bool IsEncrypted, string? Environment);

    private sealed class TreeNode
    {
        public string? Leaf { get; private set; }
        public Dictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Insert(string[] segments, string value)
        {
            var node = this;
            for (int i = 0; i < segments.Length; i++)
            {
                if (i == segments.Length - 1)
                {
                    if (!node.Children.TryGetValue(segments[i], out var leaf))
                    {
                        leaf = new TreeNode();
                        node.Children[segments[i]] = leaf;
                    }
                    leaf.Leaf = value;
                    return;
                }
                if (!node.Children.TryGetValue(segments[i], out var next))
                {
                    next = new TreeNode();
                    node.Children[segments[i]] = next;
                }
                node = next;
            }
        }
    }
}

/// <summary>
/// EN: Options controlling how <see cref="SqliteToJsonExporter"/> handles encrypted values.
/// TR: <see cref="SqliteToJsonExporter"/>'ın şifreli değerleri nasıl işleyeceğini kontrol eden seçenekler.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>
    /// EN: When true, encrypted values are decrypted using the resolved master key and written as
    ///     plaintext. Use with care — the resulting JSON contains your secrets in the clear.
    /// TR: True ise, şifreli değerler çözülmüş ana anahtarla çözülür ve düz metin olarak yazılır.
    ///     Dikkatli kullanın — sonuçta oluşan JSON sırlarınızı düz metin olarak içerir.
    /// </summary>
    public bool DecryptWithMasterKey { get; set; }

    /// <summary>
    /// EN: When true and <see cref="DecryptWithMasterKey"/> is false, encrypted entries appear
    ///     as the literal string <c>(encrypted)</c>. When false (default), they are skipped entirely.
    /// TR: True ve <see cref="DecryptWithMasterKey"/> false ise, şifreli girdiler tam olarak
    ///     <c>(encrypted)</c> dizgisi olarak görünür. False (varsayılan) ise tamamen atlanırlar.
    /// </summary>
    public bool IncludeEncrypted { get; set; }

    /// <summary>
    /// EN: Optional explicit master key passed to the resolver chain.
    /// TR: Resolver zincirine geçirilen isteğe bağlı açık ana anahtar.
    /// </summary>
    public string? MasterKey { get; set; }
}

/// <summary>
/// EN: Summary of an export operation.
/// TR: Bir dışa aktarma işleminin özeti.
/// </summary>
/// <param name="BytesWritten">
/// EN: Total number of bytes written for this output.
/// TR: Bu çıktı için yazılan toplam byte sayısı.
/// </param>
/// <param name="Environment">
/// EN: Environment name covered by this output, or null for globals.
/// TR: Bu çıktının kapsadığı ortam adı veya globaller için null.
/// </param>
public sealed record ExportResult(long BytesWritten, string? Environment);
