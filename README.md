# SchwabMCP

Schwab MCP server in .NET — Model Context Protocol tools over the Charles Schwab API.

**Public repository.** Never commit credentials. Each user brings their own Schwab developer app.

## Secrets policy

| Allowed | Forbidden in git |
|---|---|
| Placeholders / [`.env.example`](.env.example) | Schwab **app key** / **app secret** |
| `dotnet user-secrets` for local dev | OAuth **access** or **refresh** tokens |
| CI / OS / MCP host environment variables | Account passwords, PEM/P12 keys, `tokens.txt` |

Full guide: **[docs/authentication.md](docs/authentication.md)**.

### Quick setup

```powershell
# Once per clone
powershell -NoProfile -File scripts/install-git-hooks.ps1

# Dev secrets (not in git)
dotnet user-secrets set "Schwab:AppKey" "YOUR_APP_KEY" --project src/SchwabMCP
dotnet user-secrets set "Schwab:AppSecret" "YOUR_APP_SECRET" --project src/SchwabMCP

# Validate config (prints presence only, never raw secrets)
dotnet run --project src/SchwabMCP
```

Or set `Schwab__AppKey` / `Schwab__AppSecret` in the environment (preferred for MCP clients).

Refresh tokens are stored under your user profile (`%LOCALAPPDATA%\SchwabMCP\` on Windows), not in the repo.

### Local protection

Pre-commit runs **gitleaks** on staged files and **blocks** the commit if secrets are found (`winget install gitleaks` if needed).

### Remote protection

- GitHub Actions: [`.github/workflows/secret-scan.yml`](.github/workflows/secret-scan.yml)
- GitHub secret scanning + push protection

See [SECURITY.md](SECURITY.md) for reporting and rotation expectations.

## Repository layout

```text
src/SchwabMCP/           MCP host + auth/config (this package)
  Configuration/         SchwabOptions + fail-closed validation
  Auth/                  ITokenStore, DPAPI/file store, TokenProvider
  Hosting/               DI registration (AddSchwabAuth)
docs/authentication.md   How to supply secrets safely
.env.example             Env var names only
```

## Status

- [x] Secret hygiene (gitleaks hooks + CI)
- [x] Secrets/config structure (options, token store, validation)
- [ ] MCP protocol host + tools
- [ ] Schwab OAuth + API client

## License

[MIT](LICENSE)
