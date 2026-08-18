using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageSevenAzureBlobService(
    ILogger<StageSevenAzureBlobService> logger,
    IOperationalEventService operationalEventService,
    IOptions<PipelineFoldersOptions> folderOptions,
    IConfiguration configuration) : IStageSevenAzureBlobService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string sourcePath = PipelinePathResolver.StagePath(_folders.BasePath, "8_COPY_AZURE_BLOB");
        string backupPath = PipelinePathResolver.StagePath(_folders.BasePath, "9_LOCAL_BACKUP");

        if (!Directory.Exists(sourcePath))
        {
            return 0;
        }

        string connectionString = configuration.GetValue<string>("AzureBlob:ConnectionString") ?? string.Empty;
        string containerName = configuration.GetValue<string>("AzureBlob:ContainerName") ?? string.Empty;
        int retryMinutes = Math.Max(1, configuration.GetValue("RetryMinutes", 10));

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(containerName))
        {
            logger.LogWarning("Azure Blob not configured. Files remain in 8_COPY_AZURE_BLOB.");
            return 0;
        }

        BlobContainerClient container = new(connectionString, containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        int moved = 0;
        foreach (string pdfPath in Directory.GetFiles(sourcePath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);
            if (!RetryWindowEvaluator.ShouldRetry(metadata.LastAzureBlobAttemptUtc, retryMinutes))
            {
                continue;
            }

            metadata.LastAzureBlobAttemptUtc = DateTime.UtcNow;
            MetadataSidecarStore.Save(pdfPath, metadata);

            try
            {
                DateTime basis = metadata.OriginalCreationTimeLocal == default
                    ? File.GetCreationTime(pdfPath)
                    : metadata.OriginalCreationTimeLocal;

                string blobPath = $"{basis:yyyy/MM/dd/HH}/{Path.GetFileName(pdfPath)}";
                BlobClient blob = container.GetBlobClient(blobPath);
                await blob.UploadAsync(pdfPath, overwrite: true, cancellationToken);

                metadata.AzureBlobUploaded = true;

                string destination = Path.Combine(backupPath, Path.GetFileName(pdfPath));
                MetadataSidecarStore.MoveWithMetadata(pdfPath, destination);
                MetadataSidecarStore.Save(destination, metadata);
                moved++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Azure Blob upload failed for {FileName}", Path.GetFileName(pdfPath));
                await operationalEventService.TrackAsync(
                    "error",
                    "transient",
                    "AZB001",
                    "Fallo de copia Azure Blob",
                    ex.Message,
                    "8_COPY_AZURE_BLOB",
                    metadata,
                    null,
                    cancellationToken);
            }
        }

        return moved;
    }
}
