using Microsoft.EntityFrameworkCore;

namespace ReqSimulator.API.Data;

public static class SchemaBootstrapper
{
    private const string AddColumnsSql = """
        ALTER TABLE simulation_sessions
        ADD COLUMN IF NOT EXISTS finalization_status character varying(32) NOT NULL DEFAULT 'Idle';

        ALTER TABLE simulation_sessions
        ADD COLUMN IF NOT EXISTS finalization_lease_id uuid NULL;

        ALTER TABLE simulation_sessions
        ADD COLUMN IF NOT EXISTS finalization_started_at timestamp with time zone NULL;

        ALTER TABLE simulation_sessions
        ADD COLUMN IF NOT EXISTS finalization_expires_at timestamp with time zone NULL;
        """;

    private const string CreateIndexSql = """
        CREATE INDEX IF NOT EXISTS idx_sessions_finalization_state
            ON simulation_sessions (finalization_status, finalization_expires_at);
        """;

    private const string UpdateSessionsSql = """
        UPDATE simulation_sessions AS s
        SET finalization_status = CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM evaluation_results AS e
                    WHERE e.session_id = s.id
                ) THEN 'Completed'
                WHEN s.is_active THEN 'Idle'
                WHEN s.finalization_status = 'InProgress'
                     AND s.finalization_expires_at IS NOT NULL
                     AND s.finalization_expires_at > NOW() THEN 'InProgress'
                ELSE 'Failed'
            END,
            finalization_lease_id = CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM evaluation_results AS e
                    WHERE e.session_id = s.id
                ) THEN NULL
                WHEN s.finalization_status = 'InProgress'
                     AND s.finalization_expires_at IS NOT NULL
                     AND s.finalization_expires_at > NOW() THEN s.finalization_lease_id
                ELSE NULL
            END,
            finalization_started_at = CASE
                WHEN s.finalization_status = 'InProgress'
                     AND s.finalization_expires_at IS NOT NULL
                     AND s.finalization_expires_at > NOW() THEN COALESCE(s.finalization_started_at, s.ended_at, s.started_at)
                WHEN EXISTS (
                    SELECT 1
                    FROM evaluation_results AS e
                    WHERE e.session_id = s.id
                ) THEN COALESCE(s.finalization_started_at, s.ended_at, s.started_at)
                ELSE s.finalization_started_at
            END,
            finalization_expires_at = CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM evaluation_results AS e
                    WHERE e.session_id = s.id
                ) THEN NULL
                WHEN s.finalization_status = 'InProgress'
                     AND s.finalization_expires_at IS NOT NULL
                     AND s.finalization_expires_at > NOW() THEN s.finalization_expires_at
                ELSE NULL
            END;
        """;

    private const string CleanAndEnforceUniqueSql = """
        -- 1. Clean up duplicate requirement_matches associated with duplicate evaluation results
        DELETE FROM requirement_matches
        WHERE evaluation_id IN (
            SELECT id FROM (
                SELECT id, ROW_NUMBER() OVER (PARTITION BY session_id ORDER BY evaluated_at ASC) as rn
                FROM evaluation_results
            ) t
            WHERE t.rn > 1
        );

        -- 2. Clean up duplicate evaluation results, keeping only the earliest one per session
        DELETE FROM evaluation_results
        WHERE id IN (
            SELECT id FROM (
                SELECT id, ROW_NUMBER() OVER (PARTITION BY session_id ORDER BY evaluated_at ASC) as rn
                FROM evaluation_results
            ) t
            WHERE t.rn > 1
        );

        -- 3. Create UNIQUE constraint on session_id column in evaluation_results table
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'uq_evaluation_results_session_id'
            ) THEN
                ALTER TABLE evaluation_results ADD CONSTRAINT uq_evaluation_results_session_id UNIQUE (session_id);
            END IF;
        END $$;
        """;

    private const string LecturerOverrideSchemaSql = """
        -- Add lecturer override columns to evaluation_results
        ALTER TABLE evaluation_results
        ADD COLUMN IF NOT EXISTS overridden_coverage_score numeric NULL;

        ALTER TABLE evaluation_results
        ADD COLUMN IF NOT EXISTS overridden_by_lecturer_id uuid NULL;

        ALTER TABLE evaluation_results
        ADD COLUMN IF NOT EXISTS overridden_at timestamp with time zone NULL;

        -- Add lecturer override column to requirement_matches
        ALTER TABLE requirement_matches
        ADD COLUMN IF NOT EXISTS overridden_match_type match_type NULL;

        -- Create lecturer_overrides audit trail table
        CREATE TABLE IF NOT EXISTS lecturer_overrides (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            evaluation_id uuid NOT NULL REFERENCES evaluation_results(id) ON DELETE CASCADE,
            lecturer_id uuid NOT NULL REFERENCES users(id),
            original_coverage_score numeric NULL,
            new_coverage_score numeric NULL,
            match_overrides jsonb NULL,
            comment character varying(1000) NULL,
            overridden_at timestamp with time zone NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_lecturer_overrides_evaluation
            ON lecturer_overrides (evaluation_id);
        """;

    public static async Task EnsureOperationalSchemaAsync(this IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(AddColumnsSql);
        await db.Database.ExecuteSqlRawAsync(CreateIndexSql);
        await db.Database.ExecuteSqlRawAsync(UpdateSessionsSql);
        await db.Database.ExecuteSqlRawAsync(CleanAndEnforceUniqueSql);
        await db.Database.ExecuteSqlRawAsync(LecturerOverrideSchemaSql);

        logger.LogInformation("Ensured operational schema, unique constraint, and lecturer override tables.");
    }
}
