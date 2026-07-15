# SchwabMCP

Schwab MCP server in .NET — Model Context Protocol tools over the Charles Schwab API.

**Public repository.** Never commit credentials.

## Secrets policy (read this first)

| Allowed | Forbidden in git |
|---|---|
| Placeholders / `.env.example` | Schwab **app key** / **app secret** |
| `dotnet user-secrets` for local dev | OAuth **access** or **refresh** tokens |
| CI / OS environment variables | Account passwords, PEM/P12 keys, `tokens.txt` |

### Local protection (required)

Install version-controlled git hooks once per clone:

```powershell
powershell -NoProfile -File scripts/install-git-hooks.ps1
```

Pre-commit runs **gitleaks** on staged files and **blocks** the commit if secrets are found. Install gitleaks if needed: `winget install gitleaks`.

### Remote protection

- GitHub Actions: [`.github/workflows/secret-scan.yml`](.github/workflows/secret-scan.yml) (full history on push/PR)
- GitHub secret scanning + push protection (enabled)

See [SECURITY.md](SECURITY.md) for reporting and rotation expectations.

## Status

Scaffolding: secret-hygiene controls only. MCP server implementation comes next.

## License

[MIT](LICENSE)
