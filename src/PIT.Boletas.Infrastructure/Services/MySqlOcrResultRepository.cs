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
(file_name, source_file_name, agency, user_name, host_name, host_ip, source_stage, created_utc,
 original_creation_time_local, ingested_utc, api_ocr_succeeded, external_ocr_last_status_code,
 external_ocr_last_error, last_api_attempt_utc, last_db_attempt_utc, document_type,
 classification_confidence, ocr_route, requires_azure_blob, azure_files_uploaded,
 last_azure_files_attempt_utc, azure_blob_uploaded, last_azure_blob_attempt_utc, payload_sha256, payload_json)
VALUES (@file_name, @source_file_name, @agency, @user_name, @host_name, @host_ip, @source_stage, NOW(3),
 @original_creation_time_local, @ingested_utc, @api_ocr_succeeded, @external_ocr_last_status_code,
 @external_ocr_last_error, @last_api_attempt_utc, @last_db_attempt_utc, @document_type,
 @classification_confidence, @ocr_route, @requires_azure_blob, @azure_files_uploaded,
 @last_azure_files_attempt_utc, @azure_blob_uploaded, @last_azure_blob_attempt_utc, @payload_sha256, @payload_json)
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
            cmd.Parameters.AddWithValue("@original_creation_time_local", metadata.OriginalCreationTimeLocal);
            cmd.Parameters.AddWithValue("@ingested_utc", metadata.IngestedUtc);
            cmd.Parameters.AddWithValue("@api_ocr_succeeded", metadata.ApiOcrSucceeded);
            cmd.Parameters.AddWithValue("@external_ocr_last_status_code", metadata.ExternalOcrLastStatusCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@external_ocr_last_error", metadata.ExternalOcrLastError);
            cmd.Parameters.AddWithValue("@last_api_attempt_utc", metadata.LastApiAttemptUtc ?? (object)DBNull.Value);
            AddClassificationParameters(cmd, metadata);
            cmd.Parameters.AddWithValue("@azure_files_uploaded", metadata.AzureFilesUploaded);
            cmd.Parameters.AddWithValue("@last_azure_files_attempt_utc", metadata.LastAzureFilesAttemptUtc ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@azure_blob_uploaded", metadata.AzureBlobUploaded);
            cmd.Parameters.AddWithValue("@last_azure_blob_attempt_utc", metadata.LastAzureBlobAttemptUtc ?? (object)DBNull.Value);
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

    public async Task<bool> TryUpdateMetadataAsync(
        DocumentProcessingMetadata metadata,
        CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetValue<string>("MySql:ConnectionString") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
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
UPDATE ocr_result_log
SET original_creation_time_local = @original_creation_time_local,
    ingested_utc = @ingested_utc,
    api_ocr_succeeded = @api_ocr_succeeded,
    external_ocr_last_status_code = @external_ocr_last_status_code,
    external_ocr_last_error = @external_ocr_last_error,
    last_api_attempt_utc = @last_api_attempt_utc,
    last_db_attempt_utc = @last_db_attempt_utc,
    document_type = @document_type,
    classification_confidence = @classification_confidence,
    ocr_route = @ocr_route,
    requires_azure_blob = @requires_azure_blob,
    azure_files_uploaded = @azure_files_uploaded,
    last_azure_files_attempt_utc = @last_azure_files_attempt_utc,
    azure_blob_uploaded = @azure_blob_uploaded,
    last_azure_blob_attempt_utc = @last_azure_blob_attempt_utc
WHERE file_name = @file_name AND source_file_name = @source_file_name
""";

            await using MySqlCommand cmd = new(sql, connection);
            AddMetadataParameters(cmd, metadata);
            int affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MySQL metadata update failed for file {FileName}", metadata.FileName);
            return false;
        }
    }

    private async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
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
    original_creation_time_local DATETIME(3) NOT NULL,
    ingested_utc DATETIME(3) NOT NULL,
    api_ocr_succeeded TINYINT(1) NOT NULL,
    external_ocr_last_status_code INT NULL,
    external_ocr_last_error VARCHAR(1000) NOT NULL,
    last_api_attempt_utc DATETIME(3) NULL,
    last_db_attempt_utc DATETIME(3) NULL,
    document_type VARCHAR(120) NOT NULL DEFAULT '',
    classification_confidence DOUBLE NOT NULL DEFAULT 0,
    ocr_route VARCHAR(40) NOT NULL DEFAULT '',
    requires_azure_blob TINYINT(1) NOT NULL DEFAULT 0,
    azure_files_uploaded TINYINT(1) NOT NULL,
    last_azure_files_attempt_utc DATETIME(3) NULL,
    azure_blob_uploaded TINYINT(1) NOT NULL,
    last_azure_blob_attempt_utc DATETIME(3) NULL,
    payload_sha256 CHAR(64) NULL,
  payload_json LONGTEXT NOT NULL,
  INDEX idx_ocr_created_utc (created_utc),
  INDEX idx_ocr_agency_user (agency, user_name),
  INDEX idx_ocr_host (host_name)
);
""";

        await using MySqlCommand command = new(ddl, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "last_db_attempt_utc", "DATETIME(3) NULL", cancellationToken);
        await EnsureColumnAsync(connection, "document_type", "VARCHAR(120) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "classification_confidence", "DOUBLE NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "ocr_route", "VARCHAR(40) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "requires_azure_blob", "TINYINT(1) NOT NULL DEFAULT 0", cancellationToken);

        const string hashColumnExistsSql = """
    SELECT COUNT(*)
    FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'ocr_result_log'
      AND column_name = 'payload_sha256';
    """;

        await using MySqlCommand hashColumnExistsCommand = new(hashColumnExistsSql, connection);
        object? hashColumnExists = await hashColumnExistsCommand.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(hashColumnExists) == 0)
        {
            const string addHashColumn = """
    ALTER TABLE ocr_result_log
    ADD COLUMN payload_sha256 CHAR(64) NULL;
    """;

            await using MySqlCommand addHashCommand = new(addHashColumn, connection);
            await addHashCommand.ExecuteNonQueryAsync(cancellationToken);
        }

    }

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AddMetadataParameters(MySqlCommand cmd, DocumentProcessingMetadata metadata)
    {
        cmd.Parameters.AddWithValue("@file_name", metadata.FileName);
        cmd.Parameters.AddWithValue("@source_file_name", metadata.SourceFileName);
        cmd.Parameters.AddWithValue("@original_creation_time_local", metadata.OriginalCreationTimeLocal);
        cmd.Parameters.AddWithValue("@ingested_utc", metadata.IngestedUtc);
        cmd.Parameters.AddWithValue("@api_ocr_succeeded", metadata.ApiOcrSucceeded);
        cmd.Parameters.AddWithValue("@external_ocr_last_status_code", metadata.ExternalOcrLastStatusCode ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@external_ocr_last_error", metadata.ExternalOcrLastError);
        cmd.Parameters.AddWithValue("@last_api_attempt_utc", metadata.LastApiAttemptUtc ?? (object)DBNull.Value);
        AddClassificationParameters(cmd, metadata);
        cmd.Parameters.AddWithValue("@azure_files_uploaded", metadata.AzureFilesUploaded);
        cmd.Parameters.AddWithValue("@last_azure_files_attempt_utc", metadata.LastAzureFilesAttemptUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@azure_blob_uploaded", metadata.AzureBlobUploaded);
        cmd.Parameters.AddWithValue("@last_azure_blob_attempt_utc", metadata.LastAzureBlobAttemptUtc ?? (object)DBNull.Value);
    }

    private static void AddClassificationParameters(MySqlCommand cmd, DocumentProcessingMetadata metadata)
    {
        cmd.Parameters.AddWithValue("@last_db_attempt_utc", metadata.LastDbAttemptUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@document_type", metadata.DocumentType);
        cmd.Parameters.AddWithValue("@classification_confidence", metadata.ClassificationConfidence);
        cmd.Parameters.AddWithValue("@ocr_route", metadata.OcrRoute);
        cmd.Parameters.AddWithValue("@requires_azure_blob", metadata.RequiresAzureBlob);
    }

    private static async Task EnsureColumnAsync(
        MySqlConnection connection,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT COUNT(*)
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'ocr_result_log'
  AND column_name = @column_name;
""";

        await using MySqlCommand existsCommand = new(sql, connection);
        existsCommand.Parameters.AddWithValue("@column_name", columnName);
        object? exists = await existsCommand.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(exists) == 0)
        {
            await using MySqlCommand addCommand = new(
                $"ALTER TABLE ocr_result_log ADD COLUMN {columnName} {definition};",
                connection);
            await addCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
