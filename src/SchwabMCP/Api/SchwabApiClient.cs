using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SchwabMCP.Auth;

namespace SchwabMCP.Api;

/// <summary>
/// Thin Schwab Trader / Market Data HTTP client. Attaches a Bearer access token
/// from <see cref="SchwabOAuthService"/> (auto-refresh).
/// </summary>
public sealed class SchwabApiClient
{
    public const string ApiBaseUrl = "https://api.schwabapi.com/";

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
    };

    private readonly HttpClient _http;
    private readonly SchwabOAuthService _oauth;

    public SchwabApiClient(HttpClient http, SchwabOAuthService oauth)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
    }

    public Task<string> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("trader/v1/accounts", cancellationToken);

    public Task<string> GetAccountNumbersAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("trader/v1/accounts/accountNumbers", cancellationToken);

    public Task<string> GetQuotesAsync(string symbols, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbols);
        var cleaned = string.Join(",",
            symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrEmpty(cleaned))
        {
            throw new ArgumentException("Provide at least one symbol.", nameof(symbols));
        }

        var path = "marketdata/v1/quotes?symbols=" + Uri.EscapeDataString(cleaned);
        return GetJsonAsync(path, cancellationToken);
    }

    public async Task<string> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        var accessToken = await _oauth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SchwabApiException($"Failed to reach Schwab API ({relativeUrl}).", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new SchwabApiException(
                $"Schwab API {(int)response.StatusCode} {response.ReasonPhrase} for {relativeUrl}: {Truncate(body, 400)}")
            {
                StatusCode = (int)response.StatusCode,
                ResponseBody = body,
            };
        }

        return TryPrettyPrint(body);
    }

    private static string TryPrettyPrint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }
}
