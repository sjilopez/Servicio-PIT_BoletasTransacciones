using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

namespace PIT.Boletas.Infrastructure.Services;

public sealed class MySqlOcrResultRepository(
    IConfiguration configuration,
    ILogger<MySqlOcrResultRepository> logger) : IOcrResultRepository
{
    private bool _schemaEnsured;

    public async Task<bool> TryInsertOcrJsonAsync(
        DocumentProcessingMetadata metadata,
        string rawJson,
        string source,
        CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetValue<string>("MySql:ConnectionString") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("MySQL connection string is empty. Keeping payload as pending.");
            return false;
        }

        try
        {
            await using MySqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken);

            if (!_schemaEnsured)
            {
                await EnsureSchemaAsync(connection, cancellationToken);
                _schemaEnsured = true;
            }

            const string sql = """
INSERT INTO ocr_result_log
(file_name, source_file_name, agency, user_name, host_name, host_ip, source_stage, created_utc, payload_sha256, payload_json)
VALUES (@file_name, @source_file_name, @agency, @user_name, @host_name, @host_ip, @source_stage, UTC_TIMESTAMP(3), @payload_sha256, @payload_json)
ON DUPLICATE KEY UPDATE id = id
""";

            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@file_name", metadata.FileName);
            cmd.Parameters.AddWithValue("@source_file_name", metadata.SourceFileName);
            cmd.Parameters.AddWithValue("@agency", metadata.Agency);
            cmd.Parameters.AddWithValue("@user_name", metadata.User);
            cmd.Parameters.AddWithValue("@host_name", metadata.HostName);
            cmd.Parameters.AddWithValue("@host_ip", metadata.HostIp);
            cmd.Parameters.AddWithValue("@source_stage", source);
            cmd.Parameters.AddWithValue("@payload_sha256", ComputeSha256(rawJson));
            cmd.Parameters.AddWithValue("@payload_json", rawJson);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MySQL insert failed for file {FileName}", metadata.FileName);
            return false;
        }
    }

    private static async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string ddl = """
CREATE TABLE IF NOT EXISTS ocr_result_log (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  file_name VARCHAR(255) NOT NULL,
  source_file_name VARCHAR(255) NOT NULL,
  agency VARCHAR(16) NOT NULL,
  user_name VARCHAR(120) NOT NULL,
  host_name VARCHAR(120) NOT NULL,
  host_ip VARCHAR(45) NULL,
  source_stage VARCHAR(40) NOT NULL,
  created_utc DATETIME(3) NOT NULL,
    payload_sha256 CHAR(64) NULL,
  payload_json LONGTEXT NOT NULL,
  INDEX idx_ocr_created_utc (created_utc),
  INDEX idx_ocr_agency_user (agency, user_name),
  INDEX idx_ocr_host (host_name)
);
""";

        await using MySqlCommand command = new(ddl, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        const string addHashColumn = """
ALTER TABLE ocr_result_log
ADD COLUMN IF NOT EXISTS payload_sha256 CHAR(64) NULL;
""";

        await using MySqlCommand addHashCommand = new(addHashColumn, connection);
        await addHashCommand.ExecuteNonQueryAsync(cancellationToken);

        const string uniqueIndex = """
CREATE UNIQUE INDEX uq_ocr_file_payload
ON ocr_result_log (file_name, payload_sha256);
""";

        try
        {
            await using MySqlCommand indexCommand = new(uniqueIndex, connection);
            await indexCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (MySqlException ex) when (ex.Number == 1061)
        {
            // The index already exists on a previously initialized database.
        }
    }

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
