namespace PIT.Boletas.Application.Abstractions;

public interface IStageOneIngestionService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
