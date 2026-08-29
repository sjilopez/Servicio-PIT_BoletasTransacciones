using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Services;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.UnitTests;

public sealed class StageFourDbPendingServiceTests
{
    [Fact]
    public async Task MissingPdf_DoesNotInsertPayload()
    {
        string root = CreateRoot();
        try
        {
            string pendingPath = Path.Combine(root, PipelineStageNames.DbPending);
            Directory.CreateDirectory(pendingPath);
            string jsonPath = Path.Combine(pendingPath, "document.json");
            await File.WriteAllTextAsync(jsonPath, "{\"id\":1}");

            RecordingRepository repository = new();
            StageFourDbPendingService service = CreateService(root, repository);

            int processed = await service.ProcessPendingAsync(CancellationToken.None);

            Assert.Equal(0, processed);
            Assert.Equal(0, repository.InsertCount);
            Assert.True(File.Exists(jsonPath));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ConfirmedPayload_MovesPdfAndMetadataToAzureFilesStage()
    {
        string root = CreateRoot();
        try
        {
            string pendingPath = Path.Combine(root, PipelineStageNames.DbPending);
            string azureFilePath = Path.Combine(root, PipelineStageNames.AzureFile);
            Directory.CreateDirectory(pendingPath);
            Directory.CreateDirectory(azureFilePath);

            string pdfPath = Path.Combine(pendingPath, "document.pdf");
            string jsonPath = Path.Combine(pendingPath, "document.json");
            await File.WriteAllTextAsync(pdfPath, "pdf-placeholder");
            await File.WriteAllTextAsync(jsonPath, "{\"id\":1}");
            MetadataSidecarStore.Save(pdfPath, new DocumentProcessingMetadata { Agency = "SJAGM" });

            RecordingRepository repository = new() { InsertResult = true };
            StageFourDbPendingService service = CreateService(root, repository);

            int processed = await service.ProcessPendingAsync(CancellationToken.None);

            string destinationPdf = Path.Combine(azureFilePath, "document.pdf");
            Assert.Equal(1, processed);
            Assert.Equal(1, repository.InsertCount);
            Assert.False(File.Exists(pdfPath));
            Assert.False(File.Exists(jsonPath));
            Assert.True(File.Exists(destinationPdf));
            Assert.Equal("SJAGM", MetadataSidecarStore.LoadOrCreate(destinationPdf).Agency);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static StageFourDbPendingService CreateService(string root, IOcrResultRepository repository)
    {
        PipelineFoldersOptions folders = new() { BasePath = root };
        return new StageFourDbPendingService(
            NullLogger<StageFourDbPendingService>.Instance,
            Options.Create(folders),
            new ConfigurationBuilder().Build(),
            repository);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "pit-db-pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingRepository : IOcrResultRepository
    {
        public bool InsertResult { get; init; }

        public int InsertCount { get; private set; }

        public Task<bool> TryInsertOcrJsonAsync(
            DocumentProcessingMetadata metadata,
            string rawJson,
            string source,
            CancellationToken cancellationToken)
        {
            InsertCount++;
            return Task.FromResult(InsertResult);
        }

        public Task<bool> TryUpdateMetadataAsync(DocumentProcessingMetadata metadata, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}