using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.UnitTests;

public sealed class MetadataSidecarStoreTests
{
    [Fact]
    public async Task SaveConcurrentWriters_NeverLeaveInvalidJson()
    {
        string root = Path.Combine(Path.GetTempPath(), "pit-metadata-stress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string pdfPath = Path.Combine(root, "document.pdf");
        await File.WriteAllTextAsync(pdfPath, "pdf-placeholder");

        try
        {
            MetadataSidecarStore.Save(pdfPath, new DocumentProcessingMetadata());

            Task[] writers = Enumerable.Range(0, 100)
                .Select(index => Task.Run(() =>
                {
                    MetadataSidecarStore.Save(pdfPath, new DocumentProcessingMetadata
                    {
                        Agency = $"AGENCY-{index}",
                        User = $"USER-{index}"
                    });
                }))
                .ToArray();

            await Task.WhenAll(writers);

            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);

            Assert.StartsWith("AGENCY-", metadata.Agency);
            Assert.StartsWith("USER-", metadata.User);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void MoveWithMetadata_MovesBothFilesTogether()
    {
        string root = Path.Combine(Path.GetTempPath(), "pit-metadata-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string sourcePdf = Path.Combine(root, "source.pdf");
        string targetPdf = Path.Combine(root, "target", "document.pdf");

        try
        {
            File.WriteAllText(sourcePdf, "pdf-placeholder");
            MetadataSidecarStore.Save(sourcePdf, new DocumentProcessingMetadata { Agency = "SJAGM" });
            Directory.CreateDirectory(Path.GetDirectoryName(targetPdf)!);

            MetadataSidecarStore.MoveWithMetadata(sourcePdf, targetPdf);

            Assert.False(File.Exists(sourcePdf));
            Assert.False(File.Exists(MetadataSidecarStore.GetMetadataPath(sourcePdf)));
            Assert.True(File.Exists(targetPdf));
            Assert.True(File.Exists(MetadataSidecarStore.GetMetadataPath(targetPdf)));
            Assert.Equal("SJAGM", MetadataSidecarStore.LoadOrCreate(targetPdf).Agency);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}