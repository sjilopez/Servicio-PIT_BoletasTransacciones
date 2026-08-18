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
    IOcrResultRepository repository) : IStageFourDbPendingService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string pendingPath = PipelinePathResolver.StagePath(_folders.BasePath, "5_DB_PENDING");
        if (!Directory.Exists(pendingPath))
        {
            return 0;
        }

        int done = 0;
        foreach (string jsonPath in Directory.GetFiles(pendingPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string payload = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            string fileName = Path.GetFileNameWithoutExtension(jsonPath) + ".pdf";

            DocumentProcessingMetadata metadata = new()
            {
                FileName = fileName,
                SourceFileName = fileName,
                IngestedUtc = DateTime.UtcNow
            };

            bool inserted = await repository.TryInsertOcrJsonAsync(metadata, payload, "5_DB_PENDING", cancellationToken);
            if (!inserted)
            {
                continue;
            }

            File.Delete(jsonPath);
            done++;
        }

        if (done > 0)
        {
            logger.LogInformation("Stage4 DB pending resolved JSON files: {Count}", done);
        }

        return done;
    }
}
