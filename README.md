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
MCP / SuperGrok: **[docs/mcp.md](docs/mcp.md)**.

### Quick setup

```powershell
# Once per clone
powershell -NoProfile -File scripts/install-git-hooks.ps1

# Dev secrets (not in git)
dotnet user-secrets set "Schwab:AppKey" "YOUR_APP_KEY" --project src/SchwabMCP
dotnet user-secrets set "Schwab:AppSecret" "YOUR_APP_SECRET" --project src/SchwabMCP

# OAuth once
dotnet run --project src/SchwabMCP -- login
dotnet run --project src/SchwabMCP -- status
```

### MCP host

```powershell
dotnet build src/SchwabMCP/SchwabMCP.csproj -c Release
# Default (no subcommand) = MCP stdio server for SuperGrok
dotnet exec src\SchwabMCP\bin\Release\net10.0\SchwabMCP.dll
```

Tools: `auth_status`, `list_accounts`, `list_account_numbers`, `get_quotes`.

### Local protection

Pre-commit runs **gitleaks** on staged files and **blocks** the commit if secrets are found (`winget install gitleaks` if needed).

See [SECURITY.md](SECURITY.md) for reporting and rotation expectations.

## Repository layout

```text
src/SchwabMCP/
  Auth/           OAuth + token store
  Api/            Schwab HTTP client
  Tools/          MCP tools
  Hosting/        DI registration
docs/authentication.md
docs/mcp.md
```

## Status

- [x] Secret hygiene (gitleaks hooks + CI)
- [x] Secrets/config structure
- [x] Schwab OAuth (login / refresh / logout)
- [x] MCP stdio host + tools (accounts, quotes, auth_status)
- [ ] Broader trader API surface (orders, positions detail, etc.)

## License

[MIT](LICENSE)
