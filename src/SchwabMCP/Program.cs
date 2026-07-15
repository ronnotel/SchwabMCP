using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchwabMCP.Auth;
using SchwabMCP.Configuration;
using SchwabMCP.Hosting;

namespace SchwabMCP;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Always allow user-secrets for local dev (not only when Environment=Development).
        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);

        // Host defaults already load appsettings.json and environment variables (Schwab__*).
        builder.Services.AddSchwabAuth(builder.Configuration);

        try
        {
            using var host = builder.Build();

            // Force options validation (ValidateOnStart) and surface a clear status line.
            var options = host.Services.GetRequiredService<IOptions<SchwabOptions>>().Value;
            var tokens = host.Services.GetRequiredService<TokenProvider>();
            var store = host.Services.GetRequiredService<ITokenStore>();
            var current = await tokens.GetAsync().ConfigureAwait(false);

            Console.WriteLine("SchwabMCP configuration OK (secrets not printed).");
            Console.WriteLine($"  AppKey:        {RedactPresence(options.AppKey)}");
            Console.WriteLine($"  AppSecret:     {RedactPresence(options.AppSecret)}");
            Console.WriteLine($"  CallbackUrl:   {options.CallbackUrl}");
            Console.WriteLine($"  Token store:   {store.Description}");
            Console.WriteLine(
                $"  Env refresh:   {RedactPresence(options.RefreshToken)}");
            Console.WriteLine(
                $"  Tokens:        {(current is null ? "none" : current.ToRedactedString())}");
            Console.WriteLine();
            Console.WriteLine("Next: MCP host + Schwab OAuth client (see docs/authentication.md).");
            return 0;
        }
        catch (OptionsValidationException ex)
        {
            Console.Error.WriteLine("Schwab configuration is incomplete or invalid:");
            foreach (var failure in ex.Failures)
            {
                Console.Error.WriteLine($"  - {failure}");
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("See docs/authentication.md for how to supply secrets safely.");
            return 1;
        }
    }

    private static string RedactPresence(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : "set";
}
