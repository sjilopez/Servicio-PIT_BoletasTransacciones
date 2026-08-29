namespace PIT.Boletas.Application.Configuration;

public sealed class PipelineFoldersOptions
{
    public const string SectionName = "PipelineFolders";

    public string BasePath { get; set; } = @"C:\Scans";

    public string LocalSettingsFileName { get; set; } = "appsettings.local.json";

    public string ProgramDataConfigPath { get; set; } = @"C:\ProgramData\PIT-BoletasTransaccionales\Config";

    public List<string> StageFolders { get; set; } = [.. PipelineStageNames.All];
}
