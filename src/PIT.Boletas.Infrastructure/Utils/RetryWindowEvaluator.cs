using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Infrastructure.Utils;

public static class RetryWindowEvaluator
{
    public static bool ShouldRetry(DateTime? lastAttemptUtc, int retryMinutes)
    {
        if (!lastAttemptUtc.HasValue)
        {
            return true;
        }

        return DateTime.UtcNow - lastAttemptUtc.Value >= TimeSpan.FromMinutes(Math.Max(1, retryMinutes));
    }

    public static bool ShouldDeleteFromBackup(DocumentProcessingMetadata metadata, string filePath, int retentionDays)
    {
        if (!metadata.AzureBlobUploaded || !metadata.AzureFilesUploaded)
        {
            return false;
        }

        DateTime baseline = metadata.OriginalCreationTimeLocal == default
            ? File.GetCreationTime(filePath)
            : metadata.OriginalCreationTimeLocal;

        return DateTime.Now >= baseline.AddDays(Math.Max(1, retentionDays));
    }
}
