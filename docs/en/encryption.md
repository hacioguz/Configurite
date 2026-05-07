# Encryption model

## Algorithm

| Layer | Choice | Rationale |
|---|---|---|
| Cipher | **AES-256-GCM** | Authenticated encryption — tampering throws on decrypt. Native `AesGcm` in .NET 8. |
| KDF | **PBKDF2-HMAC-SHA256** | OWASP-recommended, BCL-native (no third-party deps). |
| Iterations | **200 000** | OWASP 2023 minimum for SHA-256. |
| Per-DB salt | **32 bytes** random | Stored unencrypted in `Metadata.EncryptionSalt`. |
| Per-value nonce | **12 bytes** random | Identical plaintexts never produce identical ciphertexts. |
| Auth tag | **16 bytes** | GCM standard. |

## On-disk layout (per encrypted value)

```
base64( nonce(12) ‖ ciphertext(n) ‖ tag(16) )
```

A row in the `Configuration` table:

| Column | Encrypted value | Plain value |
|---|---|---|
| `Key` | `ConnectionStrings:Default` | `AppName` |
| `Value` | `dhO+FTdRdGm…` | `Configurite Demo` |
| `IsEncrypted` | `1` | `0` |

## Master-key resolution

```text
ConfiguriteOptions.MasterKey  ──►  CONFIGURITE_MASTER_KEY env  ──►  ~/.configurite/master.key
```

If a row has `IsEncrypted = 1` but the resolver returns nothing, `Load()` throws an `InvalidOperationException` describing the available sources.

## Key rotation

1. Decrypt with the old key (load configuration normally).
2. Read each `IsEncrypted = 1` row.
3. Re-encrypt with the new key + a freshly generated salt.
4. Replace `Metadata.EncryptionSalt` and rewrite the row.

A built-in helper for rotation will land in a future release; until then the loop above is straightforward with the public store API exposed by the migrator.

## Threat model

Configurite **does** protect against:

- An attacker who reads the SQLite file from disk (backup tape, leaked container image, lost laptop).
- A misconfigured Git push that leaks the database (no plaintext to grep for).
- Malicious modification of encrypted values (GCM tag verification).

Configurite **does not** protect against:

- An attacker with both the database file and the master key.
- Memory dumps of the running process.
- A compromised application binary substituting the encryptor.

Combine Configurite with OS-level secret stores (Vault, KMS, Keychain) for defence in depth.
