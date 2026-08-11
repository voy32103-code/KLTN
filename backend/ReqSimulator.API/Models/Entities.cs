using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReqSimulator.API.Models;

// ===== ENUMS (khớp với PostgreSQL ENUM đã tạo trong DB) =====

public enum UserRole { Student, Lecturer, Admin }
public enum SenderType { Student, Stakeholder }
public enum RequirementCategory { Functional, NonFunctional, BusinessRule, Constraint }
public enum MatchType { Exact, Semantic, Partial, Missed }
public enum QuestionType { OpenEnded, Closed, Clarifying, Probing, Leading, ConstraintOriented, ExceptionOriented }
public enum PersonaDifficulty { Easy, Medium, Hard }
public enum SessionFinalizationStatus { Idle, InProgress, Completed, Failed }
public enum IngestionSourceKind { Url, Audio }

// ===== ENTITIES =====

/// <summary>Người dùng hệ thống (student / lecturer / admin)</summary>
public class User
{
    [Key] public Guid Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(255)] public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Student;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SimulationSession> Sessions { get; set; } = [];
}

/// <summary>Business scenario cho simulation (VD: University Registration)</summary>
public class Scenario
{
    [Key] public Guid Id { get; set; }
    [MaxLength(100)] public string ScenarioKey { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    [MaxLength(100)] public string? Domain { get; set; }
    public PersonaDifficulty Difficulty { get; set; } = PersonaDifficulty.Medium;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SupersededAt { get; set; }
    [MaxLength(64)] public string? ConfigHash { get; set; }
    public string? SerializedConfig { get; set; }
    [Column(TypeName = "jsonb")] public string SourceUrlsData { get; set; } = "[]";

    public ICollection<Stakeholder> Stakeholders { get; set; } = [];
    public ICollection<Persona> Personas { get; set; } = [];
    public ICollection<HiddenRequirement> HiddenRequirements { get; set; } = [];
}

/// <summary>Private R2 object used as an ingestion source. It is never exposed as a public URL.</summary>
public class SourceArtifact
{
    [Key] public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public IngestionSourceKind Kind { get; set; }
    [MaxLength(255)] public string OriginalFileName { get; set; } = "";
    [MaxLength(128)] public string ContentType { get; set; } = "";
    public long ExpectedBytes { get; set; }
    public long? ActualBytes { get; set; }
    [MaxLength(512)] public string ObjectKey { get; set; } = "";
    [MaxLength(32)] public string Status { get; set; } = "AwaitingUpload";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    [ForeignKey(nameof(CreatedByUserId))] public User CreatedByUser { get; set; } = null!;
}

/// <summary>Durable, lease-based ingestion queue backed by PostgreSQL.</summary>
public class IngestionJob
{
    [Key] public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? SourceArtifactId { get; set; }
    public IngestionSourceKind SourceKind { get; set; }
    [Column(TypeName = "jsonb")] public string SourceUrlsData { get; set; } = "[]";
    [MaxLength(100)] public string? SelectedModel { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    [MaxLength(80)] public string? ErrorCode { get; set; }
    [Column(TypeName = "jsonb")] public string? DraftData { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey(nameof(CreatedByUserId))] public User CreatedByUser { get; set; } = null!;
    [ForeignKey(nameof(SourceArtifactId))] public SourceArtifact? SourceArtifact { get; set; }
}

/// <summary>Vai trò nghiệp vụ trong scenario; một stakeholder có nhiều persona.</summary>
public class Stakeholder
{
    [Key] public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string RoleTitle { get; set; } = "";
    [MaxLength(100)] public string? Department { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ScenarioId))] public Scenario Scenario { get; set; } = null!;
    public ICollection<Persona> Personas { get; set; } = [];
}

/// <summary>Virtual stakeholder profile với personality traits</summary>
public class Persona
{
    [Key] public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public Guid? StakeholderId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Label { get; set; }
    [MaxLength(100)] public string? RoleTitle { get; set; }

