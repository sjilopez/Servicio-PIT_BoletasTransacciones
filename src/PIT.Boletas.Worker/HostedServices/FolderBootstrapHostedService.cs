using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Models;

namespace PIT.Boletas.Worker.HostedServices;

public sealed class FolderBootstrapHostedService(
    IStartupFolderGuard startupFolderGuard,
    IOneDriveScannerService oneDriveScannerService,
    IOperationalEventService operationalEventService,
    ILogger<FolderBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        FolderValidationReport report = await startupFolderGuard.ValidateAndEnsureAsync(cancellationToken);
        await oneDriveScannerService.EnsureScannerAsync(cancellationToken);

        foreach (FolderValidationIssue issue in report.Issues)
        {
            if (issue.Severity == FolderIssueSeverity.Critical)
            {
                logger.LogError("[{Code}] {Message} | Path: {Path}", issue.Code, issue.Message, issue.FullPath);
                await operationalEventService.TrackAsync(
                    "critical",
                    "configuration",
                    issue.Code,
                    "Fallo de validacion de carpetas al iniciar",
                    issue.Message + " | " + issue.FullPath,
                    "startup",
                    null,
                    null,
                    cancellationToken);
                continue;
            }

            if (issue.Severity == FolderIssueSeverity.Warning)
            {
                logger.LogWarning("[{Code}] {Message} | Path: {Path}", issue.Code, issue.Message, issue.FullPath);
                await operationalEventService.TrackAsync(
                    "warning",
                    "operational",
                    issue.Code,
                    "Advertencia de bootstrap",
                    issue.Message + " | " + issue.FullPath,
                    "startup",
                    null,
                    null,
                    cancellationToken);
                continue;
            }

            logger.LogInformation("[{Code}] {Message} | Path: {Path}", issue.Code, issue.Message, issue.FullPath);
        }

        if (report.HasCriticalIssues)
        {
            throw new InvalidOperationException(
                "Startup folder validation failed with critical issues. Service cannot continue in a degraded state.");
        }

        logger.LogInformation("Folder bootstrap completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
