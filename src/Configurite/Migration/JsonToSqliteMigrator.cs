using System.Text.RegularExpressions;
using Configurite.Encryption;
using Configurite.Storage;

namespace Configurite.Migration;

/// <summary>
/// EN: One-shot tool that copies values from <c>appsettings*.json</c> files into a Configurite
///     SQLite database, optionally encrypting selected keys.
/// TR: <c>appsettings*.json</c> dosyalarındaki değerleri bir Configurite SQLite veritabanına
///     kopyalayan tek seferlik araç; seçilen anahtarları isteğe bağlı olarak şifreler.
/// </summary>
public sealed class JsonToSqliteMigrator : IDisposable
{
    private static readonly Regex EnvironmentFileRegex = new(
        @"^(?<base>.+?)\.(?<env>[^.]+)\.json$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SqliteConfiguriteStore _store;
    private readonly string? _masterKey;
    private AesGcmConfigEncryptor? _encryptor;
    private bool _disposed;

    /// <summary>
    /// EN: Creates a migrator targeting <paramref name="databasePath"/>.
    ///     Provide <paramref name="masterKey"/> if any patterns will be encrypted; otherwise the
    ///     migrator falls back to <see cref="MasterKeyResolver"/> when an encrypt-pattern matches.
    /// TR: <paramref name="databasePath"/> hedefli bir migrator oluşturur.
    ///     Herhangi bir desen şifrelenecekse <paramref name="masterKey"/> verin; aksi halde
    ///     migrator bir şifreleme deseni eşleştiğinde <see cref="MasterKeyResolver"/>'a düşer.
    /// </summary>
    public JsonToSqliteMigrator(string databasePath, string? masterKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _store = new SqliteConfiguriteStore(databasePath);
        _store.EnsureSchema();
        _masterKey = masterKey;
    }

    /// <summary>
    /// EN: Migrates a single JSON file. Returns a summary; throws when an encrypt pattern is
    ///     declared but no master key is available.
    /// TR: Tek bir JSON dosyasını geçirir. Bir özet döner; bir şifreleme deseni tanımlanmışsa
    ///     ve ana anahtar yoksa hata fırlatır.
    /// </summary>
    public MigrationResult MigrateFile(string jsonPath, MigrationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"JSON file not found: {jsonPath}", jsonPath);
        }

        options ??= new MigrationOptions();
        var environment = options.EnvironmentOverride ?? InferEnvironment(jsonPath);
        var matcher = new KeyPatternMatcher(options.EncryptKeyPatterns);

        var written = 0;
        var encrypted = 0;
        var skipped = 0;

        var json = File.ReadAllText(jsonPath);
        var pairs = JsonFlattener.Flatten(json);

        foreach (var (key, value) in pairs)
        {
            if (!options.Overwrite && _store.ReadAll(environment).ContainsKey(key))
            {
                skipped++;
                continue;
            }

            if (matcher.IsMatch(key))
            {
                var enc = ResolveEncryptor();
                _store.Upsert(key, enc.Encrypt(value), isEncrypted: true, environment);
                written++;
                encrypted++;
            }
            else
            {
                _store.Upsert(key, value, isEncrypted: false, environment);
                written++;
            }
        }

        return new MigrationResult(FilesProcessed: 1, KeysWritten: written, KeysEncrypted: encrypted, KeysSkipped: skipped);
    }

    /// <summary>
    /// EN: Migrates every <c>{baseFileName}.json</c> and <c>{baseFileName}.{Env}.json</c> file
    ///     in <paramref name="directory"/>. The base file is treated as global (NULL environment);
    ///     suffixed files use the suffix as the environment name.
    /// TR: <paramref name="directory"/> içindeki tüm <c>{baseFileName}.json</c> ve
    ///     <c>{baseFileName}.{Env}.json</c> dosyalarını geçirir. Temel dosya global (NULL ortam)
    ///     olarak işlenir; ekli dosyalar son eki ortam adı olarak kullanır.
    /// </summary>
    public MigrationResult MigrateDirectory(
        string directory,
        string baseFileName = "appsettings",
        MigrationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        var pattern = baseFileName + "*.json";
        var files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalFiles = 0;
        var totalWritten = 0;
        var totalEncrypted = 0;
        var totalSkipped = 0;

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);

            // EN: Only migrate files that match exactly "<baseFileName>.json" or "<baseFileName>.<Env>.json".
            // TR: Yalnızca tam olarak "<baseFileName>.json" veya "<baseFileName>.<Env>.json" eşleşen dosyaları geçir.
            if (!IsTargetFile(fileName, baseFileName))
            {
                continue;
            }

            var result = MigrateFile(file, options);
            totalFiles += result.FilesProcessed;
            totalWritten += result.KeysWritten;
            totalEncrypted += result.KeysEncrypted;
            totalSkipped += result.KeysSkipped;
        }

        return new MigrationResult(totalFiles, totalWritten, totalEncrypted, totalSkipped);
    }

    private static bool IsTargetFile(string fileName, string baseFileName)
    {
        if (fileName.Equals(baseFileName + ".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var match = EnvironmentFileRegex.Match(fileName);
        return match.Success
            && match.Groups["base"].Value.Equals(baseFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? InferEnvironment(string jsonPath)
    {
        var fileName = Path.GetFileName(jsonPath);
        var match = EnvironmentFileRegex.Match(fileName);
        // "appsettings.Development.json" -> "Development". "appsettings.json" -> null.
        return match.Success ? match.Groups["env"].Value : null;
    }

    private AesGcmConfigEncryptor ResolveEncryptor()
    {
        if (_encryptor is not null)
        {
            return _encryptor;
        }

        var key = MasterKeyResolver.Resolve(_masterKey)
            ?? throw new InvalidOperationException(
                "An encryption pattern matched but no master key is available. Pass masterKey to the JsonToSqliteMigrator constructor or set CONFIGURITE_MASTER_KEY.");

        var salt = EncryptionMetadata.GetOrCreateSalt(_store);
        _encryptor = new AesGcmConfigEncryptor(key, salt);
        return _encryptor;
    }

    /// <summary>
    /// EN: Disposes the underlying encryptor (zeros the derived key).
    /// TR: Alttaki encryptor'u serbest bırakır (türetilmiş anahtarı sıfırlar).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _encryptor?.Dispose();
        _encryptor = null;
        _disposed = true;
    }
}
