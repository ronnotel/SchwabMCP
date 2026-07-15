namespace SchwabMCP.Api;

public sealed class SchwabApiException : Exception
{
    public SchwabApiException(string message)
        : base(message)
    {
    }

    public SchwabApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int? StatusCode { get; init; }

    public string? ResponseBody { get; init; }
}
