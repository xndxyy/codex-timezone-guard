# Contributing

Please keep the project Codex-desktop-only and preserve the isolation and privacy
constraints documented in SECURITY.md. Never add global timezone/proxy writes,
client termination, credential scraping or silent proxy bypass as a convenience.

Use the .NET SDK selected by global.json on Windows x64. Run:

```powershell
.\scripts\verify.ps1
.\scripts\build.ps1
```

Add a failing regression test before fixes. Keep fixtures distinct from real
Codex verification and do not require a logged-in Codex account for CI. Redact
public IPs, personal paths and credentials from all submitted evidence. Do not
commit .cache/, artifacts/, outputs/, settings or binary runtime dependencies.

Before changing detection providers or permissions, update PRIVACY.md and
describe exactly which information leaves the machine. Contributions are
licensed under the project's MIT license.
