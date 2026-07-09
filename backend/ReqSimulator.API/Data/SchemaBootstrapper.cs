using Microsoft.EntityFrameworkCore;

namespace ReqSimulator.API.Data;

public static class SchemaBootstrapper
{
    public static async Task EnsureOperationalSchemaAsync(this IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE simulation_sessions
            ADD COLUMN IF NOT EXISTS finalization_status character varying(32) NOT NULL DEFAULT 'Idle';

            ALTER TABLE simulation_sessions
            ADD COLUMN IF NOT EXISTS finalization_lease_id uuid NULL;

            ALTER TABLE simulation_sessions
            ADD COLUMN IF NOT EXISTS finalization_started_at timestamp with time zone NULL;

            ALTER TABLE simulation_sessions
            ADD COLUMN IF NOT EXISTS finalization_expires_at timestamp with time zone NULL;
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS idx_sessions_finalization_state
                ON simulation_sessions (finalization_status, finalization_expires_at);
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
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
            """);

        logger.LogInformation("Ensured operational schema for session finalization recovery.");
    }
}
