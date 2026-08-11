using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Models;

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

        ALTER TABLE scenarios
        ADD COLUMN IF NOT EXISTS serialized_config text NULL;

        ALTER TABLE scenarios
        ADD COLUMN IF NOT EXISTS scenario_key character varying(100) NULL;

        ALTER TABLE scenarios
        ADD COLUMN IF NOT EXISTS config_hash character varying(64) NULL;

        ALTER TABLE scenarios
        ADD COLUMN IF NOT EXISTS published_at timestamp with time zone NOT NULL DEFAULT NOW();

        ALTER TABLE scenarios
        ADD COLUMN IF NOT EXISTS superseded_at timestamp with time zone NULL;

        ALTER TABLE scenarios
        ADD COLUMN IF NOT EXISTS source_urls_data jsonb NOT NULL DEFAULT '[]'::jsonb;

        CREATE TABLE IF NOT EXISTS stakeholders (
            id uuid PRIMARY KEY,
            scenario_id uuid NOT NULL REFERENCES scenarios(id) ON DELETE CASCADE,
            name character varying(100) NOT NULL,
            role_title character varying(100) NOT NULL,
            department character varying(100) NULL,
            description character varying(500) NULL,
            created_at timestamp with time zone NOT NULL DEFAULT NOW()
        );

        ALTER TABLE personas ADD COLUMN IF NOT EXISTS stakeholder_id uuid NULL;
        ALTER TABLE personas ADD COLUMN IF NOT EXISTS label character varying(100) NULL;

        DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_personas_stakeholder') THEN
                ALTER TABLE personas ADD CONSTRAINT fk_personas_stakeholder
                    FOREIGN KEY (stakeholder_id) REFERENCES stakeholders(id) ON DELETE SET NULL;
            END IF;
        END $$;

        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS actor character varying(160) NULL;
        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS action character varying(160) NULL;
        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS object character varying(240) NULL;
        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS condition character varying(500) NULL;
        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS requirement_type character varying(8) NULL;
        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS priority character varying(16) NULL;
        ALTER TABLE hidden_requirements ADD COLUMN IF NOT EXISTS normalized_requirement_data jsonb NULL;

        ALTER TABLE messages ADD COLUMN IF NOT EXISTS detected_topic character varying(100) NULL;
        ALTER TABLE messages ADD COLUMN IF NOT EXISTS question_quality character varying(20) NULL;

        ALTER TABLE extracted_requirements
        ADD COLUMN IF NOT EXISTS raw_requirement_data jsonb NULL;

        ALTER TABLE extracted_requirements
        ADD COLUMN IF NOT EXISTS normalized_requirement_data jsonb NULL;

        ALTER TABLE extracted_requirements
        ADD COLUMN IF NOT EXISTS normalization_status character varying(20) NOT NULL DEFAULT 'normalized';

        ALTER TABLE extracted_requirements
        ADD COLUMN IF NOT EXISTS normalization_method character varying(50) NOT NULL DEFAULT 'deterministic_dictionary';

        ALTER TABLE evaluation_results
        ADD COLUMN IF NOT EXISTS feedback_variant character varying(1) NOT NULL DEFAULT 'A';

        CREATE TABLE IF NOT EXISTS feedback_survey_responses (
            id uuid PRIMARY KEY,
            session_id uuid NOT NULL REFERENCES simulation_sessions(id) ON DELETE CASCADE,
            student_id uuid NOT NULL REFERENCES users(id),
            variant character varying(1) NOT NULL,
            helpfulness integer NOT NULL CHECK (helpfulness BETWEEN 1 AND 5),
            actionability integer NOT NULL CHECK (actionability BETWEEN 1 AND 5),
            no_answer_leak integer NOT NULL CHECK (no_answer_leak BETWEEN 1 AND 5),
            comment character varying(1000) NULL,
            submitted_at timestamp with time zone NOT NULL DEFAULT NOW()
        );

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

        UPDATE scenarios
        SET scenario_key = 'legacy_' || replace(id::text, '-', '')
        WHERE scenario_key IS NULL OR btrim(scenario_key) = '';

        ALTER TABLE scenarios
        ALTER COLUMN scenario_key SET NOT NULL;
        """;

    private const string CreateIndexSql = """
        CREATE INDEX IF NOT EXISTS idx_sessions_finalization_state
            ON simulation_sessions (finalization_status, finalization_expires_at);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_scenarios_key_version
            ON scenarios (scenario_key, version);

        CREATE INDEX IF NOT EXISTS idx_scenarios_key_active
            ON scenarios (scenario_key, is_active);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_scenarios_one_active
            ON scenarios (scenario_key) WHERE is_active;

        CREATE UNIQUE INDEX IF NOT EXISTS uq_stakeholders_scenario_name
            ON stakeholders (scenario_id, name);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_feedback_survey_session
            ON feedback_survey_responses (session_id);

        CREATE INDEX IF NOT EXISTS idx_ingestion_jobs_claim
            ON ingestion_jobs (status, available_at);

        CREATE INDEX IF NOT EXISTS idx_ingestion_jobs_artifact
            ON ingestion_jobs (source_artifact_id);
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
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM evaluation_results
                GROUP BY session_id
                HAVING COUNT(*) > 1
            ) THEN
                RAISE EXCEPTION
                    'Duplicate evaluation_results rows exist. Resolve them explicitly before enforcing uniqueness.';
            END IF;

            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'uq_evaluation_results_session_id'
            ) THEN
                ALTER TABLE evaluation_results
                ADD CONSTRAINT uq_evaluation_results_session_id UNIQUE (session_id);
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

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await BootstrapUserSeeder.SeedAsync(db, configuration, logger);

        logger.LogInformation("Ensured operational schema, scenario version indexes, and lecturer override tables.");
    }
}
