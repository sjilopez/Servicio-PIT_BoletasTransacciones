namespace PIT.Boletas.Application.Configuration;

public sealed class AlertingOptions
{
    public const string SectionName = "Alerts";

    public bool Enabled { get; set; } = true;

    public bool ImmediateOnError { get; set; } = true;

    public string TeamsWebhook { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string SmtpUser { get; set; } = string.Empty;

    public string SmtpPassword { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;
}
