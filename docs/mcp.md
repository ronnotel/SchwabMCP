# MCP host (SuperGrok / Claude Desktop / Cursor)

SchwabMCP speaks **MCP over stdio**. When launched with no subcommand (or with `mcp` / `serve`), it stays running and exposes tools to the host.

## Prerequisites

1. App key/secret configured (`user-secrets` or env) — see [authentication.md](authentication.md)
2. OAuth login completed once:

   ```powershell
   dotnet run --project src/SchwabMCP -- login
   dotnet run --project src/SchwabMCP -- status
   ```

3. Release build (recommended for hosts):

   ```powershell
   dotnet build src/SchwabMCP/SchwabMCP.csproj -c Release
   ```

## Tools

| Tool | Description |
|---|---|
| `auth_status` | Redacted auth/token presence |
| `list_accounts` | `GET /trader/v1/accounts` |
| `list_account_numbers` | `GET /trader/v1/accounts/accountNumbers` |
| `get_quotes` | `GET /marketdata/v1/quotes?symbols=` |

In SuperGrok, tools appear as `schwab__auth_status`, `schwab__list_accounts`, etc. (server name + `__` + tool).

## SuperGrok (`~/.grok/config.toml`)

```toml
[mcp_servers.schwab]
command = "dotnet"
args = [
  "exec",
  "C:\\Users\\ronno\\Source\\SchwabMCP\\src\\SchwabMCP\\bin\\Release\\net10.0\\SchwabMCP.dll",
]
enabled = true
startup_timeout_sec = 60
tool_timeout_sec = 120

[mcp_servers.schwab.env]
# Prefer OS user env vars instead of hardcoding:
Schwab__AppKey = "${Schwab__AppKey}"
Schwab__AppSecret = "${Schwab__AppSecret}"
```

If AppKey/AppSecret already live in **user-secrets** on this machine and the process runs as your user, you can omit the `env` table — the DLL loads user-secrets at startup. Env is required when SuperGrok runs under a context without your user-secrets store.

CLI alternative:

```powershell
grok mcp add schwab `
  -e "Schwab__AppKey=${Schwab__AppKey}" `
  -e "Schwab__AppSecret=${Schwab__AppSecret}" `
  -- dotnet exec "C:\Users\ronno\Source\SchwabMCP\src\SchwabMCP\bin\Release\net10.0\SchwabMCP.dll"
```

Then:

```powershell
grok mcp list
grok mcp doctor schwab
```

In the TUI: `/mcps` → confirm **schwab** is connected and tools are listed.

## Manual stdio smoke test

The process should sit quietly (no banner on stdout):

```powershell
dotnet exec "C:\Users\ronno\Source\SchwabMCP\src\SchwabMCP\bin\Release\net10.0\SchwabMCP.dll"
# Ctrl+C to stop
```

Logs go to **stderr** only.

## CLI vs MCP

| Command | Behavior |
|---|---|
| *(none)* / `mcp` / `serve` | MCP stdio server |
| `status` / `login` / `refresh` / `logout` / `creds` | Interactive CLI (stdout OK) |

## Notes

- Tokens remain in `%LOCALAPPDATA%\SchwabMCP\tokens.dpapi` (Windows).
- Do not put refresh tokens in SuperGrok config for daily use.
- After `logout` or refresh-token expiry, run `login` again, then restart the MCP host.
