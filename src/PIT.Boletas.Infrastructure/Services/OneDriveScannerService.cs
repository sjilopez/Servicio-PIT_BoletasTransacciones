using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Win32;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PIT.Boletas.Application.Abstractions;

namespace PIT.Boletas.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public sealed class OneDriveScannerService(ILogger<OneDriveScannerService> logger) : BackgroundService, IOneDriveScannerService
{
    private const string PreferredOneDrivePrefix = "OneDrive - Cooperativa de Ahorro y Credito Integral";
    private const string FallbackOneDriveName = "OneDrive";

    private readonly Channel<CopyRequest> _pendingCopies = Channel.CreateUnbounded<CopyRequest>();
    private readonly ConcurrentDictionary<string, byte> _queuedSources = new(StringComparer.OrdinalIgnoreCase);
    private string? _scannerPath;

    public string? ScannerPath => _scannerPath;

    public Task<bool> EnsureScannerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? scannerPath = ResolveScannerPath();
        if (scannerPath is null)
        {
            logger.LogWarning("OneDrive or Desktop/Escritorio was not detected for the active user.");
            return Task.FromResult(false);
        }

        try
        {
            Directory.CreateDirectory(scannerPath);
            _scannerPath = scannerPath;
            logger.LogInformation("OneDrive Scanner ready at {ScannerPath}", scannerPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create OneDrive Scanner at {ScannerPath}", scannerPath);
            return Task.FromResult(false);
        }
    }

