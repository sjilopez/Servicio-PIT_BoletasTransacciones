namespace PIT.Boletas.Application.Abstractions;

public interface IStageTwoValidationService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
}
