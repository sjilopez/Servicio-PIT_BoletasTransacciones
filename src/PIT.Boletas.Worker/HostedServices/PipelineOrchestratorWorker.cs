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
            int stage1 = await ExecuteStageAsync("S1", stageOneIngestionService.ProcessPendingAsync, stoppingToken);
            int stage2 = await ExecuteStageAsync("S2", stageTwoValidationService.ProcessPendingAsync, stoppingToken);
            int stage3 = await ExecuteStageAsync("S3", stageThreeOcrService.ProcessPendingAsync, stoppingToken);
            int stage4 = await ExecuteStageAsync("S4", stageFourDbPendingService.ProcessPendingAsync, stoppingToken);
            int stage5 = await ExecuteStageAsync("S5", stageSixAzureFilesService.ProcessPendingAsync, stoppingToken);
            int stage6 = await ExecuteStageAsync("S6", stageFiveCompressionService.ProcessPendingAsync, stoppingToken);
            int stage7 = await ExecuteStageAsync("S7", stageSevenAzureBlobService.ProcessPendingAsync, stoppingToken);
            int stage8 = await ExecuteStageAsync("S8", stageEightRetentionService.ProcessPendingAsync, stoppingToken);

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

            Task delayTask = Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, _ingestionOptions.ReconciliationIntervalSeconds)),
                stoppingToken);
            Task activityTask = stageOneIngestionService.WaitForActivityAsync(
                TimeSpan.FromSeconds(Math.Max(1, _ingestionOptions.ReconciliationIntervalSeconds)),
                stoppingToken);
            await Task.WhenAny(delayTask, activityTask);
        }

        logger.LogInformation("Pipeline orchestrator stopping.");
    }

    private async Task<int> ExecuteStageAsync(
        string stageName,
        Func<CancellationToken, Task<int>> processStage,
        CancellationToken stoppingToken)
    {
        try
        {
            return await processStage(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline stage {StageName} failed. The next stages and cycle will continue.", stageName);
            return 0;
        }
    }
}
