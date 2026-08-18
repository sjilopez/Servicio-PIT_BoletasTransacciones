namespace PIT.Boletas.Application.Abstractions;

public interface IStageFiveCompressionService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
