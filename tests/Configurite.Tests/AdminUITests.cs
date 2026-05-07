using System.Net;
using System.Net.Http;
using Configurite.AdminUI;
using Configurite.Audit;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Configurite.Tests;

[Collection("Environment")]
public sealed class AdminUITests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-admin-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private IHost BuildHost(string? masterKey = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(svc =>
                {
                    svc.AddRouting();
                    svc.AddHttpContextAccessor();
                    svc.AddConfiguriteAdmin(opts =>
                    {
                        opts.DatabasePath = _dbPath;
                        opts.MasterKey = masterKey; // bypass env var entirely
                    });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapConfiguriteAdmin("/admin"));
                });
            })
            .Start();
    }

    [Fact]
    public async Task Dashboard_ReturnsHtmlWithStats()
    {
        // Seed some rows.
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("AppName", "Demo", false, null);
        store.Upsert("Greeting", "dev", false, "Development");

        using var host = BuildHost();
        var client = host.GetTestClient();

        var resp = await client.GetAsync(new Uri("/admin/", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Total rows");      // default lang = en
        body.Should().Contain("Encrypted");
        body.Should().Contain(">2<");             // 2 rows seeded
    }

    [Fact]
    public async Task LangQuery_SwitchesToTurkish()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        using var host = BuildHost();
        var client = host.GetTestClient();

        var body = await client.GetStringAsync(new Uri("/admin/?lang=tr", UriKind.Relative));
        body.Should().Contain("Toplam sat");      // "Toplam satır" — Turkish
    }

    [Fact]
    public async Task UpsertViaPost_CreatesRow_AndAuditLogsIt()
    {
        using var host = BuildHost();
        var client = host.GetTestClient();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("key", "FromUI"),
            new KeyValuePair<string, string>("value", "v1"),
            new KeyValuePair<string, string>("env", ""),
        });

        var resp = await client.PostAsync(new Uri("/admin/keys", UriKind.Relative), content);
        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var store = new SqliteConfiguriteStore(_dbPath);
        store.TryGet("FromUI", null, out var entry).Should().BeTrue();
        entry.Value.Should().Be("v1");

        var audit = new SqliteConfiguriteAuditLog(_dbPath);
        audit.Count().Should().Be(1);
        audit.ReadRecent(1)[0].Operation.Should().Be("Upsert");
    }

    [Fact]
    public async Task DeleteViaPost_RemovesRow_AndAuditLogsIt()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("DeleteMe", "x", false, null);

        using var host = BuildHost();
        var client = host.GetTestClient();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("key", "DeleteMe"),
            new KeyValuePair<string, string>("env", ""),
        });

        var resp = await client.PostAsync(new Uri("/admin/keys/delete", UriKind.Relative), content);
        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);

        store.TryGet("DeleteMe", null, out _).Should().BeFalse();

        var audit = new SqliteConfiguriteAuditLog(_dbPath);
        audit.Count().Should().Be(1);
        audit.ReadRecent(1)[0].Operation.Should().Be("Delete");
    }

    [Fact]
    public async Task AuditPage_ListsRecentEntries()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        var audit = new SqliteConfiguriteAuditLog(_dbPath);
        audit.Record("Upsert", "Foo", null, "alice");

        using var host = BuildHost();
        var client = host.GetTestClient();

        var body = await client.GetStringAsync(new Uri("/admin/audit", UriKind.Relative));
        body.Should().Contain("Upsert");
        body.Should().Contain("Foo");
        body.Should().Contain("alice");
    }

    [Fact]
    public async Task EncryptedFlow_RoundTripsThroughUI()
    {
        // Use AdminUIOptions.MasterKey directly so env var races (parallel test classes) cannot
        // corrupt this assertion.
        // Paralel test sınıflarındaki env var race'lerini bypass etmek için MasterKey'i doğrudan opt'a geç.
        using var host = BuildHost(masterKey: "ui-test-key");
        var client = host.GetTestClient();

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("key", "Secret"),
            new KeyValuePair<string, string>("value", "shh"),
            new KeyValuePair<string, string>("env", ""),
            new KeyValuePair<string, string>("encrypt", "1"),
        });

        var resp = await client.PostAsync(new Uri("/admin/keys", UriKind.Relative), content);
        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var hidden = await client.GetStringAsync(new Uri("/admin/keys", UriKind.Relative));
        hidden.Should().Contain("(encrypted)");
        hidden.Should().NotContain(">shh<");

        var revealed = await client.GetStringAsync(new Uri("/admin/keys?reveal=Secret&revealEnv=", UriKind.Relative));
        revealed.Should().Contain(">shh<");
    }
}
