# Observability

> Available since `Configurite` 8.5 / 9.5 / 10.5.

Configurite emits structured logs and OpenTelemetry signals out of the box. Plug them into your existing pipeline with one line each.

## ILogger

Pass any `ILogger` (or rely on `Microsoft.Extensions.Logging.Abstractions` no-op default):

```csharp
builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath = "appsettings.db";
    opts.Logger       = builder.Services.BuildServiceProvider()
                              .GetRequiredService<ILoggerFactory>()
                              .CreateLogger("Configurite");
});
```

### Events

| EventId | Level | Message |
|---|---|---|
| 1 | Information | Configurite loaded N rows (M decrypted) from DbPath in X ms. |
| 2 | Debug | Configurite database missing at DbPath but Optional=true; loading empty. |
| 3 | Error | Configurite database not found at DbPath and Optional=false. |
| 4 | Debug | Configurite hot-reload completed for DbPath. |
| 5 | Warning | Configurite hot-reload failed; watcher continues. (with exception) |

All log statements use `LoggerMessage` source generators — zero allocations when the level is disabled.

## OpenTelemetry

### Tracing

Subscribe to the `Configurite` `ActivitySource`:

```csharp
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource(ConfiguriteTelemetry.SourceName)   // "Configurite"
        .AddOtlpExporter());
```

Activities emitted:

| Name | Tags |
|---|---|
| `Configurite.Load` | `configurite.database`, `configurite.environment`, `configurite.rows.total`, `configurite.rows.decrypted` |

### Metrics

Subscribe to the `Configurite` `Meter`:

```csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter(ConfiguriteTelemetry.MeterName)     // "Configurite"
        .AddOtlpExporter());
```

Instruments:

| Name | Type | Description |
|---|---|---|
| `configurite.loads.total` | Counter (long) | Total provider Load() invocations. |
| `configurite.reloads.total` | Counter (long) | Total hot reloads triggered. |
| `configurite.decryptions.total` | Counter (long) | Total values decrypted on Load(). |
| `configurite.load.duration` | Histogram (double, ms) | Provider Load() duration. |
| `configurite.watcher.errors.total` | Counter (long) | Swallowed errors in the file watcher. |

## Suggested dashboards

| Question | Metric / activity |
|---|---|
| Is hot reload firing in production? | `configurite.reloads.total` (counter rate) |
| How long does a config load take? | `configurite.load.duration` (p50/p99 histogram) |
| Are encrypted secrets actually being read? | `configurite.decryptions.total` (counter rate) |
| Is the watcher silently failing? | `configurite.watcher.errors.total` (alert if > 0) |
| Per-database trace correlation | `Configurite.Load` activity with `configurite.database` tag |

## Logging best practices

- **Don't log decrypted values yourself.** The provider doesn't; downstream code shouldn't either.
- **Use Debug level for reload events** in development, Information for production load summaries.
- The Information event (1) carries `RowCount`, `DecryptedCount`, `DbPath`, and `ElapsedMs` — useful for capacity planning without sampling all reads.
