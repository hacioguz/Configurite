# Security architecture

> Last reviewed: 2026-04-28 (Configurite 8.4 / 9.4 / 10.4)

This document is for security engineers, auditors, and operators evaluating Configurite for sensitive workloads.

## TL;DR

- **Algorithms**: AES-256-GCM (authenticated encryption) + PBKDF2-HMAC-SHA256 (200 000 iterations).
- **Per-database 32-byte salt** + **per-encryption 12-byte nonce**.
- Master key **never persists in the database**.
- Tampering is detected by GCM tag verification; modified ciphertext throws on read.
- Zero known vulnerabilities in any direct or transitive dependency (verified `dotnet list package --vulnerable` 2026-04-28).

## Cryptographic primitives

| Layer | Choice | Standard | Why |
|---|---|---|---|
| Symmetric cipher | AES-256-GCM | NIST SP 800-38D | Authenticated encryption; FIPS-approved; native in .NET 8+ (`AesGcm`). |
| Key derivation | PBKDF2-HMAC-SHA256, 200 000 iter | RFC 8018 / OWASP 2023 | BCL-native (no third-party crypto deps); meets OWASP minimum for SHA-256. |
| Nonce | 12 bytes random | NIST SP 800-38D §8.2 | GCM standard; `RandomNumberGenerator.GetBytes` (CSPRNG). |
| Salt | 32 bytes random per database | OWASP recommendation | Stored unencrypted in `Metadata.EncryptionSalt`. |
| Auth tag | 16 bytes (default) | NIST SP 800-38D | GCM standard; full integrity guarantee. |

### Why not Argon2id?

Argon2id is the modern winner against GPU attacks, but the .NET BCL doesn't ship it. Adding a third-party crypto package would expand the trust surface — we chose to stay BCL-only and use PBKDF2 with iteration counts well above OWASP minimums. Open issue for re-evaluation in .NET 11.

### Ciphertext layout

```
base64( nonce(12) ‖ ciphertext(n) ‖ tag(16) )
```

A truncated, tampered, or short payload throws `CryptographicException` on read — the provider surfaces this as a configuration load failure, not a missing value.

## Threat model

### Assets

| Asset | Sensitivity |
|---|---|
| Plaintext configuration values (especially secrets like ConnectionStrings, ApiKeys, Passwords) | High |
| The master key | Critical |
| The SQLite database file | Medium (encrypted secrets are useless without the master key) |
| The audit log | Medium (reveals operational metadata: who changed what when) |

### Trust boundaries

```
┌─────────────────────────┐         ┌──────────────────┐
│  Application process    │  reads  │  appsettings.db  │
│  (has master key in mem)│ ──────► │  (on disk)       │
└─────────────────────────┘         └──────────────────┘
        ▲                                    ▲
        │                                    │
   master key from                    backup / git /
   env, keyfile, vault                container image
```

Configurite operates *inside* the application process boundary. The master key flows in via the resolver chain; encrypted values flow out via decrypted reads.

### Attacker capabilities — protected against

| Scenario | Mitigation |
|---|---|
| Attacker reads SQLite file from disk | Encrypted values useless without master key. |
| SQLite file leaked to backup tape, container image, or Git repo | Same — encryption-at-rest. |
| Attacker tampers with stored ciphertext (bit flip, swap rows) | GCM tag verification fails on read; throws. |
| Attacker replays an old encrypted value into a new key/env | Each ciphertext is bound to its salt; rotation refreshes the salt. |
| Attacker observes multiple encryptions of the same plaintext | Per-encryption random nonce — ciphertexts differ. |
| Master key leaks via env var inspection (e.g. `/proc/<pid>/environ`) | Out of scope; OS-level concern. Use a secret manager instead. |

### Attacker capabilities — NOT protected against

