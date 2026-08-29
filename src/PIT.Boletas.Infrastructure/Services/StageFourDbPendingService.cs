using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageFourDbPendingService(
    ILogger<StageFourDbPendingService> logger,
    IOptions<PipelineFoldersOptions> folderOptions,
    IConfiguration configuration,
    IOcrResultRepository repository) : IStageFourDbPendingService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string pendingPath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.DbPending);
        if (!Directory.Exists(pendingPath))
        {
            return 0;
        }

        int done = 0;
        int retryMinutes = Math.Max(1, configuration.GetValue("MySql:RetryMinutes", 3));
        string azureFilePath = PipelinePathResolver.StagePath(_folders.BasePath, PipelineStageNames.AzureFile);
        foreach (string jsonPath in Directory.GetFiles(pendingPath, "*.json", SearchOption.TopDirectoryOnly)
                 .Where(path => !path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string payload = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            string fileName = Path.GetFileNameWithoutExtension(jsonPath) + ".pdf";
            string pdfPath = Path.Combine(pendingPath, fileName);
            string metadataPath = MetadataSidecarStore.GetMetadataPath(pdfPath);
            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreateFromPath(jsonPath, metadataPath);
            metadata.FileName = fileName;
            if (string.IsNullOrWhiteSpace(metadata.SourceFileName))
            {
                metadata.SourceFileName = fileName;
            }

            if (!File.Exists(pdfPath))
            {
                logger.LogError("DB pending payload has no matching PDF: {FileName}", fileName);
                continue;
            }

            if (!RetryWindowEvaluator.ShouldRetry(metadata.LastDbAttemptUtc, retryMinutes))
            {
                continue;
            }

            metadata.LastDbAttemptUtc = DateTime.UtcNow;
            MetadataSidecarStore.Save(pdfPath, metadata);

            bool inserted = await repository.TryInsertOcrJsonAsync(metadata, payload, PipelineStageNames.DbPending, cancellationToken);
            if (!inserted)
            {
                continue;
            }

            string destination = Path.Combine(azureFilePath, fileName);
            MetadataSidecarStore.MoveWithMetadata(pdfPath, destination);
            MetadataSidecarStore.Save(destination, metadata);

            File.Delete(jsonPath);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
            done++;
        }

        if (done > 0)
        {
            logger.LogInformation("Stage4 DB pending resolved JSON files: {Count}", done);
        }

        return done;
    }
}
