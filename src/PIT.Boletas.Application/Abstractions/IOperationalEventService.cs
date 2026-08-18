using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Application.Abstractions;

public interface IOperationalEventService
{
    Task TrackAsync(
        string severity,
        string errorType,
        string code,
        string title,
        string description,
        string stage,
        DocumentProcessingMetadata? metadata,
        string? remoteIp,
        CancellationToken cancellationToken);

    Task TrackDuplicateAsync(
        DocumentProcessingMetadata metadata,
        string originalName,
        string duplicateName,
        int duplicateCounter,
        CancellationToken cancellationToken);

    Task TrackHeartbeatAsync(string serviceName, string status, CancellationToken cancellationToken);
}
