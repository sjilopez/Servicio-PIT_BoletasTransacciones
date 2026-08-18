namespace PIT.Boletas.Domain.Enums;

public enum PipelineStage
{
    In,
    Validate,
    Ocr,
    ErrorOcr,
    DbPending,
    Compress,
    CopyAzureFiles,
    CopyAzureBlob,
    LocalBackup
}
