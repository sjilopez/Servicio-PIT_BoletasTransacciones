namespace PIT.Boletas.Application.Abstractions;

public interface IOneDriveScannerService
{
    string? ScannerPath { get; }

    Task<bool> EnsureScannerAsync(CancellationToken cancellationToken);

    Task QueueOriginalCopyAsync(string sourcePath, string targetFileName, CancellationToken cancellationToken);
}