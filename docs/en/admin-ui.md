# Admin UI

> Available since `Configurite.AdminUI` 8.4 / 9.4 / 10.4.

A drop-in web admin panel for Configurite databases. Browse keys, edit values (plain or encrypted), and inspect the audit log without leaving the browser. Bilingual (English / Turkish), zero front-end framework dependencies, ~20 KB nupkg.

## Install

```bash
dotnet add package Configurite.AdminUI
```

## Wire up

```csharp
using Configurite;
using Configurite.AdminUI;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfigurite("appsettings.db");

builder.Services.AddHttpContextAccessor();
builder.Services.AddConfiguriteAdmin(opts =>
{
    opts.DatabasePath    = "appsettings.db";
    opts.DefaultLanguage = "en";   // or "tr"
    opts.EnableAuditLog  = true;
});

var app = builder.Build();

app.MapConfiguriteAdmin("/configurite-admin")
   .RequireAuthorization("ConfiguriteAdmin");   // YOU own the auth policy

app.Run();
```

That's it. Browse to `/configurite-admin`.

## Pages

| Path | Purpose |
|---|---|
| `/configurite-admin/` | Dashboard — total rows, encrypted count, environments, schema version, audit count. |
| `/configurite-admin/keys` | Browse / filter by environment / add / edit / delete / reveal encrypted. |
| `/configurite-admin/audit` | Reverse-chronological audit log (last 200 entries). |

Append `?lang=tr` (or `?lang=en`) to any URL to switch language. The choice is persisted in a `configurite_lang` cookie.

## Audit log

When `EnableAuditLog = true` (the default), every `Upsert` and `Delete` made through the UI is recorded into the `AuditLog` SQLite table. The `User` column captures `HttpContext.User.Identity.Name`, so make sure your auth pipeline runs *before* the admin endpoints.

You can also use the audit log programmatically:

```csharp
IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog("appsettings.db");
audit.Record("CustomOp", "MyKey", "Production", "ci-bot");
foreach (var entry in audit.ReadRecent(50)) { /* … */ }
```

## Security guidance

The admin UI is **unauthenticated by default** — by design, you wire it up behind your own policy. Recommended pattern:

```csharp
app.MapConfiguriteAdmin("/configurite-admin")
   .RequireAuthorization(policy => policy
       .RequireAuthenticatedUser()
       .RequireRole("Admin"));
```

For internal/intranet apps, network-level restriction (`UseEndpoints` behind a VPN/IP allowlist) is also acceptable.

The UI never displays encrypted values until the user clicks **Show**. The reveal action passes the key+env in the URL; if you don't trust the master-key resolver chain in the browser context, set `EnableMasterKey = false` (TODO 1.x) or omit the master key entirely so encrypted rows stay opaque.

## Schema migration

The admin UI requires schema **version 2** (introduced in 8.4 / 9.4 / 10.4). On first run, `EnsureSchema()` adds the `AuditLog` table and bumps `Metadata.SchemaVersion` from `"1"` to `"2"`. Existing data is untouched.
