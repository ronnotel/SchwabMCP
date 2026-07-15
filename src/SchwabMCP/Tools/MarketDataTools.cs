using System.ComponentModel;
using ModelContextProtocol.Server;
using SchwabMCP.Api;
using SchwabMCP.Auth;

namespace SchwabMCP.Tools;

[McpServerToolType]
public static class MarketDataTools
{
    [McpServerTool(Name = "get_quotes"),
     Description(
         "Get real-time or delayed quotes for one or more equity/ETF symbols from Schwab market data. " +
         "Pass a comma-separated list (e.g. 'AAPL' or 'AAPL,MSFT,SPY'). Requires OAuth login.")]
    public static async Task<string> GetQuotes(
        SchwabApiClient api,
        [Description("Comma-separated symbols, e.g. AAPL,MSFT,SPY")] string symbols,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbols))
        {
            return "Error: 'symbols' is required (e.g. AAPL or AAPL,MSFT).";
        }

        try
        {
            return await api.GetQuotesAsync(symbols, cancellationToken).ConfigureAwait(false);
        }
        catch (SchwabOAuthException ex)
        {
            return $"Auth error: {ex.Message}";
        }
        catch (SchwabApiException ex)
        {
            return $"API error: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
