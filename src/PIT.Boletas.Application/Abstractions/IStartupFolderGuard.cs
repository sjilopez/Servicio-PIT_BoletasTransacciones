using PIT.Boletas.Application.Models;

namespace PIT.Boletas.Application.Abstractions;

public interface IStartupFolderGuard
{
    Task<FolderValidationReport> ValidateAndEnsureAsync(CancellationToken cancellationToken);
}
