using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;
using PIT.Boletas.Infrastructure.Utils;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class StageThreeOcrService(
    ILogger<StageThreeOcrService> logger,
    IHttpClientFactory httpClientFactory,
    IOcrResultRepository repository,
    IOperationalEventService operationalEventService,
    IOptions<PipelineFoldersOptions> folderOptions,
    IConfiguration configuration) : IStageThreeOcrService
{
    private readonly PipelineFoldersOptions _folders = folderOptions.Value;

    public async Task<(bool Success, int? StatusCode, string Error, string Payload)> CheckExternalOcrAsync(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        string endpoint = configuration.GetValue<string>("ExternalOcr:Endpoint") ?? string.Empty;
        string apiKey = configuration.GetValue<string>("ExternalOcr:ApiKey") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, null, "Endpoint o API key vacios.", string.Empty);
        }

        (bool success, string payload, int? statusCode, string error) = await TryCallApiAsync(endpoint, apiKey, pdfPath, cancellationToken);
        return (success, statusCode, error, payload);
    }

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        int moved = 0;
        moved += await ProcessFolderAsync("3_OCR", fromRetryFolder: false, cancellationToken);
        moved += await ProcessFolderAsync("4_ERROR_OCR", fromRetryFolder: true, cancellationToken);
        return moved;
    }

    private async Task<int> ProcessFolderAsync(string stageFolder, bool fromRetryFolder, CancellationToken cancellationToken)
    {
        string sourcePath = PipelinePathResolver.StagePath(_folders.BasePath, stageFolder);
        string errorPath = PipelinePathResolver.StagePath(_folders.BasePath, "4_ERROR_OCR");
        string dbPendingPath = PipelinePathResolver.StagePath(_folders.BasePath, "5_DB_PENDING");
        string compressPath = PipelinePathResolver.StagePath(_folders.BasePath, "6_COMPRESS");

        if (!Directory.Exists(sourcePath))
        {
            return 0;
        }

        int moved = 0;
        int retryMinutes = Math.Max(1, configuration.GetValue("RetryMinutes", 10));
        string endpoint = configuration.GetValue<string>("ExternalOcr:Endpoint") ?? string.Empty;
        string apiKey = configuration.GetValue<string>("ExternalOcr:ApiKey") ?? string.Empty;

        foreach (string pdfPath in Directory.GetFiles(sourcePath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentProcessingMetadata metadata = MetadataSidecarStore.LoadOrCreate(pdfPath);

            if (metadata.ApiOcrSucceeded)
            {
                string donePath = Path.Combine(compressPath, Path.GetFileName(pdfPath));
                MetadataSidecarStore.MoveWithMetadata(pdfPath, donePath);
                moved++;
                continue;
            }

            if (fromRetryFolder && !RetryWindowEvaluator.ShouldRetry(metadata.LastApiAttemptUtc, retryMinutes))
            {
                continue;
            }

            metadata.LastApiAttemptUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                metadata.ExternalOcrLastStatusCode = null;
                metadata.ExternalOcrLastError = "Endpoint o API key vacios.";
                logger.LogWarning("OCR API not configured. File remains in retry flow: {FileName}", Path.GetFileName(pdfPath));
                await operationalEventService.TrackAsync(
                    "warning",
                    "configuration",
                    "OCRE_CFG",
                    "OCR API no configurada",
                    "Endpoint/API key vacios; documento enviado a flujo de reintento.",
                    stageFolder,
                    metadata,
                    null,
                    cancellationToken);
                MetadataSidecarStore.Save(pdfPath, metadata);
                if (!fromRetryFolder)
                {
                    string failPath = Path.Combine(errorPath, Path.GetFileName(pdfPath));
                    MetadataSidecarStore.MoveWithMetadata(pdfPath, failPath);
                    moved++;
                }

                continue;
            }

            (bool apiSuccess, string payloadJson, int? statusCode, string error) = await TryCallApiAsync(endpoint, apiKey, pdfPath, cancellationToken);
            if (!apiSuccess)
            {
                metadata.ExternalOcrLastStatusCode = statusCode;
                metadata.ExternalOcrLastError = error;
                await operationalEventService.TrackAsync(
                    "error",
                    "transient",
                    "OCRE001",
                    "Fallo en OCR API",
                    "No se obtuvo respuesta valida de OCR API. Se aplicara reintento.",
                    stageFolder,
                    metadata,
                    null,
                    cancellationToken);
                MetadataSidecarStore.Save(pdfPath, metadata);
                if (!fromRetryFolder)
                {
                    string failPath = Path.Combine(errorPath, Path.GetFileName(pdfPath));
                    MetadataSidecarStore.MoveWithMetadata(pdfPath, failPath);
                    moved++;
                }

                continue;
            }

            metadata.ApiOcrSucceeded = true;
            metadata.ExternalOcrLastStatusCode = statusCode;
            metadata.ExternalOcrLastError = string.Empty;
            bool dbInserted = await repository.TryInsertOcrJsonAsync(metadata, payloadJson, stageFolder, cancellationToken);

            if (!dbInserted)
            {
                await operationalEventService.TrackAsync(
                    "error",
                    "operational",
                    "DB002",
                    "Fallo al persistir OCR en MySQL",
                    "Se guarda JSON en 5_DB_PENDING para reintento.",
                    stageFolder,
                    metadata,
                    null,
                    cancellationToken);
                string pendingJson = PipelinePathResolver.BuildNonCollidingFilePath(
                    dbPendingPath,
                    Path.GetFileNameWithoutExtension(pdfPath) + ".json");
                await File.WriteAllTextAsync(pendingJson, payloadJson, Encoding.UTF8, cancellationToken);
                MetadataSidecarStore.SaveToPath(MetadataSidecarStore.GetMetadataPath(pendingJson), metadata);
            }

            string nextPath = Path.Combine(compressPath, Path.GetFileName(pdfPath));
            MetadataSidecarStore.MoveWithMetadata(pdfPath, nextPath);
            MetadataSidecarStore.Save(nextPath, metadata);
            moved++;
        }

        return moved;
    }

    private async Task<(bool Success, string PayloadJson, int? StatusCode, string Error)> TryCallApiAsync(
        string endpoint,
        string apiKey,
        string pdfPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            await using FileStream stream = File.OpenRead(pdfPath);
            using MultipartFormDataContent content = new();
            NameValueHeaderValue? boundaryHeader = content.Headers.ContentType?.Parameters.FirstOrDefault(parameter => parameter.Name == "boundary");
            if (boundaryHeader is not null && boundaryHeader.Value is not null)
            {
                boundaryHeader.Value = boundaryHeader.Value.Trim('"');
            }

            StreamContent fileContent = new(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"file\"",
                FileName = $"\"{Path.GetFileName(pdfPath)}\""
            };
            content.Add(fileContent, "file", Path.GetFileName(pdfPath));

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Add("x-api-key", apiKey);

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string payloadJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, payloadJson, (int)response.StatusCode, BuildError(response, payloadJson));
            }

            if (!LooksLikeJson(payloadJson))
            {
                payloadJson = JsonSerializer.Serialize(new
                {
                    raw = payloadJson,
                    warning = "Response was not JSON. Wrapped as raw payload."
                });
            }

            return (true, payloadJson, (int)response.StatusCode, string.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCR API call failed for {FileName}", Path.GetFileName(pdfPath));
            return (false, string.Empty, null, ex.Message);
        }
    }

    private static string BuildError(HttpResponseMessage response, string payload)
    {
        string detail = payload.Trim();
        if (detail.Length > 500)
        {
            detail = detail[..500];
        }

        return string.IsNullOrWhiteSpace(detail)
            ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {detail}";
    }

    private static bool LooksLikeJson(string value)
    {
        string trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }
}
