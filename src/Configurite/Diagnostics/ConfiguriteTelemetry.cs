using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Configurite.Diagnostics;

/// <summary>
/// EN: OpenTelemetry instrumentation for Configurite. Subscribe to the activity source
///     <see cref="SourceName"/> and the meter <see cref="MeterName"/> from your OTel
///     pipeline (AddSource / AddMeter).
/// TR: Configurite için OpenTelemetry enstrümantasyonu. OTel pipeline'ınızdan
///     <see cref="SourceName"/> activity source'una ve <see cref="MeterName"/> meter'ına
///     abone olun (AddSource / AddMeter).
/// </summary>
public static class ConfiguriteTelemetry
{
    /// <summary>
    /// EN: Activity source name. Use this with <c>OpenTelemetry.Trace.AddSource(SourceName)</c>.
    /// TR: Activity source adı. <c>OpenTelemetry.Trace.AddSource(SourceName)</c> ile kullanın.
    /// </summary>
    public const string SourceName = "Configurite";

    /// <summary>
    /// EN: Meter name. Use this with <c>OpenTelemetry.Metrics.AddMeter(MeterName)</c>.
    /// TR: Meter adı. <c>OpenTelemetry.Metrics.AddMeter(MeterName)</c> ile kullanın.
    /// </summary>
    public const string MeterName = "Configurite";

    internal static readonly ActivitySource ActivitySource = new(SourceName);
    internal static readonly Meter Meter = new(MeterName);

    // Instruments
    // Enstrümanlar
    internal static readonly Counter<long> LoadsTotal =
        Meter.CreateCounter<long>("configurite.loads.total", description: "Total provider Load() invocations.");

    internal static readonly Counter<long> ReloadsTotal =
        Meter.CreateCounter<long>("configurite.reloads.total", description: "Total hot reloads triggered.");

    internal static readonly Counter<long> DecryptionsTotal =
        Meter.CreateCounter<long>("configurite.decryptions.total", description: "Total values decrypted on Load().");

    internal static readonly Histogram<double> LoadDurationMs =
        Meter.CreateHistogram<double>("configurite.load.duration", unit: "ms", description: "Provider Load() duration in milliseconds.");

    internal static readonly Counter<long> WatcherErrorsTotal =
        Meter.CreateCounter<long>("configurite.watcher.errors.total", description: "Total swallowed errors in the file watcher reload pipeline.");
}
