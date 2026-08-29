namespace PIT.Boletas.Application.Configuration;

public static class PipelineStageNames
{
    public const string Input = "1_IN";

    public const string Validate = "2_VALIDATE";

    public const string ExternalOcr = "4_OCR_EXTERNO";

    public const string OcrError = "5_ERROR_OCR";

    public const string DbPending = "6_DB_PENDING";

    public const string AzureFile = "7_COPY_AZURE_FILE";

    public const string Compress = "8_COMPRESS";

    public const string AzureBlob = "9_COPY_AZURE_BLOB";

    public const string LocalBackup = "10_LOCAL_BACKUP";

    public static IReadOnlyList<string> All { get; } =
    [
        Input,
        Validate,
        ExternalOcr,
        OcrError,
        DbPending,
        AzureFile,
        Compress,
        AzureBlob,
        LocalBackup
    ];
}