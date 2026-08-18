namespace PIT.Boletas.Application.Abstractions;

public interface IStageSixAzureFilesService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
