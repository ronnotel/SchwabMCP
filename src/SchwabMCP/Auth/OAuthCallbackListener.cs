using System.Net;
using System.Text;

namespace SchwabMCP.Auth;

/// <summary>
/// Listens for the Schwab OAuth redirect on the configured callback URL.
/// HTTPS requires a certificate bound to the port (often unavailable on dev machines);
/// callers should fall back to paste mode when <see cref="TryStart"/> fails.
/// </summary>
public sealed class OAuthCallbackListener : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Uri _callbackUri;
    private bool _started;

    public OAuthCallbackListener(string callbackUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);
        if (!Uri.TryCreate(callbackUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Callback URL must be an absolute URI.", nameof(callbackUrl));
        }

        _callbackUri = uri;
    }

    public Uri CallbackUri => _callbackUri;

    /// <summary>HttpListener prefix (must end with /).</summary>
    public string Prefix
    {
        get
        {
            var builder = new UriBuilder(_callbackUri)
            {
                Path = string.IsNullOrEmpty(_callbackUri.AbsolutePath) || _callbackUri.AbsolutePath == "/"
                    ? "/"
                    : _callbackUri.AbsolutePath.TrimEnd('/') + "/",
                Query = null,
                Fragment = null,
            };
            return builder.Uri.AbsoluteUri;
        }
    }

    public bool TryStart(out string? error)
    {
        error = null;
        try
        {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _started = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Wait for one redirect containing <c>code</c> (or surface <c>error</c>).
    /// </summary>
    public async Task<string> WaitForAuthorizationCodeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            throw new InvalidOperationException("Listener was not started.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var contextTask = _listener.GetContextAsync();
            var completed = await Task.WhenAny(
                    contextTask,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token))
                .ConfigureAwait(false);

            if (completed != contextTask)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:0}s waiting for OAuth callback on {Prefix}");
            }

            var context = await contextTask.ConfigureAwait(false);
            return await HandleContextAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0}s waiting for OAuth callback on {Prefix}");
        }
    }

    private static async Task<string> HandleContextAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        var requestUrl = context.Request.Url
            ?? throw new SchwabOAuthException("Callback request had no URL.");

        string? responseHtml;
        string code;
        try
        {
            code = ExtractAuthorizationCode(requestUrl);
            responseHtml =
                "<!DOCTYPE html><html><body style='font-family:sans-serif;padding:2rem'>" +
                "<h1>SchwabMCP</h1><p>Authorization received. You can close this window and return to the terminal.</p>" +
                "</body></html>";
        }
        catch (Exception ex)
        {
            responseHtml =
                "<!DOCTYPE html><html><body style='font-family:sans-serif;padding:2rem'>" +
                $"<h1>SchwabMCP</h1><p>Authorization failed: {WebUtility.HtmlEncode(ex.Message)}</p>" +
                "</body></html>";
            await WriteResponseAsync(context.Response, 400, responseHtml, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        await WriteResponseAsync(context.Response, 200, responseHtml, cancellationToken)
            .ConfigureAwait(false);
        return code;
    }

    public static string ExtractAuthorizationCode(Uri redirectUri)
    {
        var query = ParseQuery(redirectUri.Query);
        if (query.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
        {
            query.TryGetValue("error_description", out var desc);
            throw new SchwabOAuthException(
                string.IsNullOrEmpty(desc)
                    ? $"Schwab authorization error: {error}"
                    : $"Schwab authorization error: {error} — {desc}")
            {
                ErrorCode = error,
            };
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new SchwabOAuthException(
                "Callback URL did not include an authorization code. " +
                "Paste the full browser redirect URL (address bar), not just a fragment.");
        }

        return code.Trim();
    }

    /// <summary>
    /// Accepts either a full redirect URL or a bare authorization code.
    /// </summary>
    public static string ParseCodeOrRedirectUrl(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var trimmed = input.Trim().Trim('"', '\'');

        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                // Schwab sometimes returns https://127.0.0.1/?code=... which is fine;
                // malformed pastes fail here.
                throw new SchwabOAuthException("Could not parse the pasted redirect URL.");
            }

            return ExtractAuthorizationCode(uri);
        }

        // Bare code (may contain @ after decode)
        if (trimmed.Contains('=', StringComparison.Ordinal) &&
            trimmed.Contains("code=", StringComparison.OrdinalIgnoreCase))
        {
            // Query string without host
            var fake = "https://local/?" + trimmed.TrimStart('?');
            return ExtractAuthorizationCode(new Uri(fake));
        }

        return trimmed;
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        int statusCode,
        string html,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var q = query.TrimStart('?');
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(part)] = "";
            }
            else
            {
                var key = Uri.UnescapeDataString(part[..idx]);
                var val = Uri.UnescapeDataString(part[(idx + 1)..]);
                result[key] = val;
            }
        }

        return result;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_started && _listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }
        catch
        {
            // ignore shutdown races
        }

        return ValueTask.CompletedTask;
    }
}
