# Versioning policy

Configurite uses **TFM-major versioning** — the major component of the package version mirrors the .NET target framework. The library and CLI ship in **lockstep** (same version every release).

## The rule

| Target framework | Package line | Example version |
|---|---|---|
| `net8.0` | `8.x.y` | `Configurite 8.2.0` |
| `net9.0` | `9.x.y` | `Configurite 9.2.0` |
| `net10.0` | `10.x.y` | `Configurite 10.2.0` |

A consumer pins to a line via floating ranges:

```xml
<!-- net8.0 project -->
<PackageReference Include="Configurite"     Version="8.*" />
<PackageReference Include="Configurite.Cli" Version="8.*" />
```

The user upgrades to .NET 9 by switching to `9.*`. No surprise major bumps in between.

## Three independent lines, one source

We publish **three separate `.nupkg` files** per release, all built from the same source tree:

```
Configurite.8.2.0.nupkg     →  lib/net8.0/   only
Configurite.9.2.0.nupkg     →  lib/net9.0/   only
Configurite.10.2.0.nupkg    →  lib/net10.0/  only
```

Same code, three TFMs, three versions. NuGet's resolver naturally picks the right `.nupkg` for the consuming project.

## Release cadence

### Feature release (synchronous)

A new feature lands across all supported TFMs at once:

```
8.2.0, 9.2.0, 10.2.0   →  feature X
```

The minor component is shared across the three lines for the same feature wave.

### TFM-specific patch (asynchronous)

A bug that only affects one TFM gets an isolated patch:

```
8.2.1                  →  net8-specific hotfix
9.2.x and 10.2.x stay unchanged
```

The `pack-all.sh --line net8 --patch 1` command produces only `8.2.1`.

### New .NET wave

When .NET N+1 ships, we add a new line at `(N+1).0.0` that sits alongside existing lines:

```
8.2.0, 9.2.0, 10.2.0    →  current state
... .NET 11 ships ...
8.2.0, 9.2.0, 10.2.0, 11.0.0   →  initial 11.x line
8.3.0, 9.3.0, 10.3.0, 11.1.0   →  next feature wave
```

Lines may be retired when their .NET version reaches end-of-life.

## Building locally

```bash
# Synchronous feature release across all three lines:
scripts/pack-all.sh --minor 2 --patch 0

# net8-only hotfix:
scripts/pack-all.sh --line net8 --minor 2 --patch 1
```

The script writes to `./out/`. Symbol packages (`.snupkg`) are produced alongside the main packages.

## Why this scheme?

| Benefit | Detail |
|---|---|
| Crystal-clear compatibility | `Configurite 9.x` ⇒ "I need .NET 9" — no checking the package's TFM list. |
| Stable consumer pins | A user on net8 stays on `8.*` for years; no accidental major bumps. |
| Independent servicing | A net8-specific CVE can ship as `8.x.(y+1)` without touching `9.x` or `10.x`. |
| Lockstep CLI + library | `Configurite 9.2.0` and `Configurite.Cli 9.2.0` are always source-compatible. |

## Why **not** a single multi-target package?

Microsoft's `Microsoft.Extensions.*` packages take the opposite approach: one `.nupkg` containing every supported TFM under `lib/`. That's idiomatic, but it conflates two upgrade axes (features vs. framework). With per-TFM packages, the consumer's `Version="8.*"` pin is permanent until they choose to migrate to `9.*` — never auto-bumped by us.
