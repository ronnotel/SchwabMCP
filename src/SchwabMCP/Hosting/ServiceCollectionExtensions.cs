using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchwabMCP.Auth;
using SchwabMCP.Configuration;

namespace SchwabMCP.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Schwab options (bound + validated on start), file token store, and <see cref="TokenProvider"/>.
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

        return services;
    }
}
