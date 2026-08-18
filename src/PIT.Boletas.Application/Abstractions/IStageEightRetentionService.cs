namespace PIT.Boletas.Application.Abstractions;

public interface IStageEightRetentionService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
