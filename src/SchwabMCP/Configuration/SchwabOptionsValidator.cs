using Microsoft.Extensions.Options;

namespace SchwabMCP.Configuration;

/// <summary>Fail-closed validation for required Schwab app credentials.</summary>
public sealed class SchwabOptionsValidator : IValidateOptions<SchwabOptions>
{
    public ValidateOptionsResult Validate(string? name, SchwabOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AppKey))
        {
            errors.Add(
                "Schwab:AppKey is required. Set env Schwab__AppKey, use " +
                "dotnet user-secrets, or see docs/authentication.md.");
        }

        if (string.IsNullOrWhiteSpace(options.AppSecret))
        {
            errors.Add(
                "Schwab:AppSecret is required. Set env Schwab__AppSecret, use " +
                "dotnet user-secrets, or see docs/authentication.md.");
        }

        if (string.IsNullOrWhiteSpace(options.CallbackUrl))
        {
            errors.Add("Schwab:CallbackUrl is required (registered OAuth redirect URI).");
        }
        else if (!Uri.TryCreate(options.CallbackUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("Schwab:CallbackUrl must be an absolute http(s) URI.");
        }

        if (!string.IsNullOrWhiteSpace(options.TokenStorePath))
        {
            try
            {
                _ = Path.GetFullPath(options.TokenStorePath);
            }
            catch (Exception ex)
            {
                errors.Add($"Schwab:TokenStorePath is not a valid path: {ex.Message}");
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
