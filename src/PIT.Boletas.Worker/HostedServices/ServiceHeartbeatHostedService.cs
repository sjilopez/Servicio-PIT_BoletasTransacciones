using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;

namespace PIT.Boletas.Worker.HostedServices;

public sealed class ServiceHeartbeatHostedService(
    IOperationalEventService operationalEventService,
    IOptions<MonitoringOptions> monitoringOptions,
    IConfiguration configuration,
    ILogger<ServiceHeartbeatHostedService> logger) : BackgroundService
{
    private readonly MonitoringOptions _monitoring = monitoringOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string serviceName = configuration.GetValue<string>("Service:Name") ?? "PIT_BoletasTransacciones";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await operationalEventService.TrackHeartbeatAsync(serviceName, "running", stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Heartbeat write failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _monitoring.HeartbeatSeconds)), stoppingToken);
        }
    }
}
