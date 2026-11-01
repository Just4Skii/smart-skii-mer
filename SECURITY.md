# Security policy

## Reporting

Open a private security advisory on GitHub, or email the maintainer listed on the profile. Please do not open a public issue for a vulnerability.

## Threat model

Smart Skii-mer is a **local desktop application**. There is no backend, no database, and no multi-user service.

| Asset | Where it lives | Protection |
|---|---|---|
| AI provider key | `%APPDATA%\Smart Skii-mer\provider.key` / `nvidia.key` | Windows DPAPI, `CurrentUser` scope; never serialised into settings or logs |
| Settings | `%APPDATA%\Smart Skii-mer\settings.json` | Per-user ACL; contains no secrets |
| Phone pairing code | Memory only | Random 4 digits, rotated after each pairing and after five failures |
| Phone session token | Phone `localStorage` + desktop memory | 32 random bytes, 12-hour expiry |
| Crash log | `%APPDATA%\Smart Skii-mer\crash.log` | Local only; no keys are written by the app |

### Network exposure

- The phone server listens on a fixed port and **rejects non-private addresses**.
- Commands are an allow-list (`quick_clean`, `deep_clean`, `free_background`, `status`, six mode switches). No path, process name, or arbitrary input is executed.
- The companion page is served from the same origin; CSP restricts scripts to inline and self, and framing is denied.
- Outbound traffic only goes to the AI endpoint the user configures.

### Known limitations

- The installer is **not code-signed**, so SmartScreen warns on download. Verify the published SHA-256 or build from source.
- A process running as the same Windows user can decrypt DPAPI-protected keys. This is inherent to per-user secret storage on Windows.
- API keys embedded in a distributed binary are extractable. Do not ship a preconfigured build; let each user supply their own key.

### Out of scope

Vulnerabilities requiring administrator or SYSTEM privileges, physical access, or modification of the binary are out of scope.

## Supported versions

Only the latest release receives security fixes.
