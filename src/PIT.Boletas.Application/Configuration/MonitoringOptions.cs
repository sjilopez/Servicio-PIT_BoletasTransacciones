namespace PIT.Boletas.Application.Configuration;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int HeartbeatSeconds { get; set; } = 60;
}