| Scenario | Recommendation |
|---|---|
| Attacker has both the database file **and** the master key | Use OS-level secret manager (Vault, KMS, Keychain) for the master key; never co-locate. |
| Memory dump of running process | OS-level hardening (memory protection, restricted ptrace, no swap of sensitive pages). |
| Compromised application binary | Code signing + supply-chain controls (Sigstore, signed nupkgs). |
| Side-channel attacks (timing, cache) on AES-GCM | Use AES-NI hardware (default on x64/arm64). |
| Master key brute force at the application | Use a high-entropy master key (≥ 32 chars random). |
| Audit log forgery by an attacker with write access | The audit log is in the same DB; there is no append-only / cryptographic chain. v9.x roadmap. |

### Specific attack scenarios

#### A1: A developer accidentally commits `appsettings.db`

✅ **Mitigated**. Without the master key, ciphertexts are computationally infeasible to recover (AES-256 + PBKDF2 200 K iter on a fresh salt). Plain values (`AppName`, `Logging:LogLevel:Default`, etc.) are exposed by the schema — that's by design; only sensitive paths should be encrypted.

#### A2: Attacker swaps a value's ciphertext in-place

✅ **Mitigated**. GCM verifies authenticity. Substituting bytes triggers `CryptographicException`.

#### A3: Attacker replaces the entire database with a doctored one

⚠️ **Partial**. The file's path is implicit-trust. If the attacker has filesystem write access, they can swap the file. Out-of-band integrity checks (filesystem ACLs, immutable mounts, checksumming via deployment system) should complement Configurite.

#### A4: Master key rotation while the app is running

✅ **Atomic**. The rotator updates salt + every encrypted row inside one SQLite transaction. A crash mid-rotation rolls back fully — the app never observes a partial state.

#### A5: Audit log tampering

⚠️ **Open**. An attacker with DB write access can edit or truncate the audit log. We document this; for high-assurance environments, ship audit entries to an external append-only log (e.g. via `IConfiguriteAuditLog` decorator that fans out to syslog/SIEM).

## Operational guidance

### Master key handling

| ❌ Don't | ✅ Do |
|---|---|
| Bake the key into a Docker image. | Inject via runtime env or mount a key file with `chmod 600`. |
| Commit `~/.configurite/master.key` to Git. | Store outside the repo; sync via secret manager. |
| Reuse the same key across environments. | One key per environment; rotate on the same cadence as TLS certs. |
| Use short or memorable keys. | ≥ 32 chars random (`openssl rand -base64 24`). |

### Backup & restore

The encrypted SQLite file is safe to back up *as-is*. To restore on a new machine, you also need the master key. **Document the recovery procedure** — losing the key is equivalent to losing the data.

### Log scrubbing

Log infrastructure should never log decrypted values. The provider does not log plaintext. If you log `IConfiguration["ConnectionStrings:Default"]` in your own code, that's on you.

### Production checklist

- [ ] Master key managed by a secret store (Vault/KMS/Keychain), not env var on the host.
- [ ] `Configurite.AdminUI` mounted behind `RequireAuthorization` with role-based policy.
- [ ] Audit log retention policy defined.
- [ ] Master key rotation cadence documented (we recommend ≤ 90 days).
- [ ] Backups tested with a full restore + decrypt drill.
- [ ] CI pipeline runs `dotnet list package --vulnerable` (we do — see `.github/workflows/ci.yml`).

## Reporting a vulnerability

Please use GitHub Security Advisories on the repository. Coordinated-disclosure window: 90 days from acknowledgement.

## Audit log

We maintain a CHANGELOG of security-relevant changes:

| Version | Date | Change |
|---|---|---|
| 8.0 / 9.0 / 10.0 | 2026-04 | Initial release with the model documented above. |
| 8.1 / 9.1 / 10.1 | 2026-04 | Atomic key rotation. |
| 8.4 / 9.4 / 10.4 | 2026-04 | Audit log infrastructure (note: not append-only). |

A full security review by an independent party is **not** in scope yet. We will publish such reviews here when they happen.
