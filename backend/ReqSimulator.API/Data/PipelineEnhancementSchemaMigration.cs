using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ReqSimulator.API.Data;

/// <summary>
/// Versioned Neon migration for reusable persona templates and reviewable ground
/// truth publication. It deliberately avoids the legacy startup bootstrap DDL.
/// </summary>
public static class PipelineEnhancementSchemaMigration
{
    private const string Version = "20260811_pipeline_enhancements_v1";
    private const long AdvisoryLockKey = 80411292;

    private const string Sql = """
        CREATE TABLE IF NOT EXISTS persona_templates (
            id uuid PRIMARY KEY,
            template_key character varying(80) NOT NULL UNIQUE,
            label character varying(100) NOT NULL,
            personality_traits jsonb NOT NULL DEFAULT '{}'::jsonb,
            communication_style character varying(50) NOT NULL,
            knowledge_level character varying(50) NOT NULL,
            difficulty persona_difficulty NOT NULL,
            initial_mood character varying(50) NOT NULL,
            initial_patience numeric NOT NULL,
            is_active boolean NOT NULL DEFAULT TRUE,
            is_system_default boolean NOT NULL DEFAULT FALSE,
            created_at timestamp with time zone NOT NULL DEFAULT NOW(),
            updated_at timestamp with time zone NOT NULL DEFAULT NOW()
        );

        ALTER TABLE personas ADD COLUMN IF NOT EXISTS template_id uuid NULL;
        DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_personas_template') THEN
                ALTER TABLE personas ADD CONSTRAINT fk_personas_template
                    FOREIGN KEY (template_id) REFERENCES persona_templates(id) ON DELETE SET NULL;
            END IF;
        END $$;

        ALTER TABLE scenarios ADD COLUMN IF NOT EXISTS reviewed_by_user_id uuid NULL;
        ALTER TABLE scenarios ADD COLUMN IF NOT EXISTS reviewed_at timestamp with time zone NULL;
        ALTER TABLE scenarios ADD COLUMN IF NOT EXISTS review_notes character varying(1000) NULL;
        DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_scenarios_reviewed_by_user') THEN
                ALTER TABLE scenarios ADD CONSTRAINT fk_scenarios_reviewed_by_user
                    FOREIGN KEY (reviewed_by_user_id) REFERENCES users(id) ON DELETE SET NULL;
            END IF;
        END $$;

        CREATE TABLE IF NOT EXISTS scenario_review_audits (
            id uuid PRIMARY KEY,
            scenario_id uuid NOT NULL REFERENCES scenarios(id) ON DELETE CASCADE,
            reviewer_id uuid NOT NULL REFERENCES users(id),
            notes character varying(1000) NULL,
            source_urls_data jsonb NOT NULL DEFAULT '[]'::jsonb,
            requirement_count integer NOT NULL,
            reviewed_at timestamp with time zone NOT NULL DEFAULT NOW()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_persona_templates_key
            ON persona_templates (template_key);
        CREATE INDEX IF NOT EXISTS idx_persona_templates_active_default
            ON persona_templates (is_active, is_system_default);
        CREATE INDEX IF NOT EXISTS idx_scenario_review_audits_scenario_time
            ON scenario_review_audits (scenario_id, reviewed_at DESC);

        INSERT INTO persona_templates
            (id, template_key, label, personality_traits, communication_style, knowledge_level,
             difficulty, initial_mood, initial_patience, is_active, is_system_default, created_at, updated_at)
        VALUES
            ('11111111-1111-4111-8111-111111111111', 'collaborative', 'Hợp tác',
             '{"traits":["collaborative","detail_oriented"]}'::jsonb, 'collaborative', 'high',
             'easy', 'neutral', 1.00, TRUE, TRUE, NOW(), NOW()),
            ('22222222-2222-4222-8222-222222222222', 'challenging', 'Phản biện',
             '{"traits":["challenging","detail_oriented"]}'::jsonb, 'concise', 'medium',
             'hard', 'neutral', 0.70, TRUE, TRUE, NOW(), NOW())
        ON CONFLICT (template_key) DO NOTHING;
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

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        string? version = null)
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

    private static async Task<T> ScalarAsync<T>(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "version";
        parameter.Value = version;
        command.Parameters.Add(parameter);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Migration version check returned no value."));
    }
}
