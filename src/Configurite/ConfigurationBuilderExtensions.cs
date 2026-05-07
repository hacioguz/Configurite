using Microsoft.Extensions.Configuration;

namespace Configurite;

/// <summary>
/// EN: Extension methods for adding Configurite SQLite configuration to <see cref="IConfigurationBuilder"/>.
/// TR: <see cref="IConfigurationBuilder"/> nesnesine Configurite SQLite yapılandırması ekleyen uzantı metotları.
/// </summary>
public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// EN: Adds a Configurite SQLite configuration source using only a database path.
    /// TR: Yalnızca veritabanı yolu ile bir Configurite SQLite yapılandırma kaynağı ekler.
    /// </summary>
    /// <param name="builder">
    /// EN: The configuration builder.
    /// TR: Yapılandırma builder'ı.
    /// </param>
    /// <param name="databasePath">
    /// EN: Path to the SQLite database file.
    /// TR: SQLite veritabanı dosyasının yolu.
    /// </param>
    public static IConfigurationBuilder AddConfigurite(
        this IConfigurationBuilder builder,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return builder.AddConfigurite(opts => opts.DatabasePath = databasePath);
    }

    /// <summary>
    /// EN: Adds a Configurite SQLite configuration source with custom options.
    /// TR: Özel seçeneklerle bir Configurite SQLite yapılandırma kaynağı ekler.
    /// </summary>
    /// <param name="builder">
    /// EN: The configuration builder.
    /// TR: Yapılandırma builder'ı.
    /// </param>
    /// <param name="configure">
    /// EN: A delegate that configures the <see cref="ConfiguriteOptions"/> instance.
    /// TR: <see cref="ConfiguriteOptions"/> örneğini yapılandıran bir delegasyon.
    /// </param>
    public static IConfigurationBuilder AddConfigurite(
        this IConfigurationBuilder builder,
        Action<ConfiguriteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ConfiguriteOptions();
        configure(options);

        return builder.Add(new SqliteConfigurationSource { Options = options });
    }
}
