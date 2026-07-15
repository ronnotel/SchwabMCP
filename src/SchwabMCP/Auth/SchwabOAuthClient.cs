using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SchwabMCP.Configuration;

namespace SchwabMCP.Auth;

/// <summary>
/// Schwab OAuth token endpoint client (authorization_code + refresh_token grants).
/// Never logs raw tokens or client secret.
/// </summary>
public sealed class SchwabOAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly SchwabOptions _options;

    public SchwabOAuthClient(HttpClient http, IOptions<SchwabOptions> options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (_http.BaseAddress is null)
        {
            // Absolute URIs used on each request; BaseAddress optional.
        }
    }

    /// <summary>Builds the browser authorize URL for the configured app + callback.</summary>
    public string BuildAuthorizeUrl()
    {
        var clientId = Uri.EscapeDataString(SanitizeCredential(_options.AppKey));
        var redirect = Uri.EscapeDataString(_options.CallbackUrl.Trim());
        return $"{SchwabOAuthEndpoints.AuthorizeUrl}?client_id={clientId}&redirect_uri={redirect}";
    }

    /// <summary>Safe fingerprint for diagnostics (never the full secret).</summary>
    public string DescribeClientCredentials()
    {
        var key = SanitizeCredential(_options.AppKey);
        var secret = SanitizeCredential(_options.AppSecret);
        return $"AppKey length={key.Length}, prefix={Prefix(key, 4)}…; AppSecret length={secret.Length}";
    }

    /// <summary>Exchange an authorization code for access + refresh tokens.</summary>
    public Task<SchwabTokenSet> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode.Trim(),
            // Must match authorize redirect_uri and the portal app callback exactly.
            ["redirect_uri"] = _options.CallbackUrl.Trim(),
        };

        return RequestTokensAsync(form, cancellationToken);
    }

    /// <summary>Exchange a refresh token for a new access token (and often a new refresh token).</summary>
    public Task<SchwabTokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken.Trim(),
        };

        return RequestTokensAsync(form, cancellationToken);
    }

    private async Task<SchwabTokenSet> RequestTokensAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SchwabOAuthEndpoints.TokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuth());
        request.Content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SchwabOAuthException("Failed to reach Schwab token endpoint.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SchwabTokenResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SchwabTokenResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new SchwabOAuthException(
                $"Schwab token response was not valid JSON (HTTP {(int)response.StatusCode}).",
                ex)
            {
                StatusCode = (int)response.StatusCode,
            };
        }

        if (!response.IsSuccessStatusCode || parsed is null ||
            !string.IsNullOrEmpty(parsed.Error) ||
            string.IsNullOrWhiteSpace(parsed.AccessToken))
        {
            var code = parsed?.Error ?? response.StatusCode.ToString();
            var desc = parsed?.ErrorDescription ?? Truncate(body, 200);
            var message = $"Schwab token request failed ({code}): {desc}";
            if (string.Equals(parsed?.Error, "invalid_client", StringComparison.OrdinalIgnoreCase) ||
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                message +=
                    "\n\ninvalid_client / 401 means Schwab rejected AppKey+AppSecret (Basic auth), " +
                    "not the pasted redirect URL." +
                    "\nLoaded credentials: " + DescribeClientCredentials() +
                    "\nFix:" +
                    "\n  1) Portal → Apps → your app → App Details → Show App Key and Secret" +
                    "\n  2) Re-copy Secret carefully (no spaces/quotes); must be the same app as App Key" +
                    "\n  3) dotnet user-secrets set \"Schwab:AppKey\" \"...\" --project src/SchwabMCP" +
                    "\n  4) dotnet user-secrets set \"Schwab:AppSecret\" \"...\" --project src/SchwabMCP" +
                    "\n  5) Run login again (authorization codes are single-use)";
            }

            throw new SchwabOAuthException(message)
            {
                ErrorCode = parsed?.Error,
                StatusCode = (int)response.StatusCode,
            };
        }

        // Refresh grant may omit a new refresh_token; caller should merge with prior.
        var refresh = string.IsNullOrWhiteSpace(parsed.RefreshToken)
            ? ""
            : parsed.RefreshToken;

        DateTimeOffset? expires = null;
        if (parsed.ExpiresIn is > 0)
        {
            expires = DateTimeOffset.UtcNow.AddSeconds(parsed.ExpiresIn.Value);
        }

        return new SchwabTokenSet(
            AccessToken: parsed.AccessToken,
            RefreshToken: refresh,
            AccessTokenExpiresAt: expires);
    }

    private string BuildBasicAuth()
    {
        // Schwab: Authorization Basic base64("{appKey}:{appSecret}")
        var raw = $"{SanitizeCredential(_options.AppKey)}:{SanitizeCredential(_options.AppSecret)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>Trim BOM/whitespace; strip embedded newlines from botched pastes.</summary>
    internal static string SanitizeCredential(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var trimmed = value.Trim().Trim('\uFEFF', '\u200B');
        // Remove accidental wrapping quotes from shell copy/paste.
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return string.Concat(trimmed.Where(static c => !char.IsWhiteSpace(c)));
    }

    private static string Prefix(string value, int count) =>
        value.Length <= count ? value : value[..count];

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }
}
