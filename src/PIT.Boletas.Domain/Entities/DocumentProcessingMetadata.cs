namespace PIT.Boletas.Domain.Entities;

public sealed class DocumentProcessingMetadata
{
    public string FileName { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string Agency { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public string HostIp { get; set; } = string.Empty;

    public DateTime OriginalCreationTimeLocal { get; set; }

    public DateTime IngestedUtc { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public double ClassificationConfidence { get; set; }

    public string OcrRoute { get; set; } = string.Empty;

    public bool RequiresAzureBlob { get; set; }

    public bool ApiOcrSucceeded { get; set; }

    public bool AzureFilesUploaded { get; set; }

    public bool AzureBlobUploaded { get; set; }

    public DateTime? LastApiAttemptUtc { get; set; }

    public DateTime? LastDbAttemptUtc { get; set; }

    public int? ExternalOcrLastStatusCode { get; set; }

    public string ExternalOcrLastError { get; set; } = string.Empty;

    public DateTime? LastAzureFilesAttemptUtc { get; set; }

    public DateTime? LastAzureBlobAttemptUtc { get; set; }
}
