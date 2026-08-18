namespace PIT.Boletas.Application.Abstractions;

public interface IStageSevenAzureBlobService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
