using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Domain.Services;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageOneIngestionService(
    ILogger<StageOneIngestionService> logger,
    IOperationalEventService operationalEventService,
    IOneDriveScannerService oneDriveScannerService,
    Microsoft.Extensions.Options.IOptions<PipelineFoldersOptions> folderOptions,
    Microsoft.Extensions.Options.IOptions<IngestionOptions> ingestionOptions) : IStageOneIngestionService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;
    private readonly IngestionOptions _ingestion = ingestionOptions.Value;
    private readonly IOneDriveScannerService _oneDriveScannerService = oneDriveScannerService;
    private readonly ConcurrentQueue<string> _pendingFiles = new();
    private readonly ConcurrentDictionary<string, byte> _queuedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _activitySignal = new(0);
    private readonly object _watcherLock = new();
    private FileSystemWatcher? _watcher;
    private bool _watcherInitialized;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        string inPath = Path.Combine(_folders.BasePath, "1_IN");
        string validatePath = Path.Combine(_folders.BasePath, "2_VALIDATE");

        if (!Directory.Exists(inPath) || !Directory.Exists(validatePath))
        {
            logger.LogWarning("Stage folders are missing. In: {InPath}, Validate: {ValidatePath}", inPath, validatePath);
            return 0;
        }

        EnsureWatcher(inPath);

        foreach (string existingFile in Directory.GetFiles(inPath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            Enqueue(existingFile);
        }

        int movedCount = 0;

        while (_pendingFiles.TryDequeue(out string? file))
        {
            _queuedFiles.TryRemove(file, out _);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(file))
            {
                continue;
            }

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
                int pageCount = GetPageCount(file);
                string sourceFileName = Path.GetFileName(file);
                string hostIp = ResolveHostIp();

                movedCount += pageCount == 1
                    ? await MovePageAsync(file, validatePath, baseName, sourceFileName, agency, user, hostName, hostIp, creationTime, pageCount, cancellationToken)
                    : await SplitAndMoveAsync(file, validatePath, baseName, sourceFileName, agency, user, hostName, hostIp, creationTime, pageCount, cancellationToken);
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

    public async Task WaitForActivityAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _activitySignal.WaitAsync(timeout, cancellationToken);
    }

    private void EnsureWatcher(string inPath)
    {
        lock (_watcherLock)
        {
            if (_watcherInitialized)
            {
                return;
            }

            _watcher = new FileSystemWatcher(inPath, "*.pdf")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;
            _watcherInitialized = true;
            logger.LogInformation("Stage1 watcher enabled for {InputPath}", inPath);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs args) => Enqueue(args.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs args) => Enqueue(args.FullPath);

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        logger.LogWarning(args.GetException(), "Stage1 watcher reported an error; reconciliation remains enabled.");
        _activitySignal.Release();
    }

    private void Enqueue(string path)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !_queuedFiles.TryAdd(path, 0))
        {
            return;
        }

        _pendingFiles.Enqueue(path);
        _activitySignal.Release();
    }

    private async Task<int> MovePageAsync(
        string sourcePath,
        string validatePath,
        string baseName,
        string sourceFileName,
        string agency,
        string user,
        string hostName,
        string hostIp,
        DateTime creationTime,
        int pageCount,
        CancellationToken cancellationToken)
    {
        (string targetPath, int? duplicateCounter) = BuildTargetPath(validatePath, baseName, sourcePath);
        DocumentProcessingMetadata metadata = CreateMetadata(targetPath, sourceFileName, agency, user, hostName, hostIp, creationTime, pageCount);

        await _oneDriveScannerService.QueueOriginalCopyAsync(sourcePath, Path.GetFileName(targetPath), cancellationToken);
        MetadataSidecarStore.MoveWithMetadata(sourcePath, targetPath);
        MetadataSidecarStore.Save(targetPath, metadata);
        await TrackDuplicateAsync(metadata, baseName, sourcePath, targetPath, duplicateCounter, cancellationToken);
        logger.LogInformation("Stage1 move OK | Source: {Source} | Target: {Target}", sourcePath, targetPath);
        return 1;
    }

    private async Task<int> SplitAndMoveAsync(
        string sourcePath,
        string validatePath,
        string baseName,
        string sourceFileName,
        string agency,
        string user,
        string hostName,
        string hostIp,
        DateTime creationTime,
        int pageCount,
        CancellationToken cancellationToken)
    {
        await _oneDriveScannerService.QueueOriginalCopyAsync(sourcePath, baseName + Path.GetExtension(sourcePath), cancellationToken);
        List<string> temporaryFiles = [];
        try
        {
            using PdfDocument sourceDocument = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                string temporaryPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, $".{Guid.NewGuid():N}.part");
                using (PdfDocument pageDocument = new())
                {
                    pageDocument.AddPage(sourceDocument.Pages[pageIndex]);
                    pageDocument.Save(temporaryPath);
                }

                temporaryFiles.Add(temporaryPath);
            }

            for (int pageIndex = 0; pageIndex < temporaryFiles.Count; pageIndex++)
            {
                string pageBaseName = $"{baseName}_{pageIndex + 1:00}";
                string temporaryPath = temporaryFiles[pageIndex];
                (string targetPath, int? duplicateCounter) = BuildTargetPath(validatePath, pageBaseName, sourcePath);
                DocumentProcessingMetadata metadata = CreateMetadata(targetPath, sourceFileName, agency, user, hostName, hostIp, creationTime, pageCount);
                MetadataSidecarStore.MoveWithMetadata(temporaryPath, targetPath);
                temporaryFiles[pageIndex] = string.Empty;
                MetadataSidecarStore.Save(targetPath, metadata);
                await TrackDuplicateAsync(metadata, pageBaseName, sourcePath, targetPath, duplicateCounter, cancellationToken);
            }

            MetadataSidecarStore.DeleteWithMetadata(sourcePath);
            logger.LogInformation("Stage1 split OK | Source: {Source} | Pages: {PageCount}", sourcePath, pageCount);
            return pageCount;
        }
        finally
        {
            foreach (string temporaryPath in temporaryFiles.Where(path => !string.IsNullOrEmpty(path)))
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private async Task TrackDuplicateAsync(
        DocumentProcessingMetadata metadata,
        string baseName,
        string sourcePath,
        string targetPath,
        int? duplicateCounter,
        CancellationToken cancellationToken)
    {
        if (duplicateCounter.HasValue)
        {
            await operationalEventService.TrackDuplicateAsync(
                metadata,
                baseName + Path.GetExtension(sourcePath),
                Path.GetFileName(targetPath),
                duplicateCounter.Value,
                cancellationToken);
        }
    }

    private static DocumentProcessingMetadata CreateMetadata(
        string targetPath,
        string sourceFileName,
        string agency,
        string user,
        string hostName,
        string hostIp,
        DateTime creationTime,
        int pageCount)
    {
        return new DocumentProcessingMetadata
        {
            FileName = Path.GetFileName(targetPath),
            SourceFileName = sourceFileName,
            Agency = agency,
            User = user,
            HostName = hostName,
            HostIp = hostIp,
            OriginalCreationTimeLocal = creationTime,
            IngestedUtc = DateTime.UtcNow,
            PageCount = pageCount
        };
    }

    private static int GetPageCount(string sourcePath)
    {
        using PdfDocument document = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        return Math.Max(1, document.PageCount);
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
