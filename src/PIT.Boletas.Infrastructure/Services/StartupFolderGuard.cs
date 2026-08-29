using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Application.Models;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StartupFolderGuard(
    IOptions<PipelineFoldersOptions> options,
    IOptions<LocalOcrOptions> localOcrOptions,
    ILogger<StartupFolderGuard> logger) : IStartupFolderGuard
{
    private readonly PipelineFoldersOptions _options = options.Value;
    private readonly LocalOcrOptions _localOcr = localOcrOptions.Value;

    public Task<FolderValidationReport> ValidateAndEnsureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FolderValidationReport report = new();

        EnsureDirectory(_options.BasePath, "FS_BASE", report, true);

        if (!Directory.Exists(_options.BasePath))
        {
            report.Add(
                "FS_BASE_MISSING",
                FolderIssueSeverity.Critical,
                "Base path is not accessible after creation attempt.",
                _options.BasePath);
            return Task.FromResult(report);
        }

        ValidateStageFolders(report);
        EnsureDirectory(_options.ProgramDataConfigPath, "FS_PROGRAMDATA", report, true);
        EnsureOperationalSettingsTemplate(report);
        EnsureTemplateSettings(report);
        EnsureLocalOcrAssets(report);

        logger.LogInformation("Folder validation executed. Critical issues: {HasCritical}", report.HasCriticalIssues);
        return Task.FromResult(report);
    }

    private void ValidateStageFolders(FolderValidationReport report)
    {
        string[] existingDirectories = Directory.GetDirectories(_options.BasePath);
        Dictionary<string, string> existingByNameIgnoreCase = existingDirectories
            .Select(path => new DirectoryInfo(path))
            .ToDictionary(
                dir => dir.Name,
                dir => dir.FullName,
                StringComparer.OrdinalIgnoreCase);

        foreach (string folderName in _options.StageFolders)
        {
            string expectedPath = Path.Combine(_options.BasePath, folderName);

            if (!existingByNameIgnoreCase.TryGetValue(folderName, out string? foundPath))
            {
                EnsureDirectory(expectedPath, "FS_STAGE_CREATE", report, true);
                continue;
            }

            if (!string.Equals(Path.GetFileName(foundPath), folderName, StringComparison.Ordinal))
            {
                report.Add(
                    "FS_STAGE_CASE",
                    FolderIssueSeverity.Warning,
                    "Folder exists with different casing than expected. No rename was performed.",
                    foundPath);
            }

            if (File.Exists(foundPath))
            {
                report.Add(
                    "FS_STAGE_FILE",
                    FolderIssueSeverity.Critical,
                    "Expected folder path exists as a file.",
                    foundPath);
            }
        }
    }

    private void EnsureOperationalSettingsTemplate(FolderValidationReport report)
    {
        string settingsPath = Path.Combine(_options.ProgramDataConfigPath, _options.LocalSettingsFileName);

        if (File.Exists(settingsPath))
        {
            report.Add(
                "FS_SETTINGS_EXISTS",
                FolderIssueSeverity.Info,
                "Local settings file is present.",
                settingsPath);
            return;
        }

        const string template = """
{
    "RetryMinutes": 10,
  "Validation": {
    "FuzzyMatch": 85,
    "MinimumMatches": 3
  },
    "Ingestion": {
        "PollIntervalSeconds": 10,
        "StabilizationChecks": 3,
        "StabilizationDelayMilliseconds": 500
    },
  "RetentionDays": 7,
    "MySql": {
        "ConnectionString": "",
        "RetryMinutes": 3
    },
    "AzureFiles": {
        "ConnectionString": "",
        "ShareName": "boleta-transacciones"
    },
    "AzureBlob": {
        "ConnectionString": "",
        "ContainerName": "boleta-transacciones"
    },
  "Alerts": {
        "Enabled": true,
        "ImmediateOnError": true,
    "TeamsWebhook": "",
    "SmtpHost": "",
    "SmtpPort": 587,
        "UseSsl": true,
        "SmtpUser": "",
        "SmtpPassword": "",
    "From": "",
    "To": ""
  },
    "Monitoring": {
        "HeartbeatSeconds": 60
    },
  "ExternalOcr": {
        "Endpoint": "http://172.179.9.62:8001/api/v1/ocr",
    "ApiKey": ""
    },
    "LocalOcr": {
        "Enabled": true,
        "Language": "spa",
        "TessDataPath": "ocr/tessdata",
                "RenderWidth": 2480,
                "RenderHeight": 3508,
                "MinTextLength": 20,
                "EngineMode": "LstmOnly",
                "PageSegMode": "Auto",
                "UserDefinedDpi": 300,
                "EnableImagePreprocessing": true,
                "ContrastBoost": 1.35,
                "BinarizationThreshold": 160
  }
}
""";

        try
        {
            File.WriteAllText(settingsPath, template);
            report.Add(
                "FS_SETTINGS_CREATE",
                FolderIssueSeverity.Warning,
                "Local settings file did not exist and was created with placeholders.",
                settingsPath);
        }
        catch (Exception ex)
        {
            report.Add(
                "FS_SETTINGS_ERROR",
                FolderIssueSeverity.Critical,
                $"Unable to create local settings file. {ex.Message}",
                settingsPath);
        }
    }

        private void EnsureTemplateSettings(FolderValidationReport report)
        {
                string templatesPath = Path.Combine(_options.ProgramDataConfigPath, "PlantillasDocumentales.json");
                if (File.Exists(templatesPath))
                {
                        report.Add(
                                "FS_TEMPLATES_EXISTS",
                                FolderIssueSeverity.Info,
                                "Template settings file is present.",
                                templatesPath);
                        return;
                }

                const string template = """
{
    "templates": [
        {
            "name": "BoletaTransaccional",
            "phrases": [
                "boleta de transacciones",
                "cooperativa de ahorro y credito integral san jose obrero, r.l.",
                "nit: 551823-7",
                "4a. avenida 9-01 zona 1, esquipulas, chiquimula",
                "autorizado segun",
                "formularios standard"
            ]
        }
    ]
}
""";

                try
                {
                        File.WriteAllText(templatesPath, template);
                        report.Add(
                                "FS_TEMPLATES_CREATE",
                                FolderIssueSeverity.Warning,
                                "Template settings file was created with default BoletaTransaccional phrases.",
                                templatesPath);
                }
                catch (Exception ex)
                {
                        report.Add(
                                "FS_TEMPLATES_ERROR",
                                FolderIssueSeverity.Critical,
                                $"Unable to create template settings file. {ex.Message}",
                                templatesPath);
                }
        }

    private static void EnsureDirectory(string path, string codePrefix, FolderValidationReport report, bool criticalIfFails)
    {
        try
        {
            if (Directory.Exists(path))
            {
                report.Add(
                    codePrefix + "_EXISTS",
                    FolderIssueSeverity.Info,
                    "Directory exists.",
                    path);
                return;
            }

            Directory.CreateDirectory(path);
            report.Add(
                codePrefix + "_CREATED",
                FolderIssueSeverity.Warning,
                "Directory was missing and has been created.",
                path);
        }
        catch (Exception ex)
        {
            report.Add(
                codePrefix + "_ERROR",
                criticalIfFails ? FolderIssueSeverity.Critical : FolderIssueSeverity.Warning,
                $"Unable to ensure directory. {ex.Message}",
                path);
        }
    }

    private void EnsureLocalOcrAssets(FolderValidationReport report)
    {
        if (!_localOcr.Enabled)
        {
            report.Add("FS_OCR_DISABLED", FolderIssueSeverity.Warning, "Local OCR is disabled by configuration.", _localOcr.TessDataPath);
            return;
        }

        string tessDataPath = Path.IsPathRooted(_localOcr.TessDataPath)
            ? _localOcr.TessDataPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _localOcr.TessDataPath));

        if (!Directory.Exists(tessDataPath))
        {
            report.Add(
                "FS_TESSDATA_MISSING",
                FolderIssueSeverity.Critical,
                "Tessdata directory was not found. OCR local cannot run.",
                tessDataPath);
            return;
        }

        string language = string.IsNullOrWhiteSpace(_localOcr.Language) ? "spa" : _localOcr.Language;
        string trainedData = Path.Combine(tessDataPath, language + ".traineddata");

        if (!File.Exists(trainedData))
        {
            report.Add(
                "FS_TRAINEDDATA_MISSING",
                FolderIssueSeverity.Critical,
                $"OCR language file {language}.traineddata is missing.",
                trainedData);
            return;
        }

        report.Add(
            "FS_TRAINEDDATA_OK",
            FolderIssueSeverity.Info,
            "OCR language file is present.",
            trainedData);
    }
}
