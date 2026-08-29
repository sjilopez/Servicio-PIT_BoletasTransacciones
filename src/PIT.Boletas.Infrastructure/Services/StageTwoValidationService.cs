using System.Text;
using System.Text.Json;
using FuzzySharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Application.Models;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageTwoValidationService(
    ILogger<StageTwoValidationService> logger,
    IOptions<PipelineFoldersOptions> folderOptions,
    ILocalOcrService localOcrService,
    IOperationalEventService operationalEventService,
    IOcrResultRepository repository,
    IConfiguration configuration) : IStageTwoValidationService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string sourcePath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.Validate);
        string successPath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.ExternalOcr);
        string errorPath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.OcrError);
        string bypassPath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.AzureFile);
        string dbPendingPath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.DbPending);

        if (!Directory.Exists(sourcePath))
        {
            return 0;
        }

        TemplateSettings templates = LoadTemplateSettings();
        int fuzzyMatch = Math.Clamp(configuration.GetValue("Validation:FuzzyMatch", 85), 1, 100);
        int headerFuzzyMatch = Math.Clamp(configuration.GetValue("Validation:HeaderFuzzyMatch", 72), 1, 100);
        int minimumMatches = Math.Max(1, configuration.GetValue("Validation:MinimumMatches", 3));
        string requiredHeader = configuration.GetValue<string>("Validation:RequiredHeader") ?? "BOLETA DE TRANSACCIONES";

        int moved = 0;
        foreach (string pdfPath in Directory.GetFiles(sourcePath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text;
            try
            {
                text = await localOcrService.ExtractTextFromPdfAsync(pdfPath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Local OCR failed for {FileName}. Document will be routed to {Stage}.", Path.GetFileName(pdfPath), PipelineStageNames.OcrError);
                await operationalEventService.TrackAsync(
                    "error",
                    "operational",
                    "OCRL001",
                    "Fallo en OCR local",
                    ex.Message,
                    PipelineStageNames.Validate,
                    MetadataSidecarStore.LoadOrCreate(pdfPath),
                    null,
                    cancellationToken);

                string errorDestination = Path.Combine(errorPath, Path.GetFileName(pdfPath));
                MetadataSidecarStore.MoveWithMetadata(pdfPath, errorDestination);
                moved++;
                continue;
            }

            DocumentTemplate template = templates.Templates.FirstOrDefault()
                                        ?? new DocumentTemplate { Name = "BoletaTransaccional" };
            string normalizedText = Normalize(text);
            string normalizedHeader = Normalize(requiredHeader);
            int headerScore = Fuzz.PartialRatio(normalizedHeader, normalizedText);
            bool headerMatched = normalizedText.Contains(normalizedHeader, StringComparison.OrdinalIgnoreCase)
                                 || headerScore >= headerFuzzyMatch;
            List<string> indicators = [.. template.Phrases, .. template.Keywords];
            int matched = indicators.Count == 0
                ? 0
                : indicators.Count(indicator => Fuzz.PartialRatio(Normalize(indicator), normalizedText) >= fuzzyMatch);

            bool isValid = headerMatched && (indicators.Count == 0 || matched >= minimumMatches);
            string documentType = isValid ? template.Name : string.Empty;
            double confidence = indicators.Count == 0
                ? headerScore / 100d
                : Math.Min(headerScore, matched * 100d / indicators.Count) / 100d;
            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);
            metadata.DocumentType = documentType;
            metadata.ClassificationConfidence = confidence;
            metadata.OcrRoute = isValid ? PipelineStageNames.ExternalOcr : PipelineStageNames.AzureFile;
            metadata.RequiresAzureBlob = isValid;

            if (!isValid)
            {
                metadata.LastDbAttemptUtc = DateTime.UtcNow;
                string payloadJson = JsonSerializer.Serialize(new
                {
                    ocrText = text,
                    documentType = metadata.DocumentType,
                    classificationConfidence = metadata.ClassificationConfidence,
                    ocrRoute = metadata.OcrRoute,
                    requiresAzureBlob = metadata.RequiresAzureBlob
                });

                bool dbInserted = await repository.TryInsertOcrJsonAsync(
                    metadata,
                    payloadJson,
                    PipelineStageNames.Validate,
                    cancellationToken);

                if (!dbInserted)
                {
                    string pendingPdf = PipelinePathResolver.BuildNonCollidingFilePath(
                        dbPendingPath,
                        Path.GetFileName(pdfPath));
                    string pendingJson = Path.Combine(
                        dbPendingPath,
                        Path.GetFileNameWithoutExtension(pendingPdf) + ".json");

                    MetadataSidecarStore.MoveWithMetadata(pdfPath, pendingPdf);
                    await File.WriteAllTextAsync(pendingJson, payloadJson, Encoding.UTF8, cancellationToken);
                    MetadataSidecarStore.Save(pendingPdf, metadata);
                    moved++;
                    continue;
                }
            }

            string destinationFolder = isValid ? successPath : bypassPath;
            string destination = Path.Combine(destinationFolder, Path.GetFileName(pdfPath));

            MetadataSidecarStore.MoveWithMetadata(pdfPath, destination);
            metadata.FileName = Path.GetFileName(destination);
            MetadataSidecarStore.Save(destination, metadata);
            moved++;

            logger.LogInformation(
                "Stage2 validate | File: {FileName} | Header: {HeaderScore}/{HeaderThreshold} | Matches: {Matched}/{Total} | Threshold: {Threshold} | MinMatches: {MinMatches} | Result: {Result}",
                Path.GetFileName(destination),
                headerScore,
                headerFuzzyMatch,
                matched,
                indicators.Count,
                fuzzyMatch,
                minimumMatches,
                isValid ? $"TRUE->{PipelineStageNames.ExternalOcr}" : $"FALSE->{PipelineStageNames.AzureFile}");
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
