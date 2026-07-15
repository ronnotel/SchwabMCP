# Security Policy

## Reporting a vulnerability

**Do not** open a public GitHub issue that includes live credentials, session tokens, or exploit details that would harm users.

Report privately to the repository owner:

- GitHub: [@ronnotel](https://github.com/ronnotel)

Please include a description, impact, minimal reproduction, and whether any credentials may have been exposed.

## Secrets and credentials

This repository is **public**. Treat every commit as world-readable.

| Do | Don't |
|---|---|
| Use `dotnet user-secrets`, environment variables, or a secret store | Commit Schwab app keys, app secrets, or refresh tokens |
| Keep `.env` and token caches gitignored | Commit `appsettings.Development.json` with real values |
| Rotate immediately if a secret was ever staged or pushed | Use `--no-verify` to skip the secret hook |

Schwab OAuth **refresh tokens** are long-lived. If one lands in git history, revoke/rotate it in the Schwab developer portal and treat the commit as a credential incident.

## Automated detection

| Control | Where |
|---|---|
| Pre-commit secret scan | Local `gitleaks protect --staged` via `.githooks/pre-commit` |
| Push / PR secret scan | `.github/workflows/secret-scan.yml` |
| GitHub native | Secret scanning + push protection (enabled on this repo) |

### Install local hooks (once per clone)

```powershell
powershell -NoProfile -File scripts/install-git-hooks.ps1
```

```sh
sh scripts/install-git-hooks.sh
```

Requires [gitleaks](https://github.com/gitleaks/gitleaks) on `PATH` (`winget install gitleaks` on Windows).
