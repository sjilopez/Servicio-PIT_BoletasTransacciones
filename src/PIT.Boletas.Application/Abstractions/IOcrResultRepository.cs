using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Application.Abstractions;

public interface IOcrResultRepository
{
    Task<bool> TryInsertOcrJsonAsync(DocumentProcessingMetadata metadata, string rawJson, string source, CancellationToken cancellationToken);

    Task<bool> TryInsertOcrAttemptAsync(OcrAttempt attempt, CancellationToken cancellationToken);

    Task<bool> TryUpdateMetadataAsync(DocumentProcessingMetadata metadata, CancellationToken cancellationToken);
}
