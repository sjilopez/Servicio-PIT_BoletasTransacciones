using System.Collections.Concurrent;
using System.Text.Json;
using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Infrastructure.Utils;

public static class MetadataSidecarStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, object> SaveLocks = new(StringComparer.OrdinalIgnoreCase);

    public static string GetMetadataPath(string pdfPath)
    {
        return pdfPath + ".meta.json";
    }

    public static DocumentProcessingMetadata LoadOrCreate(string pdfPath)
    {
        string metadataPath = GetMetadataPath(pdfPath);

        if (!File.Exists(metadataPath))
        {
            return new DocumentProcessingMetadata
            {
                FileName = Path.GetFileName(pdfPath),
                SourceFileName = Path.GetFileName(pdfPath),
                OriginalCreationTimeLocal = File.GetCreationTime(pdfPath),
                IngestedUtc = DateTime.UtcNow
            };
        }

        string content = File.ReadAllText(metadataPath);
        return JsonSerializer.Deserialize<DocumentProcessingMetadata>(content, JsonOptions)
               ?? new DocumentProcessingMetadata();
    }

    public static void Save(string pdfPath, DocumentProcessingMetadata metadata)
    {
        metadata.FileName = Path.GetFileName(pdfPath);
        string metadataPath = GetMetadataPath(pdfPath);
        string temporaryPath = metadataPath + $".{Guid.NewGuid():N}.tmp";
        string content = JsonSerializer.Serialize(metadata, JsonOptions);
        object saveLock = SaveLocks.GetOrAdd(Path.GetFullPath(metadataPath), static _ => new object());

        lock (saveLock)
        {
            try
            {
                File.WriteAllText(temporaryPath, content);
                if (File.Exists(metadataPath))
                {
                    File.Replace(temporaryPath, metadataPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, metadataPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    public static void MoveWithMetadata(string sourcePdfPath, string targetPdfPath)
    {
        string sourceMetaPath = GetMetadataPath(sourcePdfPath);
        string targetMetaPath = GetMetadataPath(targetPdfPath);

        bool pdfMoved = false;
        try
        {
            File.Move(sourcePdfPath, targetPdfPath);
            pdfMoved = true;

            if (File.Exists(sourceMetaPath))
            {
                File.Move(sourceMetaPath, targetMetaPath, overwrite: true);
            }
        }
        catch
        {
            if (pdfMoved && File.Exists(targetPdfPath) && !File.Exists(sourcePdfPath))
            {
                File.Move(targetPdfPath, sourcePdfPath);
            }

            throw;
        }
    }

    public static void DeleteWithMetadata(string pdfPath)
    {
        if (File.Exists(pdfPath))
        {
            File.Delete(pdfPath);
        }

        string metaPath = GetMetadataPath(pdfPath);
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }
    }
}
