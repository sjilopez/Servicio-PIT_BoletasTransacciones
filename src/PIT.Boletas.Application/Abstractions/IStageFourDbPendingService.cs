namespace PIT.Boletas.Application.Abstractions;

public interface IStageFourDbPendingService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
