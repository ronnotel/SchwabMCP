using Microsoft.Extensions.Options;
using SchwabMCP.Configuration;

namespace SchwabMCP.Auth;

/// <summary>
/// Resolves the current token set: persisted store first, then optional env refresh token.
/// Does not log raw secrets.
/// </summary>
public sealed class TokenProvider
{
    private readonly ITokenStore _store;
    private readonly SchwabOptions _options;

    public TokenProvider(ITokenStore store, IOptions<SchwabOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public ITokenStore Store => _store;

    /// <summary>
    /// Returns tokens from the store, or a refresh-only set from configuration if the store is empty.
    /// </summary>
    public async Task<SchwabTokenSet?> GetAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return stored;
        }

        if (!string.IsNullOrWhiteSpace(_options.RefreshToken))
        {
            return new SchwabTokenSet(
                AccessToken: "",
                RefreshToken: _options.RefreshToken.Trim(),
                AccessTokenExpiresAt: null);
        }

        return null;
    }

    public Task SaveAsync(SchwabTokenSet tokens, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(tokens, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        _store.ClearAsync(cancellationToken);
}
