using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Infrastructure.Services;

namespace PIT.Boletas.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddFolderBootstrapServices(this IServiceCollection services)
    {
        services.AddHttpClient();

        services.AddSingleton<IOperationalEventService, OperationalEventService>();
        services.AddSingleton<OneDriveScannerService>();
        services.AddSingleton<IOneDriveScannerService>(serviceProvider =>
            serviceProvider.GetRequiredService<OneDriveScannerService>());
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OneDriveScannerService>());
        services.AddSingleton<IOcrResultRepository, MySqlOcrResultRepository>();
        services.AddSingleton<LocalTesseractOcrService>();
        services.AddSingleton<LocalPaddleOcrService>();
        services.AddSingleton<ILocalOcrService>(serviceProvider =>
        {
            LocalOcrOptions options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalOcrOptions>>()
                .Value;

            return string.Equals(options.Engine, "Paddle", StringComparison.OrdinalIgnoreCase)
                ? serviceProvider.GetRequiredService<LocalPaddleOcrService>()
                : serviceProvider.GetRequiredService<LocalTesseractOcrService>();
        });
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
