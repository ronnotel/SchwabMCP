namespace SchwabMCP.Auth;

public sealed class SchwabOAuthException : Exception
{
    public SchwabOAuthException(string message)
        : base(message)
    {
    }

    public SchwabOAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? ErrorCode { get; init; }

    public int? StatusCode { get; init; }
}
