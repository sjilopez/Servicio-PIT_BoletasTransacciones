namespace PIT.Boletas.Application.Abstractions;

public interface IStageThreeOcrService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
