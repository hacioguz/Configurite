# Key rotation

> Available since `Configurite` 1.1.

## Why rotate?

Master keys leak. Compliance regimes demand periodic rotation. A built-in rotator lets you swap the master key without manual SQL or hand-rolled scripts.

## API

```csharp
using var rotator = new ConfiguriteKeyRotator("appsettings.db");
var result = rotator.Rotate(oldMasterKey: "old", newMasterKey: "new");

Console.WriteLine($"{result.RowsRotated} rows re-encrypted under the new key.");
```

## Atomicity guarantee

The whole rotation runs inside a single SQLite transaction:

1. **Read phase** — every `IsEncrypted = 1` row is decrypted with the old key into memory.
2. **Write phase (transaction)** — a fresh 32-byte salt is written, then every row is replaced with ciphertext under the new key.
3. **Commit** — only after every row succeeds.

If anything fails — wrong old key, IO error, process crash — the transaction is rolled back and the database stays fully on the **old** key. There is no half-rotated state.

## Recipe: scheduled rotation

```csharp
public sealed class KeyRotationJob(ILogger<KeyRotationJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var oldKey = Environment.GetEnvironmentVariable("CONFIGURITE_OLD_KEY")!;
                var newKey = Environment.GetEnvironmentVariable("CONFIGURITE_MASTER_KEY")!;

                using var rotator = new ConfiguriteKeyRotator("appsettings.db");
                var result = rotator.Rotate(oldKey, newKey);

                log.LogInformation("Rotated {Count} rows", result.RowsRotated);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Rotation failed; database unchanged.");
            }

            await Task.Delay(TimeSpan.FromDays(90), stoppingToken);
        }
    }
}
```

## Edge cases

| Scenario | Behaviour |
|---|---|
| No encrypted rows yet | Transaction still refreshes the salt. `RowsRotated = 0`. |
| Database has plaintext rows only and never had a salt | Throws `InvalidOperationException` ("nothing to rotate"). |
| Wrong old key | Throws `CryptographicException`; database unchanged. |
| Empty `oldMasterKey` or `newMasterKey` | Throws `ArgumentException`. |
| Same old + new key | Allowed — the salt is still rotated, ciphertexts change. |

## Combining with hot reload

If your provider runs with `ReloadOnChange = true`, the watcher will fire after the rotation transaction commits. The provider rebuilds its decryptor with the **new** master key on the next `Load()`. Make sure `ConfiguriteOptions.MasterKey` (or `CONFIGURITE_MASTER_KEY`) points at the new key before the rotator runs.
