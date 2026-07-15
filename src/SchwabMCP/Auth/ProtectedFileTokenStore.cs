using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SchwabMCP.Auth;

/// <summary>
/// File-backed token store outside the repo.
/// Windows: DPAPI (CurrentUser). Other OS: UTF-8 JSON with owner-only file mode when supported.
/// </summary>
public sealed class ProtectedFileTokenStore : ITokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("SchwabMCP.TokenStore.v1");

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedFileTokenStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public string Description =>
        OperatingSystem.IsWindows()
            ? $"DPAPI file: {_path}"
            : $"file: {_path}";

    /// <summary>
    /// Default path under LocalApplicationData/SchwabMCP
    /// (tokens.dpapi on Windows, tokens.json elsewhere).
    /// </summary>
    public static string GetDefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SchwabMCP");

        var fileName = OperatingSystem.IsWindows() ? "tokens.dpapi" : "tokens.json";
        return Path.Combine(dir, fileName);
    }

    public async Task<SchwabTokenSet?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return null;
            }

            var plain = Unprotect(bytes);
            var dto = JsonSerializer.Deserialize<TokenFileDto>(plain, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                return null;
            }

            return new SchwabTokenSet(
                AccessToken: dto.AccessToken ?? "",
                RefreshToken: dto.RefreshToken,
                AccessTokenExpiresAt: dto.AccessTokenExpiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SchwabTokenSet tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (!tokens.HasRefreshToken)
        {
            throw new ArgumentException("RefreshToken is required to persist a token set.", nameof(tokens));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dto = new TokenFileDto
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
                SavedAtUtc = DateTimeOffset.UtcNow,
            };

            var plain = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            var payload = Protect(plain);

            var tempPath = _path + ".tmp";
            await File.WriteAllBytesAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);
            RestrictPermissions(tempPath);
            File.Move(tempPath, _path, overwrite: true);
            RestrictPermissions(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            var tempPath = _path + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] Protect(byte[] plain)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(plain, OptionalEntropy, DataProtectionScope.CurrentUser);
        }

        return plain;
    }

    private static byte[] Unprotect(byte[] payload)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(payload, OptionalEntropy, DataProtectionScope.CurrentUser);
        }

        return payload;
    }

    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Best-effort on platforms/filesystems that reject chmod-style modes.
        }
    }

    private sealed class TokenFileDto
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset? AccessTokenExpiresAt { get; set; }
        public DateTimeOffset? SavedAtUtc { get; set; }
    }
}
