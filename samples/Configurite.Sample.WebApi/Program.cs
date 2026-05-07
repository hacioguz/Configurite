// EN: Sample ASP.NET 8 Web API. On first run we migrate appsettings*.json to an encrypted
//     SQLite database, then read the entire configuration from that database.
// TR: Örnek ASP.NET 8 Web API. İlk çalıştırmada appsettings*.json dosyaları şifrelenmiş bir
//     SQLite veritabanına geçirilir; sonra tüm yapılandırma o veritabanından okunur.

using System.Globalization;
using Configurite;
using Configurite.AdminUI;
using Configurite.Encryption;
using Configurite.Migration;

const string DemoMasterKey = "demo-master-key-change-me";
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName)))
{
    Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, DemoMasterKey);
}

var dbPath = Path.Combine(AppContext.BaseDirectory, "appsettings.db");
MigrateOnFirstRun(dbPath);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath = dbPath;
    opts.Environment = builder.Environment.EnvironmentName;
    opts.ReloadOnChange = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EN: Mount the Configurite admin UI at /configurite-admin (auth-gated in real apps).
// TR: Configurite yönetim arayüzünü /configurite-admin altına monte et (gerçek uygulamada auth ile koru).
builder.Services.AddHttpContextAccessor();
builder.Services.AddConfiguriteAdmin(opts =>
{
    opts.DatabasePath = dbPath;
    opts.DefaultLanguage = "tr";
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/", (IConfiguration cfg) => new
{
    AppName = cfg["AppName"],
    Greeting = cfg["Greeting"],
    LogLevel = cfg["Logging:LogLevel:Default"],
    ConnectionString = cfg["ConnectionStrings:Default"], // EN: encrypted at rest / TR: dosyada şifreli
    ApiKey = cfg["Auth:ApiKey"],                          // EN: encrypted at rest / TR: dosyada şifreli
    Source = "Configurite (SQLite-backed configuration)",
});

app.MapGet("/weatherforecast", (IConfiguration cfg) =>
{
    var days = int.Parse(cfg["Forecast:Days"] ?? "5", CultureInfo.InvariantCulture);

    return Enumerable.Range(1, days).Select(index => new WeatherForecast(
        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        Random.Shared.Next(-20, 55),
        Summaries.All[Random.Shared.Next(Summaries.All.Length)]));
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.MapConfiguriteAdmin("/configurite-admin");

app.Run();

// EN: One-shot migration: appsettings.json + appsettings.{Env}.json → appsettings.db.
//     Sensitive paths are encrypted using the resolved master key.
// TR: Tek seferlik geçiş: appsettings.json + appsettings.{Env}.json → appsettings.db.
//     Hassas yollar çözümlenen ana anahtar ile şifrelenir.
static void MigrateOnFirstRun(string databasePath)
{
    if (File.Exists(databasePath))
    {
        return;
    }

    using var migrator = new JsonToSqliteMigrator(databasePath);
    var result = migrator.MigrateDirectory(AppContext.BaseDirectory, "appsettings", new MigrationOptions
    {
        EncryptKeyPatterns =
        {
            "ConnectionStrings:*",
            "*:Password",
            "*:ApiKey",
        },
    });

    Console.WriteLine(
        $"[Configurite] Migrated {result.FilesProcessed} JSON file(s): " +
        $"{result.KeysWritten} keys written, {result.KeysEncrypted} encrypted.");
}

internal static class Summaries
{
    public static readonly string[] All =
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild",
        "Warm", "Balmy", "Hot", "Sweltering", "Scorching",
    };
}

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
