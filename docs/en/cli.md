# CLI tool

> Available since `Configurite.Cli` 1.2.

The `configurite` command-line tool wraps every operation a developer needs against a Configurite database — useful for CI scripts, ops runbooks, and ad-hoc inspection.

## Install

```bash
dotnet tool install -g Configurite.Cli
```

Run `configurite --help` to see the full reference.

## Commands

| Command | Purpose |
|---|---|
| `init <db>` | Create the schema in `<db>`. |
| `migrate <db> <json-or-dir> [opts]` | Migrate `appsettings*.json` files. |
| `rotate <db> --old <key> --new <key>` | Atomic master-key rotation. |
| `get <db> <key> [--env <name>]` | Read a single value (decrypts if needed). |
| `set <db> <key> <value> [opts]` | Insert or update a value. |
| `list <db> [--env <name>] [--reveal]` | List rows. `--reveal` decrypts encrypted values. |
| `delete <db> <key> [--env <name>]` | Remove a row. |

## Common options

| Option | Applies to | Description |
|---|---|---|
| `--env <name>` | get/set/list/delete | Environment scope (`Development`, `Production`, …). |
| `--encrypt` | set | Encrypts the new value. |
| `--encrypt <pattern>` | migrate | Glob pattern; matched keys are encrypted. Repeatable. |
| `--master-key <key>` | any encryption-aware command | Override `CONFIGURITE_MASTER_KEY`. |
| `--no-overwrite` | migrate | Preserve existing rows. |
| `--reveal` | list | Decrypt encrypted values for display. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Generic error |
| 2 | Bad arguments / unknown command |
| 3 | File not found |
| 4 | Master-key resolution failed (no encryption available) |
| 5 | Key not found in `get` / `delete` |

## Examples

```bash
# Bootstrap a fresh database from existing JSON files.
configurite migrate ./appsettings.db ./config-folder \
    --encrypt "ConnectionStrings:*" --encrypt "*:Password" --encrypt "*:ApiKey"

# Add or update an encrypted secret.
configurite set ./appsettings.db Auth:ApiKey "$(pass show api/prod)" --encrypt

# Read a value.
configurite get ./appsettings.db ConnectionStrings:Default --env Production

# Inspect the database (encrypted values stay opaque without --reveal).
configurite list ./appsettings.db
configurite list ./appsettings.db --env Development --reveal

# Rotate the master key. CI-friendly: one transaction, atomic.
export OLD_KEY=...; export NEW_KEY=...
configurite rotate ./appsettings.db --old "$OLD_KEY" --new "$NEW_KEY"

# Remove a stale entry.
configurite delete ./appsettings.db OldFlag --env Development
```

## Master-key resolution

The CLI honours the same fallback chain as the library:

1. `--master-key <key>` flag.
2. `CONFIGURITE_MASTER_KEY` environment variable.
3. `~/.configurite/master.key` file.

Commands that don't touch encrypted values (`init`, `migrate` without `--encrypt`, plain `set`/`get`, `list` without `--reveal`, `delete`) never look up a master key.
