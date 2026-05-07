using Microsoft.Extensions.Logging;

namespace Configurite.Diagnostics;

/// <summary>
/// EN: Source-generated log message templates. Each method compiles to an allocation-free
///     <c>LoggerMessage</c> delegate. Names mirror the call sites in the provider/watcher.
/// TR: Source-generator ile üretilen log mesaj şablonları. Her metot allocation-free bir
///     <c>LoggerMessage</c> delegate'ine derlenir. İsimler provider/watcher'daki çağrı
///     yerlerini yansıtır.
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Configurite loaded {RowCount} rows ({DecryptedCount} decrypted) from {DbPath} in {ElapsedMs:F2} ms.")]
    public static partial void LogLoadCompleted(ILogger logger, int rowCount, int decryptedCount, string dbPath, double elapsedMs);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Configurite database missing at {DbPath} but Optional=true; loading empty.")]
    public static partial void LogOptionalMissing(ILogger logger, string dbPath);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Configurite database not found at {DbPath} and Optional=false.")]
    public static partial void LogDatabaseMissing(ILogger logger, string dbPath);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Configurite hot-reload completed for {DbPath}.")]
    public static partial void LogReloadCompleted(ILogger logger, string dbPath);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Configurite hot-reload failed; watcher continues.")]
    public static partial void LogReloadFailed(ILogger logger, Exception ex);
}
