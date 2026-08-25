using System.Text;
using System.Text.Json;
using FuzzySharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Application.Models;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageTwoValidationService(
    ILogger<StageTwoValidationService> logger,
    IOptions<PipelineFoldersOptions> folderOptions,
    ILocalOcrService localOcrService,
    IOperationalEventService operationalEventService,
    IConfiguration configuration) : IStageTwoValidationService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string sourcePath = PipelinePathResolver.StagePath(_folders.BasePath, "2_VALIDATE");
        string successPath = PipelinePathResolver.StagePath(_folders.BasePath, "3_OCR");
        string errorPath = PipelinePathResolver.StagePath(_folders.BasePath, "4_ERROR_OCR");
        string bypassPath = PipelinePathResolver.StagePath(_folders.BasePath, "6_COMPRESS");

        if (!Directory.Exists(sourcePath))
        {
            return 0;
        }

        TemplateSettings templates = LoadTemplateSettings();
        int fuzzyMatch = Math.Clamp(configuration.GetValue("Validation:FuzzyMatch", 85), 1, 100);
        int minimumMatches = Math.Max(1, configuration.GetValue("Validation:MinimumMatches", 3));

        int moved = 0;
        foreach (string pdfPath in Directory.GetFiles(sourcePath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string txtOutput = Path.Combine(_folders.OcrTextOutputPath, Path.GetFileNameWithoutExtension(pdfPath) + ".txt");
            string text;
            try
            {
                text = await localOcrService.ExtractTextFromPdfAsync(pdfPath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Local OCR failed for {FileName}. Document will be routed to 4_ERROR_OCR.", Path.GetFileName(pdfPath));
                await operationalEventService.TrackAsync(
                    "error",
                    "operational",
                    "OCRL001",
                    "Fallo en OCR local",
                    ex.Message,
                    "2_VALIDATE",
                    MetadataSidecarStore.LoadOrCreate(pdfPath),
                    null,
                    cancellationToken);

                if (File.Exists(txtOutput))
                {
                    File.Delete(txtOutput);
                }

                string errorDestination = Path.Combine(errorPath, Path.GetFileName(pdfPath));
                MetadataSidecarStore.MoveWithMetadata(pdfPath, errorDestination);
                moved++;
                continue;
            }

            File.WriteAllText(txtOutput, text, Encoding.UTF8);

            int matched = 0;
            DocumentTemplate template = templates.Templates.FirstOrDefault()
                                        ?? new DocumentTemplate { Name = "BoletaTransaccional" };

            foreach (string phrase in template.Phrases)
            {
                int score = Fuzz.PartialRatio(Normalize(phrase), Normalize(text));
                if (score >= fuzzyMatch)
                {
                    matched++;
                }
            }

            bool isValid = matched >= minimumMatches;
            string destinationFolder = isValid ? successPath : bypassPath;
            string destination = Path.Combine(destinationFolder, Path.GetFileName(pdfPath));

            MetadataSidecarStore.MoveWithMetadata(pdfPath, destination);
            moved++;

            logger.LogInformation(
                "Stage2 validate | File: {FileName} | Matches: {Matched}/{Total} | Threshold: {Threshold} | MinMatches: {MinMatches} | Result: {Result}",
                Path.GetFileName(destination),
                matched,
                template.Phrases.Count,
                fuzzyMatch,
                minimumMatches,
                isValid ? "TRUE->3_OCR" : "FALSE->6_COMPRESS");
        }

        return moved;
    }

    private TemplateSettings LoadTemplateSettings()
    {
        string path = Path.Combine(_folders.ProgramDataConfigPath, "PlantillasDocumentales.json");
        if (!File.Exists(path))
        {
            return new TemplateSettings();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TemplateSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new TemplateSettings();
        }
        catch
        {
            return new TemplateSettings();
        }
    }

    private static string Normalize(string value)
    {
        return value
            .ToUpperInvariant()
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("  ", " ")
            .Trim();
    }
}
