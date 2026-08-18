using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;

namespace PIT.Boletas.Worker.HostedServices;

public sealed class PipelineOrchestratorWorker(
    ILogger<PipelineOrchestratorWorker> logger,
    IStageOneIngestionService stageOneIngestionService,
    IStageTwoValidationService stageTwoValidationService,
    IStageThreeOcrService stageThreeOcrService,
    IStageFourDbPendingService stageFourDbPendingService,
    IStageFiveCompressionService stageFiveCompressionService,
    IStageSixAzureFilesService stageSixAzureFilesService,
    IStageSevenAzureBlobService stageSevenAzureBlobService,
    IStageEightRetentionService stageEightRetentionService,
    IOptions<IngestionOptions> ingestionOptions) : BackgroundService
{
    private readonly IngestionOptions _ingestionOptions = ingestionOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Pipeline orchestrator started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int stage1 = await stageOneIngestionService.ProcessPendingAsync(stoppingToken);
            int stage2 = await stageTwoValidationService.ProcessPendingAsync(stoppingToken);
            int stage3 = await stageThreeOcrService.ProcessPendingAsync(stoppingToken);
            int stage4 = await stageFourDbPendingService.ProcessPendingAsync(stoppingToken);
            int stage5 = await stageFiveCompressionService.ProcessPendingAsync(stoppingToken);
            int stage6 = await stageSixAzureFilesService.ProcessPendingAsync(stoppingToken);
            int stage7 = await stageSevenAzureBlobService.ProcessPendingAsync(stoppingToken);
            int stage8 = await stageEightRetentionService.ProcessPendingAsync(stoppingToken);

            int total = stage1 + stage2 + stage3 + stage4 + stage5 + stage6 + stage7 + stage8;

            if (total > 0)
            {
                logger.LogInformation(
                    "Cycle results | S1:{S1} S2:{S2} S3:{S3} S4:{S4} S5:{S5} S6:{S6} S7:{S7} S8:{S8}",
                    stage1,
                    stage2,
                    stage3,
                    stage4,
                    stage5,
                    stage6,
                    stage7,
                    stage8);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _ingestionOptions.PollIntervalSeconds)), stoppingToken);
        }

        logger.LogInformation("Pipeline orchestrator stopping.");
    }
}
