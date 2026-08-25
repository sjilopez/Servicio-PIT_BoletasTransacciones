using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageSixAzureFilesService(
    ILogger<StageSixAzureFilesService> logger,
    IOperationalEventService operationalEventService,
    IOptions<PipelineFoldersOptions> folderOptions,
    IConfiguration configuration) : IStageSixAzureFilesService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string sourcePath = PipelinePathResolver.StagePath(_folders.BasePath, "7_COPY_AZURE_FILES");
        string nextPath = PipelinePathResolver.StagePath(_folders.BasePath, "8_COPY_AZURE_BLOB");

        if (!Directory.Exists(sourcePath))
        {
            return 0;
        }

        string connectionString = configuration.GetValue<string>("AzureFiles:ConnectionString") ?? string.Empty;
        string shareName = configuration.GetValue<string>("AzureFiles:ShareName") ?? string.Empty;
        int retryMinutes = Math.Max(1, configuration.GetValue("RetryMinutes", 10));

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(shareName))
        {
            logger.LogWarning("Azure Files not configured. Files remain in 7_COPY_AZURE_FILES.");
            return 0;
        }

        ShareClient shareClient = new(connectionString, shareName);
        await shareClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        int moved = 0;
        foreach (string pdfPath in Directory.GetFiles(sourcePath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);
            if (!RetryWindowEvaluator.ShouldRetry(metadata.LastAzureFilesAttemptUtc, retryMinutes))
            {
                continue;
            }

            metadata.LastAzureFilesAttemptUtc = DateTime.UtcNow;
            MetadataSidecarStore.Save(pdfPath, metadata);

            try
            {
                string relativePath = BuildAzureFilesRelativePath(metadata, Path.GetFileName(pdfPath));
                await UploadAsync(shareClient, pdfPath, relativePath, cancellationToken);

                metadata.AzureFilesUploaded = true;

                string destination = Path.Combine(nextPath, Path.GetFileName(pdfPath));
                MetadataSidecarStore.MoveWithMetadata(pdfPath, destination);
                MetadataSidecarStore.Save(destination, metadata);
                moved++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Azure Files upload failed for {FileName}", Path.GetFileName(pdfPath));
                await operationalEventService.TrackAsync(
                    "error",
                    "transient",
                    "AZF001",
                    "Fallo de copia Azure Files",
                    ex.Message,
                    "7_COPY_AZURE_FILES",
                    metadata,
                    null,
                    cancellationToken);
            }
        }

        return moved;
    }

    private static string BuildAzureFilesRelativePath(DocumentProcessingMetadata metadata, string fileName)
    {
        string agency = metadata.Agency;
        string user = metadata.User;

        if (string.Equals(agency, "SJAGM", StringComparison.OrdinalIgnoreCase))
        {
            return $"AgentesMiCoope/SJAGM/{user}/{fileName}";
        }

        return $"Agencias/{agency}/{user}/{fileName}";
    }

    private static async Task UploadAsync(ShareClient shareClient, string localPath, string remotePath, CancellationToken cancellationToken)
    {
        string[] segments = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        ShareDirectoryClient current = shareClient.GetRootDirectoryClient();

        for (int i = 0; i < segments.Length - 1; i++)
        {
            ShareDirectoryClient next = current.GetSubdirectoryClient(segments[i]);
            await next.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            current = next;
        }

        string fileName = segments[^1];
        ShareFileClient fileClient = current.GetFileClient(fileName);
        FileInfo localFile = new(localPath);

        if (await fileClient.ExistsAsync(cancellationToken))
        {
            ShareFileProperties properties = await fileClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (properties.ContentLength == localFile.Length)
            {
                return;
            }

            await fileClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }

        await using FileStream stream = File.OpenRead(localPath);
        await fileClient.CreateAsync(stream.Length, cancellationToken: cancellationToken);
        await fileClient.UploadRangeAsync(
            new HttpRange(0, stream.Length),
            stream,
            cancellationToken: cancellationToken);
    }
}
