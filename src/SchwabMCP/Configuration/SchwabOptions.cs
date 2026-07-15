namespace SchwabMCP.Configuration;

/// <summary>
/// Schwab developer-app credentials and OAuth settings.
/// Bind from configuration section <see cref="SectionName"/> (env: <c>Schwab__*</c>).
/// Never commit real values; see docs/authentication.md.
/// </summary>
public sealed class SchwabOptions
{
    public const string SectionName = "Schwab";

    /// <summary>Schwab developer app key (OAuth client_id).</summary>
    public string AppKey { get; set; } = "";

    /// <summary>Schwab developer app secret (OAuth client_secret).</summary>
    public string AppSecret { get; set; } = "";

    /// <summary>
    /// OAuth callback URL registered with the Schwab developer app
    /// (e.g. https://127.0.0.1:8182).
    /// </summary>
    public string CallbackUrl { get; set; } = "https://127.0.0.1:8182";

    /// <summary>
    /// Optional path to the local token store file.
    /// When null/empty, uses the platform default under LocalApplicationData/SchwabMCP.
    /// </summary>
    public string? TokenStorePath { get; set; }

    /// <summary>
    /// Optional refresh token for headless/CI setups.
    /// Prefer interactive OAuth + <see cref="Auth.ITokenStore"/> after first login.
    /// Do not put this in committed config files.
    /// </summary>
    public string? RefreshToken { get; set; }
}
