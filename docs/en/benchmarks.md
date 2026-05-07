# Benchmarks

> Numbers below come from a single run on macOS / Apple Silicon (.NET 8, BenchmarkDotNet 0.13, `--job short`). Reproduce with `dotnet run --project benchmarks/Configurite.Benchmarks -c Release`.

The goal is to give a feel for *order of magnitude*, not vendor-comparable absolutes — your numbers will differ on different hardware, OS, and SQLite versions.

## Encryption (`AesGcmConfigEncryptor`)

| Operation | Payload | Mean | Allocated |
|---|---:|---:|---:|
| Encrypt | 16 B | **2.0 µs** | 464 B |
| Encrypt | 256 B | 2.2 µs | 1.8 KB |
| Encrypt | 4 KB | 4.2 µs | 23 KB |
| Decrypt | 16 B | **2.0 µs** | 376 B |
| Decrypt | 256 B | 2.4 µs | 1.6 KB |
| Decrypt | 4 KB | 6.9 µs | 20 KB |
| **PBKDF2 key derivation** (200 000 iterations, one-time per process) | — | **30.5 ms** | 159 B |

**Takeaway**: AES-GCM itself is essentially free at config-secret sizes (single digits of microseconds). The PBKDF2 cost is one-time per process and amortizes across every encrypted row.

## Store (`SqliteConfiguriteStore`)

> Numbers reflect Phase 17 optimisations: `journal_mode=WAL` + `synchronous=NORMAL` applied once during `EnsureSchema`, dictionary pre-sized to 64.

| Operation | Row count | Mean | Allocated |
|---|---:|---:|---:|
| `TryGet` (existing key) | any size | **6.6 µs** | 1.3 KB |
| `ReadAll` | 10 | 17 µs | 5.3 KB |
| `ReadAll` | 100 | 81 µs | 20 KB |
| `ReadAll` | 1 000 | 727 µs | 221 KB |
| `Upsert` (new key) | any size | **63 µs** | 1.8 KB |

**Takeaways**:
- `TryGet` is constant-time (index hits — independent of database size).
- `Upsert` enjoys a ~5x speedup vs. the unoptimised baseline thanks to `synchronous=NORMAL` (still WAL-safe). Batch many writes inside a single connection to amortise further.
- `ReadAll` scales roughly linearly with row count and starts with a generous dictionary capacity (64) to avoid resize churn for typical configs.

### Phase 17 before/after

| Operation | Before | After | Δ |
|---|---:|---:|---:|
| `TryGet` | 7.4 µs | 6.6 µs | **-11%** |
| `Upsert` | 310 µs | 63 µs | **-80%** |
| `ReadAll` 1 000 (alloc) | 251 KB | 221 KB | -12% |

## Provider load

| Scenario | Row count | Mean | Allocated |
|---|---:|---:|---:|
| Plain values only | 10 | **43 µs** | 13 KB |
| Plain values only | 100 | 94 µs | 44 KB |
| Plain values only | 1 000 | 600 µs | 359 KB |
| 50% encrypted (with master key resolution) | 10 | **38 ms** | 16 KB |
| 50% encrypted | 100 | 39 ms | 67 KB |
| 50% encrypted | 1 000 | 34 ms | 580 KB |

**Takeaway**: An app that uses encrypted secrets pays ~30 ms once at startup for PBKDF2; after that the per-row decrypt cost is negligible (a few µs). For an app that does **not** use encryption at all, full configuration load is sub-millisecond up to ~100 keys.

## What this means in practice

| App profile | Expected startup overhead |
|---|---|
| Tiny service, plain config | ~50 µs |
| Typical web API, ~100 keys, no encryption | ~100 µs |
| Production app with encrypted secrets | ~30 ms (one-time PBKDF2) |
| Big monolith, ~1 000 keys, half encrypted | ~35 ms |

Configurite is not on the hot path of any request — the only relevant numbers are *startup* and *hot reload*. Both finish well under any reasonable target.

## Reproducing

```bash
# All benchmarks (long form, ~5 min):
dotnet run --project benchmarks/Configurite.Benchmarks -c Release

# Short form, ~1 min, less precise:
dotnet run --project benchmarks/Configurite.Benchmarks -c Release -- --job short

# Filter to a single class:
dotnet run --project benchmarks/Configurite.Benchmarks -c Release -- --filter '*EncryptionBenchmarks*'
```

BenchmarkDotNet writes detailed reports to `BenchmarkDotNet.Artifacts/results/` (Markdown, HTML, CSV).
