using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Domain.Services;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageOneIngestionService(
    ILogger<StageOneIngestionService> logger,
    IOperationalEventService operationalEventService,
    Microsoft.Extensions.Options.IOptions<PipelineFoldersOptions> folderOptions,
    Microsoft.Extensions.Options.IOptions<IngestionOptions> ingestionOptions) : IStageOneIngestionService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;
    private readonly IngestionOptions _ingestion = ingestionOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string inPath = Path.Combine(_folders.BasePath, "1_IN");
        string validatePath = Path.Combine(_folders.BasePath, "2_VALIDATE");

        if (!Directory.Exists(inPath) || !Directory.Exists(validatePath))
        {
            logger.LogWarning("Stage folders are missing. In: {InPath}, Validate: {ValidatePath}", inPath, validatePath);
            return 0;
        }

        string[] files = Directory.GetFiles(inPath, "*.pdf", SearchOption.TopDirectoryOnly);
        int movedCount = 0;

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await WaitForStableFileAsync(file, cancellationToken))
            {
                logger.LogDebug("File is not stable yet: {FilePath}", file);
                continue;
            }

            try
            {
                DateTime creationTime = File.GetCreationTime(file);
                string hostName = Environment.MachineName.ToUpperInvariant();
                string agency = DocumentNameComposer.ResolveAgency(hostName);
                string user = ResolveInteractiveUser();

                string baseName = DocumentNameComposer.BuildBaseFileName(agency, user, creationTime);
                (string targetPath, int? duplicateCounter) = BuildTargetPath(validatePath, baseName, file);

                DocumentProcessingMetadata metadata = new()
                {
                    FileName = Path.GetFileName(targetPath),
                    SourceFileName = Path.GetFileName(file),
                    Agency = agency,
                    User = user,
                    HostName = hostName,
                    HostIp = ResolveHostIp(),
                    OriginalCreationTimeLocal = creationTime,
                    IngestedUtc = DateTime.UtcNow
                };

                MetadataSidecarStore.MoveWithMetadata(file, targetPath);
                MetadataSidecarStore.Save(targetPath, metadata);

                if (duplicateCounter.HasValue)
                {
                    await operationalEventService.TrackDuplicateAsync(
                        metadata,
                        baseName + Path.GetExtension(file),
                        Path.GetFileName(targetPath),
                        duplicateCounter.Value,
                        cancellationToken);
                }

                movedCount++;
                logger.LogInformation(
                    "Stage1 move OK | Agency: {Agency} | User: {User} | Host: {Host} | HostIp: {HostIp} | Source: {Source} | Target: {Target}",
                    agency,
                    user,
                    hostName,
                    metadata.HostIp,
                    file,
                    targetPath);
            }
            catch (IOException ioEx)
            {
                logger.LogWarning(ioEx, "Stage1 could not move file yet (IO lock/retry): {FilePath}", file);
                await operationalEventService.TrackAsync(
                    "warning",
                    "transient",
                    "FS002",
                    "Archivo bloqueado o inestable",
                    ioEx.Message,
                    "1_IN",
                    null,
                    null,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stage1 unexpected error while processing file: {FilePath}", file);
                await operationalEventService.TrackAsync(
                    "error",
                    "operational",
                    "S1_UNHANDLED",
                    "Error en ingesta de etapa 1",
                    ex.Message,
                    "1_IN",
                    null,
                    null,
                    cancellationToken);
            }
        }

        return movedCount;
    }

    private (string Path, int? DuplicateCounter) BuildTargetPath(string validatePath, string baseName, string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath);
        string firstCandidate = Path.Combine(validatePath, baseName + extension);

        if (!File.Exists(firstCandidate))
        {
            return (firstCandidate, null);
        }

        int duplicateCounter = 1;

        while (true)
        {
            string duplicateCandidate = Path.Combine(validatePath, $"{baseName}_{duplicateCounter:00}{extension}");
            if (!File.Exists(duplicateCandidate))
            {
                logger.LogWarning(
                    "Duplicate filename detected. Using duplicate correlativo {DuplicateCounter} for {BaseName}",
                    duplicateCounter,
                    baseName);
                return (duplicateCandidate, duplicateCounter);
            }

            duplicateCounter++;
        }
    }

    private async Task<bool> WaitForStableFileAsync(string path, CancellationToken cancellationToken)
    {
        long previousLength = -1;
        DateTime previousWrite = DateTime.MinValue;
        int stableCount = 0;

        for (int i = 0; i < _ingestion.StabilizationChecks * 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(path);
            if (!info.Exists)
            {
                return false;
            }

            bool sameState = info.Length == previousLength && info.LastWriteTimeUtc == previousWrite;
            bool notLocked = CanOpenExclusive(path);

            stableCount = sameState && notLocked ? stableCount + 1 : 0;

            if (stableCount >= _ingestion.StabilizationChecks)
            {
                return true;
            }

            previousLength = info.Length;
            previousWrite = info.LastWriteTimeUtc;

            await Task.Delay(_ingestion.StabilizationDelayMilliseconds, cancellationToken);
        }

        return false;
    }

    private static bool CanOpenExclusive(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return stream.Length >= 0;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveInteractiveUser()
    {
        try
        {
            if (WTSUserSession.TryGetActiveUser(out string activeUser))
            {
                return activeUser;
            }

            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "query",
                Arguments = "user",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);

            string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines.Skip(1))
            {
                string line = raw.Trim();
                if (!line.Contains('>') && !line.Contains("Active", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string clean = line.Replace(">", " ").Trim();
                string[] parts = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string user = parts[0];
                    if (user.Contains('\\'))
                    {
                        user = user[(user.LastIndexOf('\\') + 1)..];
                    }

                    return user.ToUpperInvariant();
                }
            }
        }
        catch
        {
            // Fallback to service identity when interactive session cannot be detected.
        }

        return "UNKNOWN";
    }

    private static class WTSUserSession
    {
        private const int WtsActive = 0;
        private const int WtsUserName = 5;

        public static bool TryGetActiveUser(out string userName)
        {
            userName = string.Empty;

            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out IntPtr sessions, out int count))
            {
                return false;
            }

            try
            {
                int sessionInfoSize = Marshal.SizeOf<WtsSessionInfo>();
                for (int index = 0; index < count; index++)
                {
                    IntPtr current = IntPtr.Add(sessions, index * sessionInfoSize);
                    WtsSessionInfo session = Marshal.PtrToStructure<WtsSessionInfo>(current);
                    if (session.State != WtsActive ||
                        !WTSQuerySessionInformation(IntPtr.Zero, session.SessionId, WtsUserName, out IntPtr buffer, out _))
                    {
                        continue;
                    }

                    try
                    {
                        string value = Marshal.PtrToStringUni(buffer)?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(value) && !value.EndsWith('$'))
                        {
                            userName = value.ToUpperInvariant();
                            return true;
                        }
                    }
                    finally
                    {
                        WTSFreeMemory(buffer);
                    }
                }
            }
            finally
            {
                WTSFreeMemory(sessions);
            }

            return false;
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSEnumerateSessions(
            IntPtr server,
            int reserved,
            int version,
            out IntPtr sessionInfo,
            out int sessionCount);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(
            IntPtr server,
            int sessionId,
            int informationClass,
            out IntPtr buffer,
            out int byteCount);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WtsSessionInfo
        {
            public int SessionId;
            public IntPtr WinStationName;
            public int State;
        }
    }

    private static string ResolveHostIp()
    {
        try
        {
            string host = Dns.GetHostName();
            IPAddress? address = Dns.GetHostAddresses(host)
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));

            return address?.ToString() ?? "N/A";
        }
        catch
        {
            return "N/A";
        }
    }
}
