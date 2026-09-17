using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Domain.Entities;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class OperationalEventService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<OperationalEventService> logger) : IOperationalEventService
{
    private bool _schemaEnsured;

    public async Task TrackAsync(
        string severity,
        string errorType,
        string code,
        string title,
        string description,
        string stage,
        DocumentProcessingMetadata? metadata,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        long? eventId = null;

        try
        {
            await using MySqlConnection? connection = await OpenConnectionAsync(cancellationToken);
            if (connection is not null)
            {
                if (!_schemaEnsured)
                {
                    await EnsureSchemaAsync(connection, cancellationToken);
                    _schemaEnsured = true;
                }

                const string sql = """
INSERT INTO error_event
(occurred_utc, severity, error_type, error_code, title, description, stage_name, correlation_id, agency, user_name, host_name, host_ip, remote_ip)
VALUES (NOW(3), @severity, @error_type, @error_code, @title, @description, @stage_name, @correlation_id, @agency, @user_name, @host_name, @host_ip, @remote_ip);
SELECT LAST_INSERT_ID();
""";

                await using MySqlCommand cmd = new(sql, connection);
                cmd.Parameters.AddWithValue("@severity", severity);
                cmd.Parameters.AddWithValue("@error_type", errorType);
                cmd.Parameters.AddWithValue("@error_code", code);
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@description", description);
                cmd.Parameters.AddWithValue("@stage_name", stage);
                cmd.Parameters.AddWithValue("@correlation_id", metadata?.FileName ?? string.Empty);
                cmd.Parameters.AddWithValue("@agency", metadata?.Agency ?? string.Empty);
                cmd.Parameters.AddWithValue("@user_name", metadata?.User ?? string.Empty);
                cmd.Parameters.AddWithValue("@host_name", metadata?.HostName ?? Environment.MachineName);
                cmd.Parameters.AddWithValue("@host_ip", metadata?.HostIp ?? ResolveHostIp());
                cmd.Parameters.AddWithValue("@remote_ip", remoteIp ?? string.Empty);

                object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                if (scalar is not null && long.TryParse(scalar.ToString(), out long parsed))
                {
                    eventId = parsed;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist operational event {Code}", code);
        }

        await DispatchAlertsIfNeededAsync(severity, code, title, description, eventId, cancellationToken);
    }

    public async Task TrackDuplicateAsync(
        DocumentProcessingMetadata metadata,
        string originalName,
        string duplicateName,
        int duplicateCounter,
        CancellationToken cancellationToken)
    {
        try
        {
            await using MySqlConnection? connection = await OpenConnectionAsync(cancellationToken);
            if (connection is null)
            {
                return;
            }

            if (!_schemaEnsured)
            {
                await EnsureSchemaAsync(connection, cancellationToken);
                _schemaEnsured = true;
            }

            const string sql = """
INSERT INTO duplicate_event
(created_utc, agency, user_name, host_name, host_ip, original_file_name, duplicate_file_name, duplicate_counter)
VALUES (NOW(3), @agency, @user_name, @host_name, @host_ip, @original_file_name, @duplicate_file_name, @duplicate_counter)
""";

            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@agency", metadata.Agency);
            cmd.Parameters.AddWithValue("@user_name", metadata.User);
            cmd.Parameters.AddWithValue("@host_name", metadata.HostName);
            cmd.Parameters.AddWithValue("@host_ip", metadata.HostIp);
            cmd.Parameters.AddWithValue("@original_file_name", originalName);
            cmd.Parameters.AddWithValue("@duplicate_file_name", duplicateName);
            cmd.Parameters.AddWithValue("@duplicate_counter", duplicateCounter);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist duplicate event for {DuplicateFile}", duplicateName);
        }
    }

    public async Task TrackHeartbeatAsync(string serviceName, string status, CancellationToken cancellationToken)
    {
        try
        {
            await using MySqlConnection? connection = await OpenConnectionAsync(cancellationToken);
            if (connection is null)
            {
                return;
            }

            if (!_schemaEnsured)
            {
                await EnsureSchemaAsync(connection, cancellationToken);
                _schemaEnsured = true;
            }

            const string sql = """
INSERT INTO service_heartbeat
(recorded_utc, service_name, host_name, host_ip, status)
VALUES (NOW(3), @service_name, @host_name, @host_ip, @status)
""";

            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@service_name", serviceName);
            cmd.Parameters.AddWithValue("@host_name", Environment.MachineName.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@host_ip", ResolveHostIp());
            cmd.Parameters.AddWithValue("@status", status);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Heartbeat store failed");
        }
    }

    private async Task DispatchAlertsIfNeededAsync(
        string severity,
        string code,
        string title,
        string description,
        long? errorEventId,
        CancellationToken cancellationToken)
    {
        AlertingOptions alerting = configuration.GetSection(AlertingOptions.SectionName).Get<AlertingOptions>() ?? new AlertingOptions();

        if (!alerting.Enabled || !alerting.ImmediateOnError)
        {
            return;
        }

        bool shouldSend = severity.Equals("error", StringComparison.OrdinalIgnoreCase)
                          || severity.Equals("critical", StringComparison.OrdinalIgnoreCase);
        if (!shouldSend)
        {
            return;
        }

        string message = $"[{severity.ToUpperInvariant()}] {code} - {title}\n{description}\nHost: {Environment.MachineName}";

        if (!string.IsNullOrWhiteSpace(alerting.TeamsWebhook))
        {
            await SendTeamsAsync(alerting.TeamsWebhook, message, errorEventId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(alerting.SmtpHost)
            && !string.IsNullOrWhiteSpace(alerting.From)
            && !string.IsNullOrWhiteSpace(alerting.To))
        {
            await SendEmailAsync(alerting, message, errorEventId, cancellationToken);
        }
    }

    private async Task SendTeamsAsync(string webhook, string message, long? errorEventId, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(new { text = message });

        try
        {
            using HttpClient client = httpClientFactory.CreateClient();
            using StringContent content = new(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(webhook, content, cancellationToken);

            await SaveAlertDispatchAsync(errorEventId, "teams", webhook, response.IsSuccessStatusCode ? "sent" : "failed", cancellationToken);
        }
        catch
        {
            await SaveAlertDispatchAsync(errorEventId, "teams", webhook, "failed", cancellationToken);
        }
    }

    private async Task SendEmailAsync(AlertingOptions alerting, string message, long? errorEventId, CancellationToken cancellationToken)
    {
        try
        {
            using SmtpClient smtp = new(alerting.SmtpHost, alerting.SmtpPort)
            {
                EnableSsl = alerting.UseSsl
            };

            if (!string.IsNullOrWhiteSpace(alerting.SmtpUser))
            {
                smtp.Credentials = new NetworkCredential(alerting.SmtpUser, alerting.SmtpPassword);
            }

            using MailMessage mail = new(alerting.From, alerting.To)
            {
                Subject = "PIT_BoletasTransacciones_v3.0 - Alerta critica",
                Body = message
            };

            await smtp.SendMailAsync(mail, cancellationToken);
            await SaveAlertDispatchAsync(errorEventId, "email", alerting.To, "sent", cancellationToken);
        }
        catch
        {
            await SaveAlertDispatchAsync(errorEventId, "email", alerting.To, "failed", cancellationToken);
        }
    }

    private async Task SaveAlertDispatchAsync(long? errorEventId, string channel, string destination, string status, CancellationToken cancellationToken)
    {
        try
        {
            await using MySqlConnection? connection = await OpenConnectionAsync(cancellationToken);
            if (connection is null)
            {
                return;
            }

            if (!_schemaEnsured)
            {
                await EnsureSchemaAsync(connection, cancellationToken);
                _schemaEnsured = true;
            }

            const string sql = """
INSERT INTO alert_dispatch
(sent_utc, error_event_id, channel, destination, status)
VALUES (NOW(3), @error_event_id, @channel, @destination, @status)
""";

            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@error_event_id", errorEventId ?? 0);
            cmd.Parameters.AddWithValue("@channel", channel);
            cmd.Parameters.AddWithValue("@destination", destination);
            cmd.Parameters.AddWithValue("@status", status);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Keep service resilient even if alert audit fails.
        }
    }

    private async Task<MySqlConnection?> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetValue<string>("MySql:ConnectionString") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string ddl = """
CREATE TABLE IF NOT EXISTS error_event (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  occurred_utc DATETIME(3) NOT NULL,
  severity VARCHAR(20) NOT NULL,
  error_type VARCHAR(50) NOT NULL,
  error_code VARCHAR(30) NOT NULL,
  title VARCHAR(180) NOT NULL,
  description TEXT NOT NULL,
  stage_name VARCHAR(40) NOT NULL,
  correlation_id VARCHAR(255) NULL,
  agency VARCHAR(16) NULL,
  user_name VARCHAR(120) NULL,
  host_name VARCHAR(120) NOT NULL,
  host_ip VARCHAR(45) NULL,
  remote_ip VARCHAR(45) NULL,
  INDEX idx_error_event_occured (occurred_utc),
  INDEX idx_error_event_severity (severity),
  INDEX idx_error_event_code (error_code)
);

CREATE TABLE IF NOT EXISTS duplicate_event (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  created_utc DATETIME(3) NOT NULL,
  agency VARCHAR(16) NOT NULL,
  user_name VARCHAR(120) NOT NULL,
  host_name VARCHAR(120) NOT NULL,
  host_ip VARCHAR(45) NULL,
  original_file_name VARCHAR(255) NOT NULL,
  duplicate_file_name VARCHAR(255) NOT NULL,
  duplicate_counter INT NOT NULL,
  INDEX idx_duplicate_event_created (created_utc)
);

CREATE TABLE IF NOT EXISTS service_heartbeat (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  recorded_utc DATETIME(3) NOT NULL,
  service_name VARCHAR(120) NOT NULL,
  host_name VARCHAR(120) NOT NULL,
  host_ip VARCHAR(45) NULL,
  status VARCHAR(30) NOT NULL,
  INDEX idx_service_heartbeat_recorded (recorded_utc)
);

CREATE TABLE IF NOT EXISTS alert_dispatch (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  sent_utc DATETIME(3) NOT NULL,
  error_event_id BIGINT NULL,
  channel VARCHAR(20) NOT NULL,
  destination VARCHAR(255) NOT NULL,
  status VARCHAR(20) NOT NULL,
  INDEX idx_alert_dispatch_sent (sent_utc)
);
""";

        await using MySqlCommand command = new(ddl, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResolveHostIp()
    {
        try
        {
            string host = Dns.GetHostName();
            IPAddress? address = Dns.GetHostAddresses(host)
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));

            return address?.ToString() ?? "N/A";
        }
        catch
        {
            return "N/A";
        }
    }
}
