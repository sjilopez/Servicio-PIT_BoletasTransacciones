using System.Text.Json;
using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Infrastructure.Utils;

public static class MetadataSidecarStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

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
        string content = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(metadataPath, content);
    }

    public static void MoveWithMetadata(string sourcePdfPath, string targetPdfPath)
    {
        string sourceMetaPath = GetMetadataPath(sourcePdfPath);
        string targetMetaPath = GetMetadataPath(targetPdfPath);

        File.Move(sourcePdfPath, targetPdfPath);

        if (File.Exists(sourceMetaPath))
        {
            File.Move(sourceMetaPath, targetMetaPath, overwrite: true);
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
