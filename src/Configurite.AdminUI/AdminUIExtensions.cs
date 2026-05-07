using Configurite.AdminUI.Internal;
using Configurite.Audit;
using Configurite.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Configurite.AdminUI;

/// <summary>
/// EN: DI + endpoint registration for the Configurite Admin UI.
/// TR: Configurite Yönetim Arayüzü için DI + endpoint kayıtları.
/// </summary>
public static class AdminUIExtensions
{
    /// <summary>
    /// EN: Registers the services needed by <see cref="MapConfiguriteAdmin"/>. Call once during app
    ///     startup. The configured <see cref="IConfiguriteStore"/> is automatically wrapped in an
    ///     <see cref="AuditingConfiguriteStore"/> when <see cref="AdminUIOptions.EnableAuditLog"/> is true.
    /// TR: <see cref="MapConfiguriteAdmin"/> için gereken servisleri kaydeder. Uygulama başlangıcında
    ///     bir kez çağırın. <see cref="AdminUIOptions.EnableAuditLog"/> true ise, yapılandırılan
    ///     <see cref="IConfiguriteStore"/> otomatik olarak <see cref="AuditingConfiguriteStore"/>'a sarmalanır.
    /// </summary>
    public static IServiceCollection AddConfiguriteAdmin(
        this IServiceCollection services,
        Action<AdminUIOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.AddSingleton<IConfiguriteAuditLog>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AdminUIOptions>>().Value;
            return new SqliteConfiguriteAuditLog(opts.DatabasePath);
        });

        services.AddSingleton<AdminWriteStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AdminUIOptions>>().Value;
            var raw = new SqliteConfiguriteStore(opts.DatabasePath);
            raw.EnsureSchema();
            IConfiguriteStore store = raw;
            if (opts.EnableAuditLog)
            {
                var audit = sp.GetRequiredService<IConfiguriteAuditLog>();
                store = new AuditingConfiguriteStore(store, audit, () => CurrentUser(sp));
            }
            return new AdminWriteStore(store);
        });

        return services;
    }

    /// <summary>
    /// EN: Mounts the Admin UI under <paramref name="prefix"/> (default <c>/configurite-admin</c>).
    ///     Returns a route group so callers can chain <c>.RequireAuthorization(...)</c>.
    /// TR: Yönetim arayüzünü <paramref name="prefix"/> altına monte eder (varsayılan
    ///     <c>/configurite-admin</c>). Çağıranların <c>.RequireAuthorization(...)</c> ile zincirleyebilmesi
    ///     için bir rota grubu döner.
    /// </summary>
    public static RouteGroupBuilder MapConfiguriteAdmin(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/configurite-admin")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var trimmed = prefix.TrimEnd('/');
        var group = endpoints.MapGroup(trimmed);

        group.MapGet("/", (HttpContext ctx, IOptions<AdminUIOptions> opts) =>
            AdminEndpoints.Dashboard(ctx, opts.Value, trimmed));

        group.MapGet("/keys", (HttpContext ctx, IOptions<AdminUIOptions> opts) =>
            AdminEndpoints.KeysPage(ctx, opts.Value, trimmed));

        group.MapPost("/keys", (HttpContext ctx, IOptions<AdminUIOptions> opts, AdminWriteStore writer) =>
            AdminEndpoints.UpsertKey(ctx, opts.Value, writer.Store, trimmed));

        group.MapPost("/keys/delete", (HttpContext ctx, AdminWriteStore writer) =>
            AdminEndpoints.DeleteKey(ctx, writer.Store, trimmed));

        group.MapGet("/audit", (HttpContext ctx, IOptions<AdminUIOptions> opts) =>
            AdminEndpoints.AuditPage(ctx, opts.Value, trimmed));

        return group;
    }

    private static string? CurrentUser(IServiceProvider sp)
    {
        var accessor = sp.GetService<IHttpContextAccessor>();
        return accessor?.HttpContext?.User?.Identity?.Name;
    }
}

/// <summary>
/// EN: Internal carrier for the (possibly audit-decorated) write store. Keeps the type
///     distinct from <see cref="IConfiguriteStore"/> registrations the application may add elsewhere.
/// TR: (Opsiyonel olarak audit ile sarılmış) yazma store'unun dahili taşıyıcısı.
///     Uygulamanın başka yerde eklediği <see cref="IConfiguriteStore"/> kayıtlarından ayrı kalır.
/// </summary>
internal sealed class AdminWriteStore
{
    public AdminWriteStore(IConfiguriteStore store) => Store = store;
    public IConfiguriteStore Store { get; }
}
