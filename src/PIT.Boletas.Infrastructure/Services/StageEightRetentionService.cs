using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageEightRetentionService(
    ILogger<StageEightRetentionService> logger,
    IOptions<PipelineFoldersOptions> folderOptions,
    IConfiguration configuration) : IStageEightRetentionService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string backupPath = PipelinePathResolver.StagePath(_folders.BasePath, "9_LOCAL_BACKUP");
        if (!Directory.Exists(backupPath))
        {
            return Task.FromResult(0);
        }

        int retentionDays = Math.Max(1, configuration.GetValue("RetentionDays", 7));
        int deleted = 0;

        foreach (string pdfPath in Directory.GetFiles(backupPath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);
            if (!RetryWindowEvaluator.ShouldDeleteFromBackup(metadata, pdfPath, retentionDays))
            {
                continue;
            }

            MetadataSidecarStore.DeleteWithMetadata(pdfPath);
            deleted++;
            logger.LogInformation("Retention deleted file from local backup: {FileName}", Path.GetFileName(pdfPath));
        }

        return Task.FromResult(deleted);
    }
}
