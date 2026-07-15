using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchwabMCP.Api;
using SchwabMCP.Auth;
using SchwabMCP.Configuration;

namespace SchwabMCP.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Schwab options, token store, OAuth, and Trader API HTTP client.
    /// </summary>
    public static IServiceCollection AddSchwabAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<SchwabOptions>, SchwabOptionsValidator>();

        services.AddOptions<SchwabOptions>()
            .Bind(configuration.GetSection(SchwabOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<ITokenStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SchwabOptions>>().Value;
            var path = string.IsNullOrWhiteSpace(options.TokenStorePath)
                ? ProtectedFileTokenStore.GetDefaultPath()
                : options.TokenStorePath!;
            return new ProtectedFileTokenStore(path);
        });

        services.AddSingleton<TokenProvider>();

        services.AddHttpClient<SchwabOAuthClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SchwabMCP/0.1 (+https://github.com/ronnotel/SchwabMCP)");
        });

        services.AddSingleton<SchwabOAuthService>();

        services.AddHttpClient<SchwabApiClient>(client =>
        {
            client.BaseAddress = new Uri(SchwabApiClient.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SchwabMCP/0.1 (+https://github.com/ronnotel/SchwabMCP)");
        });

        return services;
    }
}
