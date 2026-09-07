namespace PIT.Boletas.Domain.Entities;

public sealed class OcrAttempt
{
    public string CorrelationId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OcrEngine { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public int? PageCount { get; set; }
    public DateTime RequestedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public bool Success { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}