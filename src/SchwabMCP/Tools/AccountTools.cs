using System.ComponentModel;
using ModelContextProtocol.Server;
using SchwabMCP.Api;
using SchwabMCP.Auth;

namespace SchwabMCP.Tools;

[McpServerToolType]
public static class AccountTools
{
    [McpServerTool(Name = "list_accounts"),
     Description(
         "List Schwab brokerage accounts linked to the logged-in user " +
         "(GET /trader/v1/accounts). Requires a prior OAuth login.")]
    public static async Task<string> ListAccounts(
        SchwabApiClient api,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SchwabOAuthException ex)
        {
            return $"Auth error: {ex.Message}";
        }
        catch (SchwabApiException ex)
        {
            return $"API error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_account_numbers"),
     Description(
         "List Schwab account number hashes used by other trader endpoints " +
         "(GET /trader/v1/accounts/accountNumbers). Requires OAuth login.")]
    public static async Task<string> ListAccountNumbers(
        SchwabApiClient api,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.GetAccountNumbersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SchwabOAuthException ex)
        {
            return $"Auth error: {ex.Message}";
        }
        catch (SchwabApiException ex)
        {
            return $"API error: {ex.Message}";
        }
    }
}