    /// <summary>JSONB: {"traits": ["impatient", "organized"], "jargon_level": "high"}</summary>
    [Column(TypeName = "jsonb")] public string PersonalityTraits { get; set; } = "{}";

    [MaxLength(50)] public string? CommunicationStyle { get; set; }
    [MaxLength(50)] public string? KnowledgeLevel { get; set; }
    public PersonaDifficulty Difficulty { get; set; } = PersonaDifficulty.Medium;
    [MaxLength(50)] public string InitialMood { get; set; } = "neutral";
    public decimal InitialPatience { get; set; } = 1.00m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ScenarioId))] public Scenario Scenario { get; set; } = null!;
    [ForeignKey(nameof(StakeholderId))] public Stakeholder? Stakeholder { get; set; }
}

/// <summary>Ground-truth requirement (ẩn với student, dùng để đánh giá coverage)</summary>
public class HiddenRequirement
{
    [Key] public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public string RequirementText { get; set; } = "";
    public RequirementCategory Category { get; set; }
    public PersonaDifficulty RevealDifficulty { get; set; } = PersonaDifficulty.Medium;
    public string? RevealCondition { get; set; }
    public int GateOrder { get; set; } = 0;
    [MaxLength(160)] public string? Actor { get; set; }
    [MaxLength(160)] public string? Action { get; set; }
    [MaxLength(240)] public string? Object { get; set; }
    [MaxLength(500)] public string? Condition { get; set; }
    [MaxLength(8)] public string? RequirementType { get; set; }
    [MaxLength(16)] public string? Priority { get; set; }
    [Column(TypeName = "jsonb")] public string? NormalizedRequirementData { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ScenarioId))] public Scenario Scenario { get; set; } = null!;
}

/// <summary>Một phiên interview giữa student và AI stakeholder</summary>
public class SimulationSession
{
    [Key] public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Guid ScenarioId { get; set; }
    public Guid PersonaId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    /// <summary>JSONB: Persona State Machine snapshot (mood, patience, revealed_set)</summary>
    [Column(TypeName = "jsonb")] public string? PersonaState { get; set; }

    public bool IsActive { get; set; } = true;
    public SessionFinalizationStatus FinalizationStatus { get; set; } = SessionFinalizationStatus.Idle;
    public Guid? FinalizationLeaseId { get; set; }
    public DateTime? FinalizationStartedAt { get; set; }
    public DateTime? FinalizationExpiresAt { get; set; }

    [ForeignKey(nameof(StudentId))] public User Student { get; set; } = null!;
    [ForeignKey(nameof(ScenarioId))] public Scenario Scenario { get; set; } = null!;
    [ForeignKey(nameof(PersonaId))] public Persona Persona { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<ExtractedRequirement> ExtractedRequirements { get; set; } = [];
    public EvaluationResult? EvaluationResult { get; set; }
}

/// <summary>Tin nhắn trong cuộc interview</summary>
public class Message
{
    [Key] public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public SenderType Sender { get; set; }
    public string Content { get; set; } = "";
    public QuestionType? DetectedQuestionType { get; set; }
    [MaxLength(100)] public string? DetectedTopic { get; set; }
    [MaxLength(20)] public string? QuestionQuality { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SessionId))] public SimulationSession Session { get; set; } = null!;
}

/// <summary>Requirement được trích xuất từ conversation</summary>
public class ExtractedRequirement
{
    [Key] public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string RequirementText { get; set; } = "";
    public decimal? ConfidenceScore { get; set; }
    /// <summary>Original structured extraction returned by the AI service.</summary>
    [Column(TypeName = "jsonb")] public string? RawRequirementData { get; set; }
    /// <summary>Canonical fields/key used for deterministic matching.</summary>
    [Column(TypeName = "jsonb")] public string? NormalizedRequirementData { get; set; }
    [MaxLength(20)] public string NormalizationStatus { get; set; } = "normalized";
    [MaxLength(50)] public string NormalizationMethod { get; set; } = "deterministic_dictionary";
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SessionId))] public SimulationSession Session { get; set; } = null!;
}

/// <summary>Kết quả đánh giá coverage cho một session</summary>
public class EvaluationResult
{
    [Key] public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public decimal? CoverageScore { get; set; }
    public int TotalRequirements { get; set; }
    public int MatchedCount { get; set; }
    public int PartialCount { get; set; }
    public int MissedCount { get; set; }

    /// <summary>JSONB: {"strengths": [...], "weaknesses": [...], "suggestions": [...]}</summary>
    [Column(TypeName = "jsonb")] public string? Feedback { get; set; }
    [MaxLength(1)] public string FeedbackVariant { get; set; } = "A";

    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    // === Lecturer override fields ===
    /// <summary>Coverage score sau khi giảng viên chỉnh sửa matchType (auto-recalculated)</summary>
    public decimal? OverriddenCoverageScore { get; set; }
    public Guid? OverriddenByLecturerId { get; set; }
    public DateTime? OverriddenAt { get; set; }

    [ForeignKey(nameof(SessionId))] public SimulationSession Session { get; set; } = null!;
    [ForeignKey(nameof(OverriddenByLecturerId))] public User? OverriddenByLecturer { get; set; }
    public ICollection<RequirementMatch> Matches { get; set; } = [];
    public ICollection<LecturerOverride> LecturerOverrides { get; set; } = [];
}

/// <summary>Human outcome used to compare feedback experiment variants.</summary>
public class FeedbackSurveyResponse
{
    [Key] public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid StudentId { get; set; }
    [MaxLength(1)] public string Variant { get; set; } = "A";
    public int Helpfulness { get; set; }
    public int Actionability { get; set; }
    public int NoAnswerLeak { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SessionId))] public SimulationSession Session { get; set; } = null!;
    [ForeignKey(nameof(StudentId))] public User Student { get; set; } = null!;
}

/// <summary>Chi tiết match giữa extracted requirement và hidden requirement</summary>
public class RequirementMatch
{
    [Key] public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public Guid HiddenRequirementId { get; set; }
    public Guid? ExtractedRequirementId { get; set; }
    public decimal? SimilarityScore { get; set; }
    public MatchType MatchType { get; set; } = MatchType.Missed;

    // === Lecturer override fields ===
    /// <summary>MatchType sau khi giảng viên chỉnh sửa (null = chưa chỉnh)</summary>
    public MatchType? OverriddenMatchType { get; set; }

    [ForeignKey(nameof(EvaluationId))] public EvaluationResult Evaluation { get; set; } = null!;
    [ForeignKey(nameof(HiddenRequirementId))] public HiddenRequirement HiddenRequirement { get; set; } = null!;
    [ForeignKey(nameof(ExtractedRequirementId))] public ExtractedRequirement? ExtractedRequirement { get; set; }
}

/// <summary>Bản ghi audit trail khi giảng viên chỉnh sửa kết quả đánh giá AI</summary>
public class LecturerOverride
{
    [Key] public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public Guid LecturerId { get; set; }

    /// <summary>Coverage score AI gốc trước khi chỉnh</summary>
    public decimal? OriginalCoverageScore { get; set; }
    /// <summary>Coverage score mới sau khi tính lại từ matchType đã chỉnh</summary>
    public decimal? NewCoverageScore { get; set; }

    /// <summary>JSONB: [{matchId, originalMatchType, newMatchType}]</summary>
    [Column(TypeName = "jsonb")] public string? MatchOverrides { get; set; }

    [MaxLength(1000)] public string? Comment { get; set; }
    public DateTime OverriddenAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(EvaluationId))] public EvaluationResult Evaluation { get; set; } = null!;
    [ForeignKey(nameof(LecturerId))] public User Lecturer { get; set; } = null!;
}
