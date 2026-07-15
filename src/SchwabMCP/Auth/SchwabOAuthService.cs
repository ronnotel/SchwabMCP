using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchwabMCP.Configuration;

namespace SchwabMCP.Auth;

/// <summary>
/// Interactive login, token refresh, and access-token resolution on top of
/// <see cref="SchwabOAuthClient"/> + <see cref="ITokenStore"/>.
/// </summary>
public sealed class SchwabOAuthService
{
    private readonly SchwabOAuthClient _oauth;
    private readonly TokenProvider _tokens;
    private readonly SchwabOptions _options;
    private readonly ILogger<SchwabOAuthService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SchwabOAuthService(
        SchwabOAuthClient oauth,
        TokenProvider tokens,
        IOptions<SchwabOptions> options,
        ILogger<SchwabOAuthService> logger)
    {
        _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string BuildAuthorizeUrl() => _oauth.BuildAuthorizeUrl();

    /// <summary>
    /// Runs the authorization_code flow: open browser, capture code (listener or paste),
    /// exchange for tokens, persist via <see cref="ITokenStore"/>.
    /// </summary>
    public async Task<SchwabTokenSet> LoginInteractiveAsync(
        TimeSpan timeout,
        bool openBrowser = true,
        bool preferListener = true,
        CancellationToken cancellationToken = default)
    {
        var authorizeUrl = _oauth.BuildAuthorizeUrl();
        _logger.LogInformation("Starting Schwab OAuth login (callback {Callback})", _options.CallbackUrl);

        string authorizationCode;

        if (preferListener)
        {
            await using var listener = new OAuthCallbackListener(_options.CallbackUrl);
            if (listener.TryStart(out var listenError))
            {
                Console.WriteLine("Listening for OAuth redirect on:");
                Console.WriteLine($"  {listener.Prefix}");
                Console.WriteLine();
                Console.WriteLine("Opening browser for Schwab authorization…");
                Console.WriteLine("If the browser does not open, visit:");
                Console.WriteLine($"  {authorizeUrl}");
                Console.WriteLine();

                if (openBrowser)
                {
                    TryOpenBrowser(authorizeUrl);
                }

                try
                {
                    authorizationCode = await listener
                        .WaitForAuthorizationCodeAsync(timeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is TimeoutException or HttpListenerException)
                {
                    _logger.LogWarning(ex, "OAuth listener failed; falling back to paste mode");
                    Console.WriteLine();
                    Console.WriteLine($"Listener did not receive a callback ({ex.Message}).");
                    authorizationCode = await PromptForCodeOrRedirectAsync(authorizeUrl, openBrowser: false)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                Console.WriteLine("Could not start a local callback listener:");
                Console.WriteLine($"  {listenError}");
                Console.WriteLine();
                Console.WriteLine(
                    "HTTPS callbacks need a certificate bound to the port, which many dev machines lack.");
                Console.WriteLine("Falling back to paste mode.");
                Console.WriteLine();
                authorizationCode = await PromptForCodeOrRedirectAsync(authorizeUrl, openBrowser)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            authorizationCode = await PromptForCodeOrRedirectAsync(authorizeUrl, openBrowser)
                .ConfigureAwait(false);
        }

        Console.WriteLine("Exchanging authorization code for tokens…");
        var tokenSet = await _oauth
            .ExchangeAuthorizationCodeAsync(authorizationCode, cancellationToken)
            .ConfigureAwait(false);

        if (!tokenSet.HasRefreshToken)
        {
            throw new SchwabOAuthException(
                "Schwab did not return a refresh_token. Check app product access and try again.");
        }

        await _tokens.SaveAsync(tokenSet, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("OAuth tokens saved ({Summary})", tokenSet.ToRedactedString());
        return tokenSet;
    }

    /// <summary>
    /// Returns a usable access token, refreshing from the store when expired or missing.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var current = await _tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || !current.HasRefreshToken)
        {
            throw new SchwabOAuthException(
                "No refresh token on file. Run: dotnet run --project src/SchwabMCP -- login");
        }

        if (current.IsAccessTokenValid())
        {
            return current.AccessToken;
        }

        var refreshed = await RefreshAndSaveAsync(current, cancellationToken).ConfigureAwait(false);
        return refreshed.AccessToken;
    }

    /// <summary>Force a refresh using the stored (or env) refresh token.</summary>
    public async Task<SchwabTokenSet> RefreshStoredAsync(CancellationToken cancellationToken = default)
    {
        var current = await _tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || !current.HasRefreshToken)
        {
            throw new SchwabOAuthException(
                "No refresh token on file. Run: dotnet run --project src/SchwabMCP -- login");
        }

        return await RefreshAndSaveAsync(current, cancellationToken).ConfigureAwait(false);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _tokens.ClearAsync(cancellationToken);

    private async Task<SchwabTokenSet> RefreshAndSaveAsync(
        SchwabTokenSet current,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-read under lock in case another waiter already refreshed.
            var latest = await _tokens.GetAsync(cancellationToken).ConfigureAwait(false) ?? current;
            if (latest.IsAccessTokenValid())
            {
                return latest;
            }

            if (!latest.HasRefreshToken)
            {
                throw new SchwabOAuthException(
                    "No refresh token available to renew the access token. Run login again.");
            }

            _logger.LogInformation("Refreshing Schwab access token");
            var refreshed = await _oauth.RefreshAsync(latest.RefreshToken, cancellationToken)
                .ConfigureAwait(false);

            // Schwab may omit refresh_token on refresh; keep the previous one.
            if (!refreshed.HasRefreshToken)
            {
                refreshed = refreshed with { RefreshToken = latest.RefreshToken };
            }

            await _tokens.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static async Task<string> PromptForCodeOrRedirectAsync(string authorizeUrl, bool openBrowser)
    {
        Console.WriteLine("Authorize this app in your browser.");
        Console.WriteLine();
        Console.WriteLine(authorizeUrl);
        Console.WriteLine();

        if (openBrowser)
        {
            TryOpenBrowser(authorizeUrl);
        }

        Console.WriteLine("After login, Schwab redirects to your callback URL.");
        Console.WriteLine("Copy the FULL address from the browser bar (it will look like it failed to load)");
        Console.WriteLine("and paste it here, then press Enter.");
        Console.WriteLine("(You can also paste only the authorization code.)");
        Console.WriteLine();
        Console.Write("> ");

        var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new SchwabOAuthException("No redirect URL or code was pasted.");
        }

        return OAuthCallbackListener.ParseCodeOrRedirectUrl(line);
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch
        {
            Console.WriteLine("(Could not open a browser automatically — open the URL above manually.)");
        }
    }
}
