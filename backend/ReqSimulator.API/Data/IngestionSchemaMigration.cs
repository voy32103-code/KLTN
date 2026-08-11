using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ReqSimulator.API.Data;

/// <summary>Applies the ingestion queue schema exactly once per Neon database.</summary>
public static class IngestionSchemaMigration
{
    private const string Version = "20260811_ingestion_queue_v1";
    private const long AdvisoryLockKey = 80411291;

    private const string Sql = """
        CREATE TABLE IF NOT EXISTS source_artifacts (
            id uuid PRIMARY KEY,
            created_by_user_id uuid NOT NULL REFERENCES users(id),
            kind character varying(16) NOT NULL,
            original_file_name character varying(255) NOT NULL,
            content_type character varying(128) NOT NULL,
            expected_bytes bigint NOT NULL,
            actual_bytes bigint NULL,
            object_key character varying(512) NOT NULL UNIQUE,
            status character varying(32) NOT NULL,
            created_at timestamp with time zone NOT NULL DEFAULT NOW(),
            expires_at timestamp with time zone NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ingestion_jobs (
            id uuid PRIMARY KEY,
            created_by_user_id uuid NOT NULL REFERENCES users(id),
            source_artifact_id uuid NULL REFERENCES source_artifacts(id),
            source_kind character varying(16) NOT NULL,
            source_urls_data jsonb NOT NULL DEFAULT '[]'::jsonb,
            selected_model character varying(100) NULL,
            status character varying(32) NOT NULL,
            attempts integer NOT NULL DEFAULT 0,
            max_attempts integer NOT NULL DEFAULT 3,
            lease_id uuid NULL,
            lease_expires_at timestamp with time zone NULL,
            available_at timestamp with time zone NOT NULL DEFAULT NOW(),
            error_code character varying(80) NULL,
            draft_data jsonb NULL,
            created_at timestamp with time zone NOT NULL DEFAULT NOW(),
            updated_at timestamp with time zone NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_ingestion_jobs_claim
            ON ingestion_jobs (status, available_at);
        CREATE INDEX IF NOT EXISTS idx_ingestion_jobs_artifact
            ON ingestion_jobs (source_artifact_id);
        """;

    public static async Task ApplyAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var connection = db.Database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();

            await ExecuteAsync(connection, dbTransaction, $"SELECT pg_advisory_xact_lock({AdvisoryLockKey});", cancellationToken);
            await ExecuteAsync(connection, dbTransaction, """
                CREATE TABLE IF NOT EXISTS application_schema_migrations (
                    version character varying(100) PRIMARY KEY,
                    applied_at timestamp with time zone NOT NULL DEFAULT NOW()
                );
                """, cancellationToken);

            var applied = await ScalarAsync<bool>(connection, dbTransaction,
                "SELECT EXISTS (SELECT 1 FROM application_schema_migrations WHERE version = @version);",
                Version,
                cancellationToken);
            if (!applied)
            {
                await ExecuteAsync(connection, dbTransaction, Sql, cancellationToken);
                await ExecuteAsync(connection, dbTransaction,
                    "INSERT INTO application_schema_migrations (version) VALUES (@version);",
                    cancellationToken,
                    Version);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken cancellationToken, string? version = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (version is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "version";
            parameter.Value = version;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(DbConnection connection, DbTransaction transaction, string sql, string version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "version";
        parameter.Value = version;
        command.Parameters.Add(parameter);
        return (T)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Migration version check returned no value."));
    }
}
