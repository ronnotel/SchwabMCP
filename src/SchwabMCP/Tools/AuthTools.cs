using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SchwabMCP.Auth;
using SchwabMCP.Configuration;

namespace SchwabMCP.Tools;

[McpServerToolType]
public static class AuthTools
{
    [McpServerTool(Name = "auth_status"),
     Description(
         "Show SchwabMCP auth status without revealing secrets: whether AppKey/AppSecret are set, " +
         "whether OAuth tokens exist, access-token expiry, and token store location.")]
    public static async Task<string> AuthStatus(
        TokenProvider tokens,
        ITokenStore store,
        IOptions<SchwabOptions> options,
        CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var current = await tokens.GetAsync(cancellationToken).ConfigureAwait(false);

        var payload = new
        {
            appKey = string.IsNullOrWhiteSpace(opts.AppKey) ? "missing" : "set",
            appSecret = string.IsNullOrWhiteSpace(opts.AppSecret) ? "missing" : "set",
            callbackUrl = opts.CallbackUrl,
            tokenStore = store.Description,
            envRefreshToken = string.IsNullOrWhiteSpace(opts.RefreshToken) ? "missing" : "set",
            tokens = current is null
                ? new
                {
                    present = false,
                    accessToken = "missing",
                    refreshToken = "missing",
                    accessTokenExpiresAt = (string?)null,
                    accessTokenValid = false,
                }
                : new
                {
                    present = true,
                    accessToken = current.HasAccessToken ? "set" : "missing",
                    refreshToken = current.HasRefreshToken ? "set" : "missing",
                    accessTokenExpiresAt = current.AccessTokenExpiresAt?.ToString("O"),
                    accessTokenValid = current.IsAccessTokenValid(),
                },
            hint = current is null || !current.HasRefreshToken
                ? "Not logged in. Run: dotnet run --project src/SchwabMCP -- login"
                : "OK — API tools can obtain a Bearer access token.",
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
