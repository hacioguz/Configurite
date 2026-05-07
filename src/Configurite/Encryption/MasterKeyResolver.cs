namespace Configurite.Encryption;

/// <summary>
/// EN: Resolves the master key used by <see cref="AesGcmConfigEncryptor"/> from a fallback chain:
///     <list type="number">
///         <item>EN: Explicit value supplied via <see cref="ConfiguriteOptions.MasterKey"/>.</item>
///         <item>EN: Environment variable <c>CONFIGURITE_MASTER_KEY</c>.</item>
///         <item>EN: Key file at <c>~/.configurite/master.key</c> (single line, UTF-8).</item>
///     </list>
/// TR: <see cref="AesGcmConfigEncryptor"/> için ana anahtarı şu öncelik sırasıyla çözer:
///     <list type="number">
///         <item>TR: <see cref="ConfiguriteOptions.MasterKey"/> ile verilen açık değer.</item>
///         <item>TR: <c>CONFIGURITE_MASTER_KEY</c> ortam değişkeni.</item>
///         <item>TR: <c>~/.configurite/master.key</c> anahtar dosyası (tek satır, UTF-8).</item>
///     </list>
/// </summary>
public static class MasterKeyResolver
{
    /// <summary>
    /// EN: Environment variable name searched when no explicit master key is provided.
    /// TR: Açık ana anahtar verilmediğinde aranan ortam değişkeninin adı.
    /// </summary>
    public const string EnvironmentVariableName = "CONFIGURITE_MASTER_KEY";

    /// <summary>
    /// EN: Returns the resolved master key, or <see langword="null"/> if no source is available.
    /// TR: Çözümlenen ana anahtarı döndürür; hiçbir kaynak yoksa <see langword="null"/> döner.
    /// </summary>
    /// <param name="explicitKey">
    /// EN: Optional value supplied directly via <see cref="ConfiguriteOptions.MasterKey"/>.
    /// TR: <see cref="ConfiguriteOptions.MasterKey"/> üzerinden doğrudan verilen isteğe bağlı değer.
    /// </param>
    public static string? Resolve(string? explicitKey = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey;
        }

        var envValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        var keyFile = DefaultKeyFilePath();
        if (File.Exists(keyFile))
        {
            var contents = File.ReadAllText(keyFile).Trim();
            if (!string.IsNullOrEmpty(contents))
            {
                return contents;
            }
        }

        return null;
    }

    /// <summary>
    /// EN: Resolves the master key, throwing <see cref="InvalidOperationException"/> if none is found.
    /// TR: Ana anahtarı çözer; bulunamazsa <see cref="InvalidOperationException"/> fırlatır.
    /// </summary>
    public static string Require(string? explicitKey = null)
    {
        return Resolve(explicitKey)
            ?? throw new InvalidOperationException(
                $"Configurite master key not found. Set {EnvironmentVariableName}, place one in {DefaultKeyFilePath()}, or supply ConfiguriteOptions.MasterKey.");
    }

    /// <summary>
    /// EN: Default key file path: <c>~/.configurite/master.key</c>.
    /// TR: Varsayılan anahtar dosyası yolu: <c>~/.configurite/master.key</c>.
    /// </summary>
    public static string DefaultKeyFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".configurite", "master.key");
    }
}
