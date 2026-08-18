using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Application.Abstractions;

public interface IOcrResultRepository
{
    Task<bool> TryInsertOcrJsonAsync(DocumentProcessingMetadata metadata, string rawJson, string source, CancellationToken cancellationToken);
}
