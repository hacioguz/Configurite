# Hot reload

## Enable it

```csharp
builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath   = "appsettings.db";
    opts.ReloadOnChange = true;
});
```

The provider then watches the directory containing the database and fires `IChangeToken` whenever the `.db`, `-journal`, `-wal`, or `-shm` file changes. Any consumer using `IOptionsMonitor<T>` reacts automatically.

## Mechanism

1. `FileSystemWatcher` on the database directory, filtered to the four SQLite filenames.
2. A 250 ms debounce window collapses the burst of events from a single commit into one reload.
3. The provider re-reads the database, decrypts encrypted rows with the same master key, and signals `OnReload()`.

Failures inside the callback (e.g. transient file lock during a write) are swallowed — the watcher stays alive and tries again on the next event.

## Reacting to changes

```csharp
public sealed class GreetingService
{
    private readonly IOptionsMonitor<GreetingOptions> _opts;

    public GreetingService(IOptionsMonitor<GreetingOptions> opts)
    {
        _opts = opts;
        _opts.OnChange(o => Console.WriteLine($"new greeting: {o.Message}"));
    }

    public string CurrentMessage => _opts.CurrentValue.Message;
}
```

Updating a row from any tool (CLI, admin UI, your own code) triggers the change without a process restart.

## Caveats

- Some network filesystems and Docker bind mounts emit unreliable events; test in your target environment.
- Hot reload only re-evaluates Configurite. Other configuration sources (env vars, command line) are unaffected.
- If the master key is rotated while the app is running, the old encryptor stays in memory until disposal — restart to pick up a new key.
