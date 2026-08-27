using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Infrastructure.Services;

namespace PIT.Boletas.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddFolderBootstrapServices(this IServiceCollection services)
    {
        services.AddHttpClient();

        services.AddSingleton<IOperationalEventService, OperationalEventService>();
        services.AddSingleton<IOcrResultRepository, MySqlOcrResultRepository>();
        services.AddSingleton<ILocalOcrService, LocalTesseractOcrService>();
        services.AddSingleton<IStartupFolderGuard, StartupFolderGuard>();
        services.AddSingleton<IStageOneIngestionService, StageOneIngestionService>();
        services.AddSingleton<IStageTwoValidationService, StageTwoValidationService>();
        services.AddSingleton<StageThreeOcrService>();
        services.AddSingleton<IStageThreeOcrService>(serviceProvider =>
            serviceProvider.GetRequiredService<StageThreeOcrService>());
        services.AddSingleton<IStageFourDbPendingService, StageFourDbPendingService>();
        services.AddSingleton<IStageFiveCompressionService, StageFiveCompressionService>();
        services.AddSingleton<IStageSixAzureFilesService, StageSixAzureFilesService>();
        services.AddSingleton<IStageSevenAzureBlobService, StageSevenAzureBlobService>();
        services.AddSingleton<IStageEightRetentionService, StageEightRetentionService>();
        return services;
    }
}
