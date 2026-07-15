namespace SchwabMCP.Auth;

/// <summary>
/// OAuth tokens for the Schwab API. Treat all fields as secrets.
/// Access tokens are short-lived; refresh tokens are long-lived and must not be committed.
/// </summary>
public sealed record SchwabTokenSet(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset? AccessTokenExpiresAt = null)
{
    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>
    /// True when an access token is present and not past its expiry
    /// (with a small skew). Unknown expiry is treated as not usable for refresh-skipping.
    /// </summary>
    public bool IsAccessTokenValid(DateTimeOffset? utcNow = null, TimeSpan? skew = null)
    {
        if (!HasAccessToken)
        {
            return false;
        }

        if (AccessTokenExpiresAt is null)
        {
            return false;
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var margin = skew ?? TimeSpan.FromMinutes(1);
        return AccessTokenExpiresAt > now + margin;
    }

    /// <summary>Redacted summary safe for logs and console output.</summary>
    public string ToRedactedString()
    {
        var access = HasAccessToken ? "set" : "missing";
        var refresh = HasRefreshToken ? "set" : "missing";
        var exp = AccessTokenExpiresAt?.ToString("O") ?? "unknown";
        return $"access_token={access}; refresh_token={refresh}; access_expires={exp}";
    }
}
