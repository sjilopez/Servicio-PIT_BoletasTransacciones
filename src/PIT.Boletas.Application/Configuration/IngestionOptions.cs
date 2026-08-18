namespace PIT.Boletas.Application.Configuration;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public int PollIntervalSeconds { get; set; } = 10;

    public int StabilizationChecks { get; set; } = 3;

    public int StabilizationDelayMilliseconds { get; set; } = 500;
}
