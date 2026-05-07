using Microsoft.Extensions.Configuration;

namespace Configurite;

/// <summary>
/// EN: Represents a SQLite-backed configuration source for the .NET configuration system.
/// TR: .NET yapılandırma sistemi için SQLite tabanlı yapılandırma kaynağı.
/// </summary>
public sealed class SqliteConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// EN: Options used to construct the provider.
    /// TR: Sağlayıcıyı oluşturmak için kullanılan seçenekler.
    /// </summary>
    public ConfiguriteOptions Options { get; set; } = new();

    /// <summary>
    /// EN: Builds a <see cref="SqliteConfigurationProvider"/> instance.
    /// TR: Bir <see cref="SqliteConfigurationProvider"/> örneği oluşturur.
    /// </summary>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return new SqliteConfigurationProvider(Options);
    }
}