    public async Task QueueOriginalCopyAsync(string sourcePath, string targetFileName, CancellationToken cancellationToken)
    {
        string pendingPath = Path.Combine(Path.GetTempPath(), $"pit-onedrive-{Guid.NewGuid():N}.pdf");
        try
        {
            if (_scannerPath is not null)
            {
                string targetPath = Path.Combine(_scannerPath, targetFileName);
                File.Copy(sourcePath, targetPath, overwrite: true);
                logger.LogInformation("OneDrive original copy OK | Target: {TargetPath}", targetPath);
                return;
            }

            File.Copy(sourcePath, pendingPath, overwrite: false);
            await EnqueueAsync(new CopyRequest(pendingPath, targetFileName), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OneDrive original copy failed; queuing retry for {SourcePath}", sourcePath);
            try
            {
                if (!File.Exists(pendingPath))
                {
                    File.Copy(sourcePath, pendingPath, overwrite: false);
                }

                await EnqueueAsync(new CopyRequest(pendingPath, targetFileName), cancellationToken);
            }
            catch (Exception queueException)
            {
                logger.LogError(queueException, "Could not queue OneDrive copy retry for {SourcePath}", sourcePath);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (CopyRequest request in _pendingCopies.Reader.ReadAllAsync(stoppingToken))
        {
            _queuedSources.TryRemove(request.SourcePath, out _);
            bool copied = false;
            try
            {
                copied = await EnsureScannerAsync(stoppingToken);
                if (copied && _scannerPath is not null)
                {
                    File.Copy(request.SourcePath, Path.Combine(_scannerPath, request.TargetFileName), overwrite: true);
                    logger.LogInformation("OneDrive queued copy OK | Target: {TargetFileName}", request.TargetFileName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OneDrive queued copy failed for {TargetFileName}", request.TargetFileName);
            }

            if (copied)
            {
                TryDelete(request.SourcePath);
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                await EnqueueAsync(request, stoppingToken);
            }
        }
    }

    private async Task EnqueueAsync(CopyRequest request, CancellationToken cancellationToken)
    {
        if (_queuedSources.TryAdd(request.SourcePath, 0))
        {
            await _pendingCopies.Writer.WriteAsync(request, cancellationToken);
        }
    }

    private static string? ResolveScannerPath()
    {
        string? user = StageOneIngestionService.ResolveInteractiveUser();
        if (string.Equals(user, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (string sid in Registry.Users.GetSubKeyNames().Where(IsUserSid))
        {
            using RegistryKey? userKey = Registry.Users.OpenSubKey(sid);
            if (userKey is null || !IsMatchingUser(userKey, user))
            {
                continue;
            }

            using RegistryKey? accounts = userKey.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            List<string> roots = accounts is null
                ? []
                : accounts.GetSubKeyNames()
                    .Select(accountName => ReadString(accounts, accountName, "UserFolder"))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!)
                    .ToList();

            string? configuredScannerPath = ResolveScannerFromPreferredRoot(roots);
            if (configuredScannerPath is not null)
            {
                return configuredScannerPath;
            }

            string? desktop = ReadString(userKey, @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Desktop");
            configuredScannerPath = ResolveScannerFromDesktop(desktop);
            if (configuredScannerPath is not null)
            {
                return configuredScannerPath;
            }
        }

        return ResolveScannerFromEnvironment(user);
    }

    private static string? ResolveScannerFromDesktop(string? desktop)
    {
        if (string.IsNullOrWhiteSpace(desktop))
        {
            return null;
        }

        string expandedDesktop = Environment.ExpandEnvironmentVariables(desktop.Trim().Trim('"'));
        if (!Directory.Exists(expandedDesktop) || !IsAllowedOneDrivePath(expandedDesktop))
        {
            return null;
        }

        return Path.Combine(expandedDesktop, "Scanner");
    }

    private static string? ResolveScannerFromOneDriveRoot(string? oneDriveRoot)
    {
        if (string.IsNullOrWhiteSpace(oneDriveRoot) || !Directory.Exists(oneDriveRoot))
        {
            return null;
        }

        string desktop = Directory.Exists(Path.Combine(oneDriveRoot, "Desktop"))
            ? "Desktop"
            : Directory.Exists(Path.Combine(oneDriveRoot, "Escritorio")) ? "Escritorio" : string.Empty;
        return string.IsNullOrEmpty(desktop) ? null : Path.Combine(oneDriveRoot, desktop, "Scanner");
    }

    private static string? ResolveScannerFromEnvironment(string user)
    {
        string[] roots =
        [
            Environment.GetEnvironmentVariable("OneDriveCommercial") ?? string.Empty,
            Environment.GetEnvironmentVariable("OneDriveConsumer") ?? string.Empty,
            Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty
        ];

        string? scannerPath = ResolveScannerFromPreferredRoot(roots.Where(path => !string.IsNullOrWhiteSpace(path)));
        if (scannerPath is not null)
        {
            return scannerPath;
        }

        string profile = Path.Combine(@"C:\Users", user);
        return Directory.Exists(profile)
            ? Directory.GetDirectories(profile, "OneDrive*", SearchOption.TopDirectoryOnly)
                .Where(IsAllowedOneDriveRoot)
                .Select(ResolveScannerFromOneDriveRoot)
                .FirstOrDefault(path => path is not null)
            : null;
    }

    private static string? ResolveScannerFromPreferredRoot(IEnumerable<string> roots)
    {
        string? preferredRoot = roots.FirstOrDefault(IsPreferredOneDriveRoot);
        if (preferredRoot is not null)
        {
            return ResolveScannerFromOneDriveRoot(preferredRoot);
        }

        string? fallbackRoot = roots.FirstOrDefault(IsFallbackOneDriveRoot);
        return fallbackRoot is null ? null : ResolveScannerFromOneDriveRoot(fallbackRoot);
    }

    private static bool IsPreferredOneDriveRoot(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.StartsWith(PreferredOneDrivePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFallbackOneDriveRoot(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(name, FallbackOneDriveName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedOneDriveRoot(string path)
    {
        return IsPreferredOneDriveRoot(path) || IsFallbackOneDriveRoot(path);
    }

    private static bool IsAllowedOneDrivePath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, FallbackOneDriveName, StringComparison.OrdinalIgnoreCase)
                            || segment.StartsWith(PreferredOneDrivePrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMatchingUser(RegistryKey userKey, string user)
    {
        string? profilePath = ReadString(userKey, @"Volatile Environment", "USERPROFILE")
                              ?? ReadString(userKey, @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Personal");
        return string.IsNullOrWhiteSpace(profilePath)
            || profilePath.Contains(user, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(RegistryKey parent, string subKeyName, string valueName)
    {
        using RegistryKey? key = parent.OpenSubKey(subKeyName);
        return key?.GetValue(valueName)?.ToString();
    }

    private static bool IsOneDrivePath(string path)
    {
        return path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserSid(string name)
    {
        return name.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record CopyRequest(string SourcePath, string TargetFileName);
}