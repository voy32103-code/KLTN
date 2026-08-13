using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ReqSimulator.API.Models;

#nullable disable

namespace ReqSimulator.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:match_type", "exact,semantic,partial,missed")
                .Annotation("Npgsql:Enum:persona_difficulty", "easy,medium,hard")
                .Annotation("Npgsql:Enum:question_type", "open_ended,closed,clarifying,probing,leading,constraint_oriented,exception_oriented")
                .Annotation("Npgsql:Enum:requirement_category", "functional,non_functional,business_rule,constraint")
                .Annotation("Npgsql:Enum:sender_type", "student,stakeholder")
                .Annotation("Npgsql:Enum:user_role", "student,lecturer,admin");

            migrationBuilder.CreateTable(
                name: "persona_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    personality_traits = table.Column<string>(type: "jsonb", nullable: false),
                    communication_style = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    knowledge_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    difficulty = table.Column<PersonaDifficulty>(type: "persona_difficulty", nullable: false),
                    initial_mood = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    initial_patience = table.Column<decimal>(type: "numeric", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_system_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<UserRole>(type: "user_role", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    difficulty = table.Column<PersonaDifficulty>(type: "persona_difficulty", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    config_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    serialized_config = table.Column<string>(type: "text", nullable: true),
                    source_urls_data = table.Column<string>(type: "jsonb", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_scenarios_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "source_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expected_bytes = table.Column<long>(type: "bigint", nullable: false),
                    actual_bytes = table.Column<long>(type: "bigint", nullable: true),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_artifacts_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hidden_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_text = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<RequirementCategory>(type: "requirement_category", nullable: false),
                    reveal_difficulty = table.Column<PersonaDifficulty>(type: "persona_difficulty", nullable: false),
                    reveal_condition = table.Column<string>(type: "text", nullable: true),
                    gate_order = table.Column<int>(type: "integer", nullable: false),
                    actor = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    action = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    @object = table.Column<string>(name: "object", type: "character varying(240)", maxLength: 240, nullable: true),
                    condition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requirement_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    normalized_requirement_data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hidden_requirements", x => x.id);
                    table.ForeignKey(
                        name: "FK_hidden_requirements_scenarios_scenario_id",
                        column: x => x.scenario_id,
                        principalTable: "scenarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_review_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source_urls_data = table.Column<string>(type: "jsonb", nullable: false),
                    requirement_count = table.Column<int>(type: "integer", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_review_audits", x => x.id);
                    table.ForeignKey(
                        name: "FK_scenario_review_audits_scenarios_scenario_id",
                        column: x => x.scenario_id,
                        principalTable: "scenarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenario_review_audits_users_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stakeholders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stakeholders", x => x.id);
                    table.ForeignKey(
                        name: "FK_stakeholders_scenarios_scenario_id",
                        column: x => x.scenario_id,
                        principalTable: "scenarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_artifact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_urls_data = table.Column<string>(type: "jsonb", nullable: false),
                    selected_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    available_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    draft_data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ingestion_jobs_source_artifacts_source_artifact_id",
                        column: x => x.source_artifact_id,
                        principalTable: "source_artifacts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_ingestion_jobs_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "personas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stakeholder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    role_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    personality_traits = table.Column<string>(type: "jsonb", nullable: false),
                    communication_style = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    knowledge_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    difficulty = table.Column<PersonaDifficulty>(type: "persona_difficulty", nullable: false),
                    initial_mood = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    initial_patience = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personas", x => x.id);
                    table.ForeignKey(
                        name: "FK_personas_persona_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "persona_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_personas_scenarios_scenario_id",
                        column: x => x.scenario_id,
                        principalTable: "scenarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_personas_stakeholders_stakeholder_id",
                        column: x => x.stakeholder_id,
                        principalTable: "stakeholders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "simulation_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    persona_state = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    finalization_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    finalization_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finalization_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finalization_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_simulation_sessions_personas_persona_id",
                        column: x => x.persona_id,
                        principalTable: "personas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_simulation_sessions_scenarios_scenario_id",
                        column: x => x.scenario_id,
                        principalTable: "scenarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_simulation_sessions_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coverage_score = table.Column<decimal>(type: "numeric", nullable: true),
                    total_requirements = table.Column<int>(type: "integer", nullable: false),
                    matched_count = table.Column<int>(type: "integer", nullable: false),
                    partial_count = table.Column<int>(type: "integer", nullable: false),
                    missed_count = table.Column<int>(type: "integer", nullable: false),
                    feedback = table.Column<string>(type: "jsonb", nullable: true),
                    feedback_variant = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    overridden_coverage_score = table.Column<decimal>(type: "numeric", nullable: true),
                    overridden_by_lecturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    overridden_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_evaluation_results_simulation_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "simulation_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evaluation_results_users_overridden_by_lecturer_id",
                        column: x => x.overridden_by_lecturer_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "extracted_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_text = table.Column<string>(type: "text", nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric", nullable: true),
                    raw_requirement_data = table.Column<string>(type: "jsonb", nullable: true),
                    normalized_requirement_data = table.Column<string>(type: "jsonb", nullable: true),
                    normalization_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normalization_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    extracted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extracted_requirements", x => x.id);
                    table.ForeignKey(
                        name: "FK_extracted_requirements_simulation_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "simulation_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback_survey_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    helpfulness = table.Column<int>(type: "integer", nullable: false),
                    actionability = table.Column<int>(type: "integer", nullable: false),
                    no_answer_leak = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_survey_responses", x => x.id);
                    table.ForeignKey(
                        name: "FK_feedback_survey_responses_simulation_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "simulation_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_feedback_survey_responses_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender = table.Column<SenderType>(type: "sender_type", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    detected_question_type = table.Column<QuestionType>(type: "question_type", nullable: true),
                    detected_topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    question_quality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_simulation_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "simulation_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lecturer_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lecturer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_coverage_score = table.Column<decimal>(type: "numeric", nullable: true),
                    new_coverage_score = table.Column<decimal>(type: "numeric", nullable: true),
                    match_overrides = table.Column<string>(type: "jsonb", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    overridden_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lecturer_overrides", x => x.id);
                    table.ForeignKey(
                        name: "FK_lecturer_overrides_evaluation_results_evaluation_id",
                        column: x => x.evaluation_id,
                        principalTable: "evaluation_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lecturer_overrides_users_lecturer_id",
                        column: x => x.lecturer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirement_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hidden_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extracted_requirement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    similarity_score = table.Column<decimal>(type: "numeric", nullable: true),
                    match_type = table.Column<ReqSimulator.API.Models.MatchType>(type: "match_type", nullable: false),
                    overridden_match_type = table.Column<ReqSimulator.API.Models.MatchType>(type: "match_type", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_matches", x => x.id);
                    table.ForeignKey(
                        name: "FK_requirement_matches_evaluation_results_evaluation_id",
                        column: x => x.evaluation_id,
                        principalTable: "evaluation_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_matches_extracted_requirements_extracted_requir~",
                        column: x => x.extracted_requirement_id,
                        principalTable: "extracted_requirements",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_requirement_matches_hidden_requirements_hidden_requirement_~",
                        column: x => x.hidden_requirement_id,
                        principalTable: "hidden_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_results_overridden_by_lecturer_id",
                table: "evaluation_results",
                column: "overridden_by_lecturer_id");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_results_session_id",
                table: "evaluation_results",
                column: "session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_extracted_requirements_session_id",
                table: "extracted_requirements",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_survey_responses_student_id",
                table: "feedback_survey_responses",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "uq_feedback_survey_session",
                table: "feedback_survey_responses",
                column: "session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hidden_requirements_scenario_id",
                table: "hidden_requirements",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "idx_ingestion_jobs_artifact",
                table: "ingestion_jobs",
                column: "source_artifact_id");

            migrationBuilder.CreateIndex(
                name: "idx_ingestion_jobs_claim",
                table: "ingestion_jobs",
                columns: new[] { "status", "available_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_created_by_user_id",
                table: "ingestion_jobs",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "idx_lecturer_overrides_evaluation",
                table: "lecturer_overrides",
                column: "evaluation_id");

            migrationBuilder.CreateIndex(
                name: "IX_lecturer_overrides_lecturer_id",
                table: "lecturer_overrides",
                column: "lecturer_id");

            migrationBuilder.CreateIndex(
                name: "idx_messages_session_time",
                table: "messages",
                columns: new[] { "session_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_persona_templates_active_default",
                table: "persona_templates",
                columns: new[] { "is_active", "is_system_default" });

            migrationBuilder.CreateIndex(
                name: "uq_persona_templates_key",
                table: "persona_templates",
                column: "template_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_personas_scenario_id",
                table: "personas",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "IX_personas_stakeholder_id",
                table: "personas",
                column: "stakeholder_id");

            migrationBuilder.CreateIndex(
                name: "IX_personas_template_id",
                table: "personas",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_matches_evaluation_id",
                table: "requirement_matches",
                column: "evaluation_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_matches_extracted_requirement_id",
                table: "requirement_matches",
                column: "extracted_requirement_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_matches_hidden_requirement_id",
                table: "requirement_matches",
                column: "hidden_requirement_id");

            migrationBuilder.CreateIndex(
                name: "idx_scenario_review_audits_scenario_time",
                table: "scenario_review_audits",
                columns: new[] { "scenario_id", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_scenario_review_audits_reviewer_id",
                table: "scenario_review_audits",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "idx_scenarios_key_active",
                table: "scenarios",
                columns: new[] { "scenario_key", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_scenarios_reviewed_by_user_id",
                table: "scenarios",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_scenarios_key_version",
                table: "scenarios",
                columns: new[] { "scenario_key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_scenarios_one_active",
                table: "scenarios",
                column: "scenario_key",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "idx_sessions_finalization_state",
                table: "simulation_sessions",
                columns: new[] { "finalization_status", "finalization_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_sessions_persona_id",
                table: "simulation_sessions",
                column: "persona_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_sessions_scenario_id",
                table: "simulation_sessions",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_sessions_student_id",
                table: "simulation_sessions",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "IX_source_artifacts_created_by_user_id",
                table: "source_artifacts",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_stakeholders_scenario_name",
                table: "stakeholders",
                columns: new[] { "scenario_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_survey_responses");

            migrationBuilder.DropTable(
                name: "ingestion_jobs");

            migrationBuilder.DropTable(
                name: "lecturer_overrides");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "requirement_matches");

            migrationBuilder.DropTable(
                name: "scenario_review_audits");

            migrationBuilder.DropTable(
                name: "source_artifacts");

            migrationBuilder.DropTable(
                name: "evaluation_results");

            migrationBuilder.DropTable(
                name: "extracted_requirements");

            migrationBuilder.DropTable(
                name: "hidden_requirements");

            migrationBuilder.DropTable(
                name: "simulation_sessions");

            migrationBuilder.DropTable(
                name: "personas");

            migrationBuilder.DropTable(
                name: "persona_templates");

            migrationBuilder.DropTable(
                name: "stakeholders");

            migrationBuilder.DropTable(
                name: "scenarios");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
