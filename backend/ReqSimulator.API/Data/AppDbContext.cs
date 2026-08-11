using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Data;

/// <summary>
/// EF Core DbContext that maps application entities to PostgreSQL tables.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<Stakeholder> Stakeholders => Set<Stakeholder>();
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<HiddenRequirement> HiddenRequirements => Set<HiddenRequirement>();
    public DbSet<SimulationSession> SimulationSessions => Set<SimulationSession>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ExtractedRequirement> ExtractedRequirements => Set<ExtractedRequirement>();
    public DbSet<EvaluationResult> EvaluationResults => Set<EvaluationResult>();
    public DbSet<RequirementMatch> RequirementMatches => Set<RequirementMatch>();
    public DbSet<LecturerOverride> LecturerOverrides => Set<LecturerOverride>();
    public DbSet<FeedbackSurveyResponse> FeedbackSurveyResponses => Set<FeedbackSurveyResponse>();
    public DbSet<SourceArtifact> SourceArtifacts => Set<SourceArtifact>();
    public DbSet<IngestionJob> IngestionJobs => Set<IngestionJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<UserRole>("public", "user_role");
        modelBuilder.HasPostgresEnum<SenderType>("public", "sender_type");
        modelBuilder.HasPostgresEnum<RequirementCategory>("public", "requirement_category");
        modelBuilder.HasPostgresEnum<Models.MatchType>("public", "match_type");
        modelBuilder.HasPostgresEnum<QuestionType>("public", "question_type");
        modelBuilder.HasPostgresEnum<PersonaDifficulty>("public", "persona_difficulty");

        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<Scenario>().ToTable("scenarios");
        modelBuilder.Entity<Stakeholder>().ToTable("stakeholders");
        modelBuilder.Entity<Persona>().ToTable("personas");
        modelBuilder.Entity<HiddenRequirement>().ToTable("hidden_requirements");
        modelBuilder.Entity<SimulationSession>().ToTable("simulation_sessions");
        modelBuilder.Entity<Message>().ToTable("messages");
        modelBuilder.Entity<ExtractedRequirement>().ToTable("extracted_requirements");
        modelBuilder.Entity<EvaluationResult>().ToTable("evaluation_results");
        modelBuilder.Entity<RequirementMatch>().ToTable("requirement_matches");
        modelBuilder.Entity<LecturerOverride>().ToTable("lecturer_overrides");
        modelBuilder.Entity<FeedbackSurveyResponse>().ToTable("feedback_survey_responses");
        modelBuilder.Entity<SourceArtifact>().ToTable("source_artifacts");
        modelBuilder.Entity<IngestionJob>().ToTable("ingestion_jobs");

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasColumnType("user_role");
        modelBuilder.Entity<Scenario>()
            .Property(s => s.Difficulty)
            .HasColumnType("persona_difficulty");
        modelBuilder.Entity<Persona>()
            .Property(p => p.Difficulty)
            .HasColumnType("persona_difficulty");
        modelBuilder.Entity<Stakeholder>()
            .HasMany(s => s.Personas)
            .WithOne(p => p.Stakeholder)
            .HasForeignKey(p => p.StakeholderId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<HiddenRequirement>()
            .Property(r => r.Category)
            .HasColumnType("requirement_category");
        modelBuilder.Entity<HiddenRequirement>()
            .Property(r => r.RevealDifficulty)
            .HasColumnType("persona_difficulty");
        modelBuilder.Entity<Message>()
            .Property(m => m.Sender)
            .HasColumnType("sender_type");
        modelBuilder.Entity<Message>()
            .Property(m => m.DetectedQuestionType)
            .HasColumnType("question_type");
        modelBuilder.Entity<SimulationSession>()
            .Property(s => s.FinalizationStatus)
            .HasConversion<string>()
            .HasMaxLength(32);
        modelBuilder.Entity<RequirementMatch>()
            .Property(m => m.MatchType)
            .HasColumnType("match_type");
        modelBuilder.Entity<RequirementMatch>()
            .Property(m => m.OverriddenMatchType)
            .HasColumnType("match_type");

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.SessionId, m.Timestamp })
            .HasDatabaseName("idx_messages_session_time");

        modelBuilder.Entity<Stakeholder>()
            .HasIndex(s => new { s.ScenarioId, s.Name })
            .IsUnique()
            .HasDatabaseName("uq_stakeholders_scenario_name");

        modelBuilder.Entity<SimulationSession>()
            .HasIndex(s => new { s.FinalizationStatus, s.FinalizationExpiresAt })
            .HasDatabaseName("idx_sessions_finalization_state");

        modelBuilder.Entity<Scenario>()
            .HasIndex(s => new { s.ScenarioKey, s.Version })
            .IsUnique()
            .HasDatabaseName("uq_scenarios_key_version");

        modelBuilder.Entity<Scenario>()
            .HasIndex(s => new { s.ScenarioKey, s.IsActive })
            .HasDatabaseName("idx_scenarios_key_active");

        modelBuilder.Entity<Scenario>()
            .HasIndex(s => s.ScenarioKey)
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("uq_scenarios_one_active");

        modelBuilder.Entity<EvaluationResult>()
            .HasIndex(e => e.SessionId)
            .IsUnique();

        modelBuilder.Entity<LecturerOverride>()
            .HasIndex(o => o.EvaluationId)
            .HasDatabaseName("idx_lecturer_overrides_evaluation");
        modelBuilder.Entity<FeedbackSurveyResponse>()
            .HasIndex(item => item.SessionId)
            .IsUnique()
            .HasDatabaseName("uq_feedback_survey_session");
        modelBuilder.Entity<SourceArtifact>()
            .Property(item => item.Kind)
            .HasConversion<string>()
            .HasMaxLength(16);
        modelBuilder.Entity<IngestionJob>()
            .Property(item => item.SourceKind)
            .HasConversion<string>()
            .HasMaxLength(16);
        modelBuilder.Entity<IngestionJob>()
            .HasIndex(item => new { item.Status, item.AvailableAt })
            .HasDatabaseName("idx_ingestion_jobs_claim");
        modelBuilder.Entity<IngestionJob>()
            .HasIndex(item => item.SourceArtifactId)
            .HasDatabaseName("idx_ingestion_jobs_artifact");
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(
            name.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? "_" + c.ToString().ToLowerInvariant() : c.ToString().ToLowerInvariant()
            )
        );
    }
}

/// <summary>
/// Legacy helper kept for compatibility with earlier startup code.
/// </summary>
public static class NpgsqlEnumSetup
{
    public static void ConfigureEnums()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder();
        dataSourceBuilder.MapEnum<UserRole>("user_role");
        dataSourceBuilder.MapEnum<SenderType>("sender_type");
        dataSourceBuilder.MapEnum<RequirementCategory>("requirement_category");
        dataSourceBuilder.MapEnum<Models.MatchType>("match_type");
        dataSourceBuilder.MapEnum<QuestionType>("question_type");
        dataSourceBuilder.MapEnum<PersonaDifficulty>("persona_difficulty");
    }
}
