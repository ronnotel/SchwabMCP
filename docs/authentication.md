# Authentication and secrets

SchwabMCP is a **public** open-source project. The repository and NuGet/package artifacts never contain live credentials. Each user supplies their own Schwab developer app credentials and OAuth tokens.

## What you need from Schwab

1. A [Schwab developer](https://developer.schwab.com/) application.
2. **App key** (OAuth `client_id`) and **app secret** (`client_secret`).
3. A registered **callback URL** (default in this project: `https://127.0.0.1:8182`).
4. After OAuth: a **refresh token** (long-lived) used to obtain short-lived **access tokens**.

| Secret | Where it should live | Lifetime |
|---|---|---|
| App key / app secret | Process env, user-secrets, or MCP client config | Until you rotate the app |
| Refresh token | Local token store (or env for headless only) | Days; revoke on leak |
| Access token | Memory / token store only | ~minutes |

## Configuration model

Section name: **`Schwab`**. Environment variables use double underscore:

| Setting | Env var | Required |
|---|---|---|
| App key | `Schwab__AppKey` | Yes |
| App secret | `Schwab__AppSecret` | Yes |
| Callback URL | `Schwab__CallbackUrl` | No (default `https://127.0.0.1:8182`) |
| Token store path | `Schwab__TokenStorePath` | No (platform default) |
| Refresh token | `Schwab__RefreshToken` | No (prefer OAuth + store) |

Committed `appsettings.json` only has non-secret defaults (callback URL). It does **not** contain keys or tokens.

## Recommended: environment variables (MCP hosts)

Claude Desktop / Cursor-style MCP config:

```json
{
  "mcpServers": {
    "schwab": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/SchwabMCP/src/SchwabMCP"],
      "env": {
        "Schwab__AppKey": "YOUR_APP_KEY",
        "Schwab__AppSecret": "YOUR_APP_SECRET"
      }
    }
  }
}
```

If your MCP config file is synced to the cloud or checked into git, treat it as a secrets file—or inject env from the OS instead.

## Recommended for library development: user-secrets

```powershell
dotnet user-secrets set "Schwab:AppKey" "YOUR_APP_KEY" --project src/SchwabMCP
dotnet user-secrets set "Schwab:AppSecret" "YOUR_APP_SECRET" --project src/SchwabMCP
```

User-secrets live under your user profile, not in the repo.

## Token store (refresh tokens)

After OAuth (or when you save tokens programmatically), tokens are written via `ITokenStore`.

**Default path**

| OS | Path |
|---|---|
| Windows | `%LOCALAPPDATA%\SchwabMCP\tokens.dpapi` (DPAPI, current user) |
| Linux / macOS | `$XDG` / LocalApplicationData `SchwabMCP/tokens.json` (owner read/write when supported) |

Override with `Schwab__TokenStorePath` if needed. Never place the store under the git working tree.

**Resolution order** (`TokenProvider`):

1. Tokens from `ITokenStore` (file).
2. Else optional `Schwab__RefreshToken` (headless/CI).
3. Else no tokens (OAuth required before API calls).

## Fail-closed validation

On startup, missing `AppKey` / `AppSecret` (or an invalid callback URL) fails with a clear error and exit code `1`. The process does not call Schwab with empty credentials.

Verify locally (with secrets set):

```powershell
dotnet run --project src/SchwabMCP
```

Without secrets you should see validation errors pointing here—not a hang or a vague HTTP failure.

## What never goes in git

- Real app keys / secrets  
- Refresh or access tokens  
- Filled `.env`  
- `appsettings.Development.json` with live values  
- Token cache files (`tokens.json`, `tokens.dpapi`, etc.)

Pre-commit **gitleaks** and CI secret-scan block many of these; do not use `--no-verify` to bypass.

## Leak response

1. Revoke or rotate the Schwab application credentials in the developer portal.  
2. Delete the local token store file (or call `ITokenStore.ClearAsync`).  
3. If a secret was committed or pushed, treat it as public: rotate, then purge history if needed.  
4. See [SECURITY.md](../SECURITY.md).

## Code map

| Type | Role |
|---|---|
| `SchwabOptions` | Bound configuration (app key/secret, callback, paths) |
| `SchwabOptionsValidator` | Fail-closed required-field checks |
| `ITokenStore` | Persist/load/clear OAuth tokens |
| `ProtectedFileTokenStore` | Default disk store (DPAPI on Windows) |
| `MemoryTokenStore` | Tests / ephemeral sessions |
| `TokenProvider` | Store + optional env refresh resolution |
| `Hosting.ServiceCollectionExtensions.AddSchwabAuth` | DI registration |

Interactive OAuth and live Schwab HTTP calls are implemented separately on top of this layer.
