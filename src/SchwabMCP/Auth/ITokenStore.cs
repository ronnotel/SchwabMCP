namespace SchwabMCP.Auth;

/// <summary>
/// Persists OAuth tokens outside source control (user profile / secret store).
/// Implementations must never log raw token values.
/// </summary>
public interface ITokenStore
{
    /// <summary>Load the current token set, or null if none is stored.</summary>
    Task<SchwabTokenSet?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Persist tokens (overwrites any previous set).</summary>
    Task SaveAsync(SchwabTokenSet tokens, CancellationToken cancellationToken = default);

    /// <summary>Remove stored tokens (logout / invalid_grant recovery).</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Human-readable location for status/diagnostics (no secret material).</summary>
    string Description { get; }
}
