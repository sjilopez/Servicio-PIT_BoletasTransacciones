namespace PIT.Boletas.Application.Configuration;

public sealed class PipelineFoldersOptions
{
    public const string SectionName = "PipelineFolders";

    public string BasePath { get; set; } = @"C:\Scans";

    public string OcrTextOutputPath { get; set; } = @"C:\Scans\OCR";

    public string ToolsPath { get; set; } = @"C:\Scans\Tools";

    public string LocalSettingsFileName { get; set; } = "settings.local.json";

    public string ProgramDataConfigPath { get; set; } = @"C:\ProgramData\PIT-BoletasTransaccionales\Config";

    public List<string> StageFolders { get; set; } =
    [
        "1_IN",
        "2_VALIDATE",
        "3_OCR",
        "4_ERROR_OCR",
        "5_DB_PENDING",
        "6_COMPRESS",
        "7_COPY_AZURE_FILES",
        "8_COPY_AZURE_BLOB",
        "9_LOCAL_BACKUP"
    ];
}
