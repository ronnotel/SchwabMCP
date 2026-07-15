namespace SchwabMCP.Auth;

/// <summary>In-process token store for tests and ephemeral sessions.</summary>
public sealed class MemoryTokenStore : ITokenStore
{
    private readonly object _gate = new();
    private SchwabTokenSet? _tokens;

    public string Description => "memory (ephemeral)";

    public Task<SchwabTokenSet?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_tokens);
        }
    }

    public Task SaveAsync(SchwabTokenSet tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _tokens = tokens;
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _tokens = null;
        }

        return Task.CompletedTask;
    }
}
