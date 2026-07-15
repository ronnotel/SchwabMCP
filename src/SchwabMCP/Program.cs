using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SchwabMCP.Auth;
using SchwabMCP.Configuration;
using SchwabMCP.Hosting;

namespace SchwabMCP;

public static class Program
{
    private static readonly HashSet<string> CliCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "login", "logout", "refresh", "creds", "help", "-h", "--help",
    };

    private static readonly HashSet<string> McpCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "mcp", "serve",
    };

    public static async Task<int> Main(string[] args)
    {
        var (command, hostArgs) = ParseArgs(args);

        if (command is "help" or "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        // Default with no verb: MCP stdio server (SuperGrok / Claude Desktop spawn this process).
        // Explicit CLI verbs: status, login, logout, refresh, creds.
        if (McpCommands.Contains(command) || string.IsNullOrEmpty(command))
        {
            return await RunMcpServerAsync(hostArgs).ConfigureAwait(false);
        }

        return await RunCliAsync(command, hostArgs).ConfigureAwait(false);
    }

    private static async Task<int> RunMcpServerAsync(string[] hostArgs)
    {
        var builder = Host.CreateApplicationBuilder(hostArgs);

        // Never write logs to stdout — that breaks the MCP JSON-RPC framing.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
        builder.Services.AddSchwabAuth(builder.Configuration);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "SchwabMCP",
                    Version = "0.1.0",
                };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        try
        {
            await builder.Build().RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (OptionsValidationException ex)
        {
            // stderr only
            Console.Error.WriteLine("Schwab configuration is incomplete or invalid:");
            foreach (var failure in ex.Failures)
            {
                Console.Error.WriteLine($"  - {failure}");
            }

            Console.Error.WriteLine("See docs/authentication.md");
            return 1;
        }
    }

    private static async Task<int> RunCliAsync(string command, string[] hostArgs)
    {
        var builder = Host.CreateApplicationBuilder(hostArgs);
        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
        builder.Services.AddSchwabAuth(builder.Configuration);

        try
        {
            using var host = builder.Build();
            return command.ToLowerInvariant() switch
            {
                "status" => await RunStatusAsync(host.Services).ConfigureAwait(false),
                "login" => await RunLoginAsync(host.Services, hostArgs).ConfigureAwait(false),
                "logout" => await RunLogoutAsync(host.Services).ConfigureAwait(false),
                "refresh" => await RunRefreshAsync(host.Services).ConfigureAwait(false),
                "creds" => RunCreds(host.Services),
                _ => UnknownCommand(command),
            };
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
        catch (SchwabOAuthException ex)
        {
            Console.Error.WriteLine($"OAuth error: {ex.Message}");
            if (ex.StatusCode is not null)
            {
                Console.Error.WriteLine($"  HTTP status: {ex.StatusCode}");
            }

            return 2;
        }
    }

    private static async Task<int> RunStatusAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<SchwabOptions>>().Value;
        var tokens = services.GetRequiredService<TokenProvider>();
        var store = services.GetRequiredService<ITokenStore>();
        var current = await tokens.GetAsync().ConfigureAwait(false);

        Console.WriteLine("SchwabMCP configuration OK (secrets not printed).");
        Console.WriteLine($"  AppKey:        {RedactPresence(options.AppKey)}");
        Console.WriteLine($"  AppSecret:     {RedactPresence(options.AppSecret)}");
        Console.WriteLine($"  CallbackUrl:   {options.CallbackUrl}");
        Console.WriteLine($"  Token store:   {store.Description}");
        Console.WriteLine($"  Env refresh:   {RedactPresence(options.RefreshToken)}");
        Console.WriteLine(
            $"  Tokens:        {(current is null ? "none" : current.ToRedactedString())}");

        if (current is null || !current.HasRefreshToken)
        {
            Console.WriteLine();
            Console.WriteLine("Not logged in. Run:");
            Console.WriteLine("  dotnet run --project src/SchwabMCP -- login");
        }
        else if (!current.IsAccessTokenValid())
        {
            Console.WriteLine();
            Console.WriteLine("Access token missing/expired; refresh will run automatically on API use, or:");
            Console.WriteLine("  dotnet run --project src/SchwabMCP -- refresh");
        }

        Console.WriteLine();
        Console.WriteLine("MCP tools: auth_status, list_accounts, list_account_numbers, get_quotes");
        Console.WriteLine("Start MCP host:  dotnet run --project src/SchwabMCP -- mcp");
        Console.WriteLine("  (also the default when no command is passed)");
        return 0;
    }

    private static async Task<int> RunLoginAsync(IServiceProvider services, string[] hostArgs)
    {
        var pasteOnly = hostArgs.Any(a =>
            string.Equals(a, "--paste", StringComparison.OrdinalIgnoreCase));
        var forceListen = hostArgs.Any(a =>
            string.Equals(a, "--listen", StringComparison.OrdinalIgnoreCase));
        var timeout = TimeSpan.FromMinutes(5);
        for (var i = 0; i < hostArgs.Length; i++)
        {
            if (string.Equals(hostArgs[i], "--timeout", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < hostArgs.Length &&
                int.TryParse(hostArgs[i + 1], out var seconds) &&
                seconds > 0)
            {
                timeout = TimeSpan.FromSeconds(seconds);
            }
        }

        var oauth = services.GetRequiredService<SchwabOAuthService>();
        var options = services.GetRequiredService<IOptions<SchwabOptions>>().Value;
        var store = services.GetRequiredService<ITokenStore>();

        bool? preferListener = null;
        if (pasteOnly)
        {
            preferListener = false;
        }
        else if (forceListen)
        {
            preferListener = true;
        }

        Console.WriteLine("Schwab OAuth login");
        Console.WriteLine($"  Callback:     {options.CallbackUrl}");
        Console.WriteLine($"  Token store:  {store.Description}");
        Console.WriteLine($"  Timeout:      {timeout.TotalSeconds:0}s");
        Console.WriteLine();
        Console.WriteLine("NOTE: After Schwab redirects to 127.0.0.1, the browser page will usually");
        Console.WriteLine("FAIL to load (connection reset). That is OK — copy the address-bar URL.");
        Console.WriteLine();

        var tokens = await oauth.LoginInteractiveAsync(
                timeout,
                openBrowser: true,
                preferListener: preferListener)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Login successful (tokens stored; values not printed).");
        Console.WriteLine($"  {tokens.ToRedactedString()}");
        Console.WriteLine($"  Store: {store.Description}");
        return 0;
    }

    private static async Task<int> RunLogoutAsync(IServiceProvider services)
    {
        var oauth = services.GetRequiredService<SchwabOAuthService>();
        var store = services.GetRequiredService<ITokenStore>();
        await oauth.LogoutAsync().ConfigureAwait(false);
        Console.WriteLine("Logged out. Cleared local token store:");
        Console.WriteLine($"  {store.Description}");
        Console.WriteLine("Revoke app access in the Schwab portal if the device was compromised.");
        return 0;
    }

    private static async Task<int> RunRefreshAsync(IServiceProvider services)
    {
        var oauth = services.GetRequiredService<SchwabOAuthService>();
        var tokens = await oauth.RefreshStoredAsync().ConfigureAwait(false);
        Console.WriteLine("Token refresh successful (values not printed).");
        Console.WriteLine($"  {tokens.ToRedactedString()}");
        return 0;
    }

    private static int RunCreds(IServiceProvider services)
    {
        var client = services.GetRequiredService<SchwabOAuthClient>();
        var options = services.GetRequiredService<IOptions<SchwabOptions>>().Value;
        Console.WriteLine("Loaded Schwab client credentials (fingerprints only):");
        Console.WriteLine($"  {client.DescribeClientCredentials()}");
        Console.WriteLine($"  CallbackUrl: {options.CallbackUrl}");
        Console.WriteLine();
        Console.WriteLine("Compare AppKey prefix/length to Developer Portal → App Details.");
        Console.WriteLine("Do not use: dotnet user-secrets list  (it prints full secrets).");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            SchwabMCP — Schwab OAuth CLI + MCP stdio server

            Usage:
              dotnet run --project src/SchwabMCP -- [command] [options]

            Commands:
              mcp        Start MCP stdio server (default when no command)
              serve      Alias for mcp
              status     Show config + token presence
              login      Browser OAuth; save refresh/access tokens locally
              refresh    Refresh access token using stored refresh token
              logout     Delete local token store
              creds      Show AppKey/AppSecret fingerprints only (no secret values)
              help       Show this help

            MCP tools (when host is running):
              auth_status, list_accounts, list_account_numbers, get_quotes

            login options:
              --paste              Force paste mode (default for https:// callbacks)
              --listen             Try local HttpListener (needs TLS cert for https)
              --timeout <seconds>  Listener wait time (default 300)

            SuperGrok: see docs/mcp.md

            Secrets (never commit):
              dotnet user-secrets set "Schwab:AppKey" "..." --project src/SchwabMCP
              dotnet user-secrets set "Schwab:AppSecret" "..." --project src/SchwabMCP

            Docs: docs/authentication.md
            """);
    }

    private static (string Command, string[] HostArgs) ParseArgs(string[] args)
    {
        if (args.Length == 0)
        {
            // Empty command → MCP server (stdio hosts spawn with no subcommand).
            return ("", Array.Empty<string>());
        }

        if (CliCommands.Contains(args[0]) || McpCommands.Contains(args[0]))
        {
            return (args[0], args.Skip(1).ToArray());
        }

        if (args[0].StartsWith('-'))
        {
            // Flags only → MCP, pass through for host config.
            return ("", args);
        }

        return (args[0], args.Skip(1).ToArray());
    }

    private static string RedactPresence(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : "set";
}
