using Configurite;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Configurite.Tests;

public sealed class HotReloadTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-reload-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void ReloadOnChange_WhenDisabled_DoesNotPickUpUpdates()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Greeting", "v1", isEncrypted: false, environment: null);

        var config = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.ReloadOnChange = false;
            })
            .Build();

        config["Greeting"].Should().Be("v1");

        store.Upsert("Greeting", "v2", isEncrypted: false, environment: null);

        // Without reload-on-change, the in-memory snapshot stays at v1.
        // ReloadOnChange kapalıyken bellek içi anlık görüntü v1 olarak kalır.
        config["Greeting"].Should().Be("v1");
    }

    [Fact]
    public void ManualReload_PicksUpUpdates_AndFiresChangeToken()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Greeting", "v1", isEncrypted: false, environment: null);

        // Build a provider directly so we can drive its reload synchronously.
        // Provider'ı doğrudan kuruyoruz; böylece reload'ı senkron tetikleyebiliriz.
        using var provider = new SqliteConfigurationProvider(new ConfiguriteOptions
        {
            DatabasePath = _dbPath,
            ReloadOnChange = true,
        });
        provider.Load();

        // Hook the provider's change token into a flag.
        // Provider'ın değişim tokenini bir bayrağa bağla.
        var reloaded = false;
        ChangeToken.OnChange(provider.GetReloadToken, () => reloaded = true);

        store.Upsert("Greeting", "v2", isEncrypted: false, environment: null);
        provider.TriggerReloadForTesting();

        reloaded.Should().BeTrue("the change token must fire after a reload");

        provider.TryGet("Greeting", out var value).Should().BeTrue();
        value.Should().Be("v2");
    }

    [Fact]
    public async Task FileSystemWatcher_DetectsExternalWrite_WithinTimeout()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Greeting", "v1", isEncrypted: false, environment: null);

        using var provider = new SqliteConfigurationProvider(new ConfiguriteOptions
        {
            DatabasePath = _dbPath,
            ReloadOnChange = true,
        });
        provider.Load();

        var tcs = new TaskCompletionSource();
        ChangeToken.OnChange(provider.GetReloadToken, () => tcs.TrySetResult());

        // External writer modifies the file. The watcher debounces, then triggers.
        // Dış yazıcı dosyayı değiştirir. Watcher debounce edip tetikler.
        store.Upsert("Greeting", "v2", isEncrypted: false, environment: null);

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        fired.Should().Be(tcs.Task, "FileSystemWatcher should fire within 5 seconds");

        provider.TryGet("Greeting", out var value).Should().BeTrue();
        value.Should().Be("v2");
    }

    [Fact]
    public void Provider_Dispose_StopsWatcher()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Greeting", "v1", isEncrypted: false, environment: null);

        var provider = new SqliteConfigurationProvider(new ConfiguriteOptions
        {
            DatabasePath = _dbPath,
            ReloadOnChange = true,
        });
        provider.Load();
        provider.Dispose();

        // After Dispose, manually triggering must be a no-op (no exceptions).
        // Dispose sonrası manuel tetikleme no-op olmalı (istisna yok).
        var act = () => provider.TriggerReloadForTesting();
        act.Should().NotThrow();
    }
}
