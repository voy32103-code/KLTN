using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using ReqSimulator.API.Services;
using RequirementMatchType = ReqSimulator.API.Models.MatchType;
using SessionFinalizationState = ReqSimulator.API.Models.SessionFinalizationStatus;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AiServiceClient _ai;
    private readonly ILogger<SessionsController> _logger;
    private static readonly TimeSpan FinalizationLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan EvaluationWaitTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan EvaluationPollInterval = TimeSpan.FromSeconds(1);

    private sealed class PersonaStateSnapshot
    {
        [JsonPropertyName("mood")]
        public string Mood { get; set; } = "neutral_busy";

        [JsonPropertyName("patience")]
        public decimal Patience { get; set; } = 1.00m;

        [JsonPropertyName("revealed_requirements")]
        public List<string> RevealedRequirements { get; set; } = [];

        [JsonPropertyName("turn_count")]
        public int TurnCount { get; set; }

        [JsonPropertyName("selected_model")]
        public string? SelectedModel { get; set; }
    }

    public record CreateSessionDto(Guid ScenarioId, Guid PersonaId, string? SelectedModel);
    public record SendMessageDto([Required, StringLength(4000)] string Content);
    public record FeedbackSurveyDto(
        [Range(1, 5)] int Helpfulness,
        [Range(1, 5)] int Actionability,
        [Range(1, 5)] int NoAnswerLeak,
        [StringLength(1000)] string? Comment);

    private record RequirementMatchReport(
        string MatchId,
        string HiddenId,
        string? HiddenText,
        string? ExtractedText,
        decimal Score,
        string MatchType,
        string Reason,
        string? OverriddenMatchType);

    private sealed record FinalizationLeaseClaim(Guid LeaseId, DateTime StartedAt, DateTime ExpiresAt);
    private sealed record FinalizationLeaseSnapshot(SessionFinalizationState Status, DateTime? ExpiresAt);

    public SessionsController(
        AppDbContext db,
        AiServiceClient ai,
        ILogger<SessionsController>? logger = null)
    {
        _db = db;
        _ai = ai;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionsController>.Instance;
    }

    private Guid? GetCurrentUserId()
    {
        var rawUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private bool IsPrivilegedUser() =>
        User.IsInRole(UserRole.Lecturer.ToString()) || User.IsInRole(UserRole.Admin.ToString());

    private bool CanAccessSession(SimulationSession session, Guid currentUserId) =>
        session.StudentId == currentUserId || IsPrivilegedUser();

    private async Task<object?> TryGetExistingEvaluationResponse(Guid sessionId)
    {
        var existingEvaluation = await _db.EvaluationResults
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.SessionId == sessionId);

        if (existingEvaluation is null)
            return null;

        var extractedCount = await _db.ExtractedRequirements
            .AsNoTracking()
            .CountAsync(r => r.SessionId == sessionId);
        var matches = await LoadRequirementMatchReports(existingEvaluation.Id);

        return ToEvaluationResponse(
            existingEvaluation,
            DeserializeFeedback(existingEvaluation.Feedback),
            extractedCount,
            matches);
    }

    private static FinalizationLeaseClaim CreateFinalizationLease()
    {
        var startedAt = DateTime.UtcNow;
        return new FinalizationLeaseClaim(
            Guid.NewGuid(),
            startedAt,
            startedAt.Add(FinalizationLeaseDuration));
    }

    private async Task<FinalizationLeaseSnapshot?> GetFinalizationLeaseState(Guid sessionId)
    {
        return await _db.SimulationSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new FinalizationLeaseSnapshot(
                s.FinalizationStatus,
                s.FinalizationExpiresAt))
            .FirstOrDefaultAsync();
    }

    private async Task<bool> TryClaimFinalizationLease(Guid sessionId, FinalizationLeaseClaim lease)
    {
        var claimed = await _db.SimulationSessions
            .Where(s => s.Id == sessionId &&
                (s.FinalizationStatus == SessionFinalizationState.Idle ||
                 s.FinalizationStatus == SessionFinalizationState.Failed ||
                 (s.FinalizationStatus == SessionFinalizationState.InProgress &&
                  s.FinalizationExpiresAt != null &&
                  s.FinalizationExpiresAt <= lease.StartedAt)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.FinalizationStatus, SessionFinalizationState.InProgress)
                .SetProperty(s => s.FinalizationLeaseId, lease.LeaseId)
                .SetProperty(s => s.FinalizationStartedAt, lease.StartedAt)
                .SetProperty(s => s.FinalizationExpiresAt, lease.ExpiresAt)
                .SetProperty(s => s.IsActive, false)
                .SetProperty(s => s.EndedAt, lease.StartedAt));

        return claimed > 0;
    }

    private async Task TryMarkFinalizationFailed(Guid sessionId, FinalizationLeaseClaim lease)
    {
        try
        {
            await _db.SimulationSessions
                .Where(s => s.Id == sessionId &&
                    s.FinalizationStatus == SessionFinalizationState.InProgress &&
                    s.FinalizationLeaseId == lease.LeaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.FinalizationStatus, SessionFinalizationState.Failed)
                    .SetProperty(s => s.FinalizationLeaseId, (Guid?)null)
                    .SetProperty(s => s.FinalizationExpiresAt, (DateTime?)null));
        }
        catch
        {
            // Preserve the original error path if status cleanup also fails.
        }
    }

    private async Task<bool> TryMarkFinalizationCompleted(Guid sessionId, FinalizationLeaseClaim lease)
    {
        var completed = await _db.SimulationSessions
            .Where(s => s.Id == sessionId &&
                s.FinalizationStatus == SessionFinalizationState.InProgress &&
                s.FinalizationLeaseId == lease.LeaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.FinalizationStatus, SessionFinalizationState.Completed)
                .SetProperty(s => s.FinalizationLeaseId, (Guid?)null)
                .SetProperty(s => s.FinalizationExpiresAt, (DateTime?)null)
                .SetProperty(s => s.IsActive, false)
                .SetProperty(s => s.EndedAt, lease.StartedAt));

        return completed > 0;
    }

    private async Task<object?> WaitForExistingEvaluationResponse(Guid sessionId)
    {
        var deadline = DateTime.UtcNow.Add(EvaluationWaitTimeout);

        while (DateTime.UtcNow < deadline)
        {
            var existingResponse = await TryGetExistingEvaluationResponse(sessionId);
            if (existingResponse is not null)
                return existingResponse;

            var leaseState = await GetFinalizationLeaseState(sessionId);
            if (leaseState is null)
                return null;

            if (leaseState.Status is SessionFinalizationState.Idle or SessionFinalizationState.Failed)
                return null;

            if (leaseState.Status == SessionFinalizationState.InProgress &&
                (!leaseState.ExpiresAt.HasValue || leaseState.ExpiresAt <= DateTime.UtcNow))
            {
                return null;
            }

            await Task.Delay(EvaluationPollInterval);
        }

        return await TryGetExistingEvaluationResponse(sessionId);
    }

    [HttpGet("review")]
    [Authorize(Roles = "Lecturer,Admin")]
    public async Task<IActionResult> ReviewSessions([FromQuery] int limit = 50)
    {
        var safeLimit = Math.Clamp(limit, 1, 200);

        var sessionRows = await _db.SimulationSessions
            .AsNoTracking()
            .OrderByDescending(s => s.StartedAt)
            .Take(safeLimit)
            .Select(s => new
            {
                s.Id,
                s.StartedAt,
                s.EndedAt,
                s.IsActive,
                s.FinalizationStatus,
                Student = new
                {
                    s.Student.Id,
                    s.Student.Name,
                    s.Student.Email
                },
                Scenario = new
                {
                    s.Scenario.Id,
                    s.Scenario.Title,
                    s.Scenario.Domain,
                    s.Scenario.Difficulty
                },
                Persona = new
                {
                    s.Persona.Id,
                    s.Persona.Name,
                    s.Persona.RoleTitle
                },
                MessageCount = s.Messages.Count,
                StudentTurnCount = s.Messages.Count(m => m.Sender == SenderType.Student),
                Evaluation = s.EvaluationResult == null
                    ? null
                    : new
                    {
                        s.EvaluationResult.Id,
                        s.EvaluationResult.CoverageScore,
                        s.EvaluationResult.MatchedCount,
                        s.EvaluationResult.PartialCount,
                        s.EvaluationResult.MissedCount,
                        s.EvaluationResult.TotalRequirements,
                        s.EvaluationResult.EvaluatedAt
                    }
            })
            .ToListAsync();

        var sessions = sessionRows.Select(s => new
        {
            s.Id,
            s.StartedAt,
            s.EndedAt,
            s.IsActive,
            FinalizationStatus = s.FinalizationStatus.ToString(),
            s.Student,
            Scenario = new
            {
                s.Scenario.Id,
                s.Scenario.Title,
                s.Scenario.Domain,
                Difficulty = s.Scenario.Difficulty.ToString()
            },
            s.Persona,
            s.MessageCount,
            s.StudentTurnCount,
            s.Evaluation
        }).ToList();

        return Ok(sessions);
    }

    [HttpGet("review/{sessionId}")]
    [Authorize(Roles = "Lecturer,Admin")]
    public async Task<IActionResult> ReviewSessionDetail(Guid sessionId)
    {
        var sessionRow = await _db.SimulationSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new
            {
                s.Id,
                s.StartedAt,
                s.EndedAt,
                s.IsActive,
                s.FinalizationStatus,
                Student = new
                {
                    s.Student.Id,
                    s.Student.Name,
                    s.Student.Email
                },
                Scenario = new
                {
                    s.Scenario.Id,
                    s.Scenario.Title,
                    s.Scenario.Description,
                    s.Scenario.Domain,
                    s.Scenario.Difficulty
                },
                Persona = new
                {
                    s.Persona.Id,
                    s.Persona.Name,
                    s.Persona.RoleTitle,
                    s.Persona.CommunicationStyle,
                    s.Persona.KnowledgeLevel
                }
            })
            .FirstOrDefaultAsync();

        if (sessionRow is null)
            return NotFound();

        var session = new
        {
            sessionRow.Id,
            sessionRow.StartedAt,
            sessionRow.EndedAt,
            sessionRow.IsActive,
            FinalizationStatus = sessionRow.FinalizationStatus.ToString(),
            sessionRow.Student,
            Scenario = new
            {
                sessionRow.Scenario.Id,
                sessionRow.Scenario.Title,
                sessionRow.Scenario.Description,
                sessionRow.Scenario.Domain,
                Difficulty = sessionRow.Scenario.Difficulty.ToString()
            },
            sessionRow.Persona
        };

        var messageRows = await _db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                m.Sender,
                m.Content,
                m.DetectedQuestionType,
                m.DetectedTopic,
                m.QuestionQuality,
                m.Timestamp
            })
            .ToListAsync();

        var messages = messageRows.Select(m => new
        {
            Sender = m.Sender.ToString(),
            m.Content,
            DetectedQuestionType = m.DetectedQuestionType?.ToString(),
            m.DetectedTopic,
            m.QuestionQuality,
            m.Timestamp
        }).ToList();

        var extractedRequirements = await _db.ExtractedRequirements
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.ConfidenceScore)
            .ThenBy(r => r.RequirementText)
            .Select(r => new
            {
                r.Id,
                r.RequirementText,
                r.ConfidenceScore,
                r.RawRequirementData,
                r.NormalizedRequirementData,
                r.ExtractedAt
            })
            .ToListAsync();

        var hiddenRequirementRows = await _db.HiddenRequirements
            .AsNoTracking()
            .Where(r => r.ScenarioId == session.Scenario.Id)
            .OrderBy(r => r.GateOrder)
            .ThenBy(r => r.RequirementText)
            .Select(r => new
            {
                r.Id,
                r.RequirementText,
                r.Category,
                r.RevealDifficulty,
                r.RevealCondition,
                r.GateOrder
            })
            .ToListAsync();

        var hiddenRequirements = hiddenRequirementRows.Select(r => new
        {
            r.Id,
            r.RequirementText,
            Category = r.Category.ToString(),
            RevealDifficulty = r.RevealDifficulty.ToString(),
            r.RevealCondition,
            r.GateOrder
        }).ToList();

        var evaluation = await _db.EvaluationResults
            .AsNoTracking()
            .Include(e => e.OverriddenByLecturer)
            .FirstOrDefaultAsync(e => e.SessionId == sessionId);

        object? evaluationResponse = null;
        if (evaluation is not null)
        {
            var matches = await LoadRequirementMatchReports(evaluation.Id);
            evaluationResponse = ToEvaluationResponse(
                evaluation,
                DeserializeFeedback(evaluation.Feedback),
                extractedRequirements.Count,
                matches);
        }

        return Ok(new
        {
            Session = session,
            Messages = messages,
            ExtractedRequirements = extractedRequirements,
            HiddenRequirements = hiddenRequirements,
            Evaluation = evaluationResponse
        });
    }

    public record MatchOverrideItemDto(Guid MatchId, string NewMatchType);
    public record LecturerOverrideDto(List<MatchOverrideItemDto> MatchOverrides, string? Comment);

    /// <summary>
    /// Giảng viên hoặc Admin chỉnh sửa MatchType của các yêu cầu và tự động tính lại CoverageScore
    /// </summary>
    [HttpPut("review/{sessionId:guid}/override")]
    [Authorize(Roles = "Lecturer,Admin")]
    public async Task<IActionResult> OverrideSessionEvaluation(Guid sessionId, [FromBody] LecturerOverrideDto dto)
    {
        var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserIdClaim, out var lecturerId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính giảng viên." });
        }

        var evaluation = await _db.EvaluationResults
            .Include(e => e.Matches)
            .FirstOrDefaultAsync(e => e.SessionId == sessionId);

        if (evaluation is null)
        {
            return NotFound(new { message = "Chưa có kết quả đánh giá cho phiên này." });
        }

        if (dto.MatchOverrides == null || dto.MatchOverrides.Count == 0)
        {
            return BadRequest(new { message = "Danh sách chỉnh sửa không được để trống." });
        }

        var matchDict = evaluation.Matches.ToDictionary(m => m.Id);
        var auditLogs = new List<object>();

        foreach (var item in dto.MatchOverrides)
        {
            if (!matchDict.TryGetValue(item.MatchId, out var match))
            {
                continue;
            }

            if (Enum.TryParse<RequirementMatchType>(item.NewMatchType, true, out var newType))
            {
                var originalType = match.OverriddenMatchType ?? match.MatchType;
                match.OverriddenMatchType = newType;
                auditLogs.Add(new
                {
                    matchId = match.Id,
                    hiddenRequirementId = match.HiddenRequirementId,
                    originalMatchType = originalType.ToString(),
                    newMatchType = newType.ToString()
                });
            }
        }

        // Tự động tính lại MatchedCount, PartialCount, MissedCount & OverriddenCoverageScore (Option A)
        int matched = 0, partial = 0, missed = 0;
        foreach (var m in evaluation.Matches)
        {
            var effectiveType = m.OverriddenMatchType ?? m.MatchType;
            switch (effectiveType)
            {
                case RequirementMatchType.Exact:
                case RequirementMatchType.Semantic:
                    matched++;
                    break;
                case RequirementMatchType.Partial:
                    partial++;
                    break;
                default:
                    missed++;
                    break;
            }
        }

        int total = evaluation.TotalRequirements > 0 ? evaluation.TotalRequirements : evaluation.Matches.Count;
        decimal newCoverage = total > 0
            ? Math.Round(((decimal)matched + (decimal)partial * 0.5m) / total * 100m, 2)
            : 0m;

        var originalScore = evaluation.OverriddenCoverageScore ?? evaluation.CoverageScore;

        evaluation.OverriddenCoverageScore = newCoverage;
        evaluation.OverriddenByLecturerId = lecturerId;
        evaluation.OverriddenAt = DateTime.UtcNow;

        var lecturerOverrideRecord = new LecturerOverride
        {
            Id = Guid.NewGuid(),
            EvaluationId = evaluation.Id,
            LecturerId = lecturerId,
            OriginalCoverageScore = originalScore,
            NewCoverageScore = newCoverage,
            MatchOverrides = JsonSerializer.Serialize(auditLogs),
            Comment = dto.Comment,
            OverriddenAt = DateTime.UtcNow
        };

        _db.LecturerOverrides.Add(lecturerOverrideRecord);
        await _db.SaveChangesAsync();

        var updatedMatches = await LoadRequirementMatchReports(evaluation.Id);
        var lecturer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == lecturerId);
        evaluation.OverriddenByLecturer = lecturer;

        return Ok(ToEvaluationResponse(
            evaluation,
            DeserializeFeedback(evaluation.Feedback),
            evaluation.Matches.Count,
            updatedMatches));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSessionDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        if (dto.ScenarioId == Guid.Empty || dto.PersonaId == Guid.Empty)
            return BadRequest("Thiếu thông tin ScenarioId hoặc PersonaId.");

        var scenario = await _db.Scenarios
            .Include(s => s.Personas)
            .FirstOrDefaultAsync(s => s.Id == dto.ScenarioId && s.IsActive);

        if (scenario is null)
            return NotFound("Kịch bản không tồn tại hoặc đã bị ẩn.");

        var persona = scenario.Personas.FirstOrDefault(p => p.Id == dto.PersonaId);
        if (persona is null)
            return BadRequest("Stakeholder không thuộc kịch bản đã chọn.");

        if (!AiModelCatalog.IsSupported(dto.SelectedModel))
            return BadRequest("Mô hình AI không được hỗ trợ.");

        var selectedModel = AiModelCatalog.NormalizeOrDefault(dto.SelectedModel);
        var session = new SimulationSession
        {
            StudentId = userId.Value,
            ScenarioId = dto.ScenarioId,
            PersonaId = dto.PersonaId,
            PersonaState = JsonSerializer.Serialize(new PersonaStateSnapshot
            {
                Mood = persona.InitialMood,
                Patience = persona.InitialPatience,
                RevealedRequirements = [],
                TurnCount = 0,
                SelectedModel = selectedModel
            })
        };

        _db.SimulationSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new { session.Id, session.StartedAt, SelectedModel = selectedModel });
    }

    [HttpPost("{sessionId}/messages")]
    [EnableRateLimiting("ai_expensive")]
    public async Task<IActionResult> SendMessage(Guid sessionId, [FromBody] SendMessageDto dto)
    {
        var rawUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(rawUserId, out var parsedUserId)) return Unauthorized();

        var content = dto.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest("Nội dung tin nhắn không được để trống.");

        // 1. Tải thông tin ban đầu không khóa và nằm ngoài transaction
        var sessionInit = await _db.SimulationSessions
            .AsNoTracking()
            .Include(s => s.Persona)
            .Include(s => s.Scenario)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (sessionInit is null) return NotFound();

        if (sessionInit.StudentId != parsedUserId) return Forbid();
        if (!sessionInit.IsActive) return BadRequest("Phiên phỏng vấn này đã kết thúc.");

        // Lấy lịch sử chat hiện tại để truyền cho AI
        var messagesHistory = await _db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new ChatMessage(m.Sender.ToString(), m.Content, m.Timestamp))
            .ToListAsync();
        var historyMessageCount = messagesHistory.Count;

        var personaStateInit = DeserializePersonaState(sessionInit.PersonaState, sessionInit.Persona);

        var hiddenRequirements = await _db.HiddenRequirements
            .AsNoTracking()
            .Where(r => r.ScenarioId == sessionInit.ScenarioId)
            .ToListAsync();

        ScenarioConfigJson? scenarioConfig = null;
        if (!string.IsNullOrEmpty(sessionInit.Scenario.SerializedConfig))
        {
            try
            {
                scenarioConfig = JsonSerializer.Deserialize<ScenarioConfigJson>(
                    sessionInit.Scenario.SerializedConfig
                );
            }
            catch
            {
                // Ignore deserialization error, will fallback to local file config in AI Service
            }
        }

        // 2. Thực hiện cuộc gọi HTTP API ngoại mạng (được chạy bên ngoài transaction)
        var aiResponse = await _ai.Chat(new AiChatRequest(
            SessionId: sessionId.ToString(),
            ScenarioTitle: sessionInit.Scenario.Title,
            StudentMessage: content,
            History: messagesHistory,
            Persona: new PersonaProfile(
                sessionInit.Persona.Name,
                sessionInit.Persona.RoleTitle ?? "",
                sessionInit.Persona.PersonalityTraits,
                sessionInit.Persona.CommunicationStyle ?? "neutral",
                personaStateInit.Mood,
                personaStateInit.Patience),
            PersonaStateJson: sessionInit.PersonaState,
            AvailableRequirements: hiddenRequirements.Select(r => r.RequirementText).ToList(),
            SelectedModel: personaStateInit.SelectedModel,
            ScenarioConfig: scenarioConfig
        ));

        if (aiResponse.IsFallback)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = aiResponse.StakeholderReply,
                isFallback = true
            });
        }

        // 3. Mở transaction cục bộ ngắn và khóa dòng FOR UPDATE để lưu kết quả nhanh
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var session = await _db.SimulationSessions
            .FromSqlInterpolated($"SELECT * FROM simulation_sessions WHERE id = {sessionId} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (session is null) return NotFound();
        await _db.Entry(session).Reference(s => s.Persona).LoadAsync();
        if (!session.IsActive) return BadRequest("Phiên phỏng vấn này đã kết thúc.");

        var currentMessageCount = await _db.Messages.CountAsync(m => m.SessionId == sessionId);
        if (currentMessageCount != historyMessageCount)
        {
            return Conflict(new
            {
                message = "Phiên làm việc đã thay đổi trong quá trình xử lý tin nhắn. Vui lòng tải lại trang và thử lại."
            });
        }

        var studentMsg = new Message
        {
            SessionId = sessionId,
            Sender = SenderType.Student,
            Content = content
        };
        _db.Messages.Add(studentMsg);

        var stakeholderMsg = new Message
        {
            SessionId = sessionId,
            Sender = SenderType.Stakeholder,
            Content = aiResponse.StakeholderReply
        };
        _db.Messages.Add(stakeholderMsg);

        if (Enum.TryParse<QuestionType>(aiResponse.DetectedQuestionType, true, out var questionType))
            studentMsg.DetectedQuestionType = questionType;
        studentMsg.DetectedTopic = aiResponse.DetectedTopic;
        studentMsg.QuestionQuality = aiResponse.QuestionQuality;

        if (aiResponse.StateUpdate is not null)
        {
            var personaState = DeserializePersonaState(session.PersonaState, session.Persona);
            personaState.Mood = aiResponse.StateUpdate.Mood;
            personaState.Patience = aiResponse.StateUpdate.Patience;
            personaState.TurnCount = aiResponse.StateUpdate.TurnCount;

            foreach (var requirement in aiResponse.StateUpdate.NewlyRevealed)
            {
                if (!personaState.RevealedRequirements.Contains(requirement, StringComparer.OrdinalIgnoreCase))
                    personaState.RevealedRequirements.Add(requirement);
            }

            session.PersonaState = JsonSerializer.Serialize(personaState);
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            reply = aiResponse.StakeholderReply,
            questionType = aiResponse.DetectedQuestionType,
            topic = aiResponse.DetectedTopic,
            questionQuality = aiResponse.QuestionQuality,
            // Revealed requirement texts are internal ground truth. Persist them
            // server-side, but never expose them in the student's chat response.
            stateUpdate = aiResponse.StateUpdate is null
                ? null
                : new
                {
                    aiResponse.StateUpdate.Mood,
                    aiResponse.StateUpdate.Patience,
                    aiResponse.StateUpdate.TurnCount
                }
        });
    }

    [HttpPost("{sessionId}/feedback-survey")]
    public async Task<IActionResult> SubmitFeedbackSurvey(
        Guid sessionId,
        [FromBody] FeedbackSurveyDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var session = await _db.SimulationSessions
            .Include(item => item.EvaluationResult)
            .FirstOrDefaultAsync(item => item.Id == sessionId);
        if (session is null) return NotFound();
        if (session.StudentId != userId.Value) return Forbid();
        if (session.EvaluationResult is null)
            return BadRequest(new { message = "Vui lòng hoàn thành đánh giá trước khi gửi phản hồi." });

        var response = await _db.FeedbackSurveyResponses
            .FirstOrDefaultAsync(item => item.SessionId == sessionId);
        if (response is null)
        {
            response = new FeedbackSurveyResponse
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                StudentId = userId.Value,
                Variant = session.EvaluationResult.FeedbackVariant
            };
            _db.FeedbackSurveyResponses.Add(response);
        }
        response.Helpfulness = dto.Helpfulness;
        response.Actionability = dto.Actionability;
        response.NoAnswerLeak = dto.NoAnswerLeak;
        response.Comment = dto.Comment?.Trim();
        response.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Feedback survey saved.", response.Variant });
    }

    [HttpPost("{sessionId}/end")]
    [EnableRateLimiting("ai_expensive")]
    public async Task<IActionResult> EndSession(Guid sessionId)
    {
        var rawUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(rawUserId, out var parsedUserId)) return Unauthorized();

        var session = await _db.SimulationSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is null) return NotFound();

        if (session.StudentId != parsedUserId) return Forbid();

        // Order messages locally to ensure correct chronological history
        session.Messages = session.Messages.OrderBy(m => m.Timestamp).ToList();

        var existingResponse = await TryGetExistingEvaluationResponse(sessionId);
        if (existingResponse is not null)
            return Ok(existingResponse);

        var lease = CreateFinalizationLease();
        if (!await TryClaimFinalizationLease(sessionId, lease))
        {
            existingResponse = await WaitForExistingEvaluationResponse(sessionId);
            if (existingResponse is not null)
                return Ok(existingResponse);

            lease = CreateFinalizationLease();
            if (!await TryClaimFinalizationLease(sessionId, lease))
            {
                existingResponse = await TryGetExistingEvaluationResponse(sessionId);
                if (existingResponse is not null)
                    return Ok(existingResponse);

                return Conflict("Phiên phỏng vấn đang được hệ thống đánh giá, vui lòng không gửi lại.");
            }
        }

        session.IsActive = false;
        session.EndedAt = lease.StartedAt;
        session.FinalizationStatus = SessionFinalizationState.InProgress;
        session.FinalizationLeaseId = lease.LeaseId;
        session.FinalizationStartedAt = lease.StartedAt;
        session.FinalizationExpiresAt = lease.ExpiresAt;

        try
        {
            var history = session.Messages
                .Select(m => new ChatMessage(m.Sender.ToString(), m.Content, m.Timestamp))
                .ToList();

            var selectedModel = AiModelCatalog.DefaultModel;
            try
            {
                var snapshot = JsonSerializer.Deserialize<PersonaStateSnapshot>(
                    session.PersonaState ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (snapshot?.SelectedModel is not null)
                    selectedModel = AiModelCatalog.NormalizeOrDefault(snapshot.SelectedModel);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Stored persona state could not be parsed; using the default model.");
            }

            var serializedConfig = await _db.Scenarios
                .Where(item => item.Id == session.ScenarioId)
                .Select(item => item.SerializedConfig)
                .FirstOrDefaultAsync();
            var normalizationGlossary = TryReadNormalizationGlossary(serializedConfig);

            var extractResult = await _ai.ExtractRequirements(
                new AiExtractRequest(sessionId.ToString(), history, selectedModel, normalizationGlossary));

            var hiddenRequirements = await _db.HiddenRequirements
                .Where(r => r.ScenarioId == session.ScenarioId)
                .ToListAsync();

            var evaluateResult = await _ai.Evaluate(new AiEvaluateRequest(
                extractResult.Requirements,
                hiddenRequirements.Select(r => new HiddenReq(
                    r.Id.ToString(),
                    r.RequirementText,
                    r.Category.ToString(),
                    r.Actor,
                    r.Action,
                    r.Object,
                    r.Condition,
                    r.RequirementType,
                    r.Priority)).ToList(),
                selectedModel,
                await _db.Scenarios.Where(s => s.Id == session.ScenarioId)
                    .Select(s => s.Description).FirstOrDefaultAsync(),
                FeedbackVariantFor(sessionId),
                normalizationGlossary
            ));

            if (extractResult.IsFallback || evaluateResult.IsFallback)
            {
                await TryMarkFinalizationFailed(sessionId, lease);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Máy chủ AI đang khởi động lạnh hoặc gặp sự cố tạm thời. Bài làm của bạn chưa bị khóa điểm. Vui lòng thử lại sau ít giây."
                });
            }

            var evaluation = new EvaluationResult
            {
                SessionId = sessionId,
                CoverageScore = evaluateResult.CoverageScore,
                TotalRequirements = hiddenRequirements.Count,
                MatchedCount = evaluateResult.Matches.Count(m =>
                    ParseMatchType(m.MatchType) is RequirementMatchType.Exact or RequirementMatchType.Semantic),
                PartialCount = evaluateResult.Matches.Count(m =>
                    ParseMatchType(m.MatchType) == RequirementMatchType.Partial),
                MissedCount = evaluateResult.Matches.Count(m =>
                    ParseMatchType(m.MatchType) == RequirementMatchType.Missed),
                Feedback = JsonSerializer.Serialize(evaluateResult.Feedback),
                FeedbackVariant = evaluateResult.Feedback.ExperimentVariant
            };

            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                // 1. Delete previous evaluation details first to prevent FK/Unique constraints violations
                var oldEvaluation = await _db.EvaluationResults
                    .Include(e => e.Matches)
                    .FirstOrDefaultAsync(e => e.SessionId == sessionId);

                if (oldEvaluation is not null)
                {
                    _db.RequirementMatches.RemoveRange(oldEvaluation.Matches);
                    _db.EvaluationResults.Remove(oldEvaluation);
                }

                // 2. Delete previous extracted requirements
                var oldExtracted = await _db.ExtractedRequirements
                    .Where(r => r.SessionId == sessionId)
                    .ToListAsync();
                _db.ExtractedRequirements.RemoveRange(oldExtracted);

                // 3. Add new extracted requirements
                var normalizedByText = (extractResult.NormalizedRequirements ?? [])
                    .GroupBy(item => NormalizeRequirementText(item.CanonicalText))
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                var extractedEntities = extractResult.Requirements.Select(req =>
                {
                    normalizedByText.TryGetValue(NormalizeRequirementText(req.Text), out var normalized);
                    return new ExtractedRequirement
                    {
                        SessionId = sessionId,
                        RequirementText = req.Text,
                        ConfidenceScore = req.Confidence,
                        RawRequirementData = normalized is null
                            ? null
                            : JsonSerializer.Serialize(normalized.Original),
                        NormalizedRequirementData = normalized is null
                            ? null
                            : JsonSerializer.Serialize(normalized)
                    };
                }).ToList();

                _db.ExtractedRequirements.AddRange(extractedEntities);
                _db.EvaluationResults.Add(evaluation);

                await _db.SaveChangesAsync();

                foreach (var match in evaluateResult.Matches)
                {
                    if (!Guid.TryParse(match.HiddenId, out var hiddenRequirementId))
                        continue;

                    var normalizedExtractedText = NormalizeRequirementText(match.ExtractedText);
                    var extracted = match.ExtractedText is null
                        ? null
                        : extractedEntities.FirstOrDefault(r => string.Equals(NormalizeRequirementText(r.RequirementText), normalizedExtractedText, StringComparison.Ordinal));

                    _db.RequirementMatches.Add(new RequirementMatch
                    {
                        EvaluationId = evaluation.Id,
                        HiddenRequirementId = hiddenRequirementId,
                        ExtractedRequirementId = extracted?.Id,
                        SimilarityScore = match.Score,
                        MatchType = ParseMatchType(match.MatchType)
                    });
                }

                await _db.SaveChangesAsync();

                if (!await TryMarkFinalizationCompleted(sessionId, lease))
                    throw new DbUpdateConcurrencyException("Session finalization lease was lost before completion.");

                await transaction.CommitAsync();

                session.FinalizationStatus = SessionFinalizationState.Completed;
                session.FinalizationLeaseId = null;
                session.FinalizationExpiresAt = null;

                var matchReports = evaluateResult.Matches.Select(m => new RequirementMatchReport(
                    Guid.Empty.ToString(),
                    m.HiddenId,
                    m.HiddenText,
                    m.ExtractedText,
                    m.Score,
                    m.MatchType,
                    m.Reason,
                    null));

                return Ok(ToEvaluationResponse(
                    evaluation,
                    evaluateResult.Feedback,
                    extractedEntities.Count,
                    matchReports,
                    evaluateResult.ScoringPolicy,
                    evaluateResult.ExtraExtractedCount));
            }
            catch (DbUpdateException ex) when (IsEvaluationAlreadyPersisted(ex))
            {
                existingResponse = await TryGetExistingEvaluationResponse(sessionId);
                if (existingResponse is not null)
                    return Ok(existingResponse);

                throw;
            }
        }
        catch
        {
            await TryMarkFinalizationFailed(sessionId, lease);
            throw;
        }
    }

    [HttpGet("{sessionId}/messages")]
    public async Task<IActionResult> GetMessages(Guid sessionId)
    {
        var rawUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(rawUserId, out var parsedUserId)) return Unauthorized();

        var session = await _db.SimulationSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is null) return NotFound();

        var isPrivileged = IsPrivilegedUser();
        if (!isPrivileged && session.StudentId != parsedUserId) return Forbid();

        var messageRows = await _db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                m.Sender,
                m.Content,
                m.DetectedQuestionType,
                m.DetectedTopic,
                m.QuestionQuality,
                m.Timestamp
            })
            .ToListAsync();

        var messages = messageRows.Select(m => new
        {
            Sender = m.Sender.ToString(),
            m.Content,
            DetectedQuestionType = m.DetectedQuestionType?.ToString(),
            m.DetectedTopic,
            m.QuestionQuality,
            m.Timestamp
        }).ToList();

        return Ok(messages);
    }

    private static PersonaStateSnapshot DeserializePersonaState(string? stateJson, Persona persona)
    {
        if (!string.IsNullOrWhiteSpace(stateJson))
        {
            try
            {
                var state = JsonSerializer.Deserialize<PersonaStateSnapshot>(
                    stateJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (state is not null)
                {
                    state.RevealedRequirements ??= [];
                    if (string.IsNullOrWhiteSpace(state.Mood))
                        state.Mood = persona.InitialMood;
                    if (state.Patience <= 0m)
                        state.Patience = persona.InitialPatience;
                    return state;
                }
            }
            catch (JsonException)
            {
                // Soft fallback: khôi phục partial state từ JsonDocument nếu parse class thất bại
                try
                {
                    using var doc = JsonDocument.Parse(stateJson);
                    var root = doc.RootElement;
                    var mood = root.TryGetProperty("mood", out var m) ? m.GetString() : null;
                    var patience = root.TryGetProperty("patience", out var p) && p.TryGetDecimal(out var pVal) ? pVal : persona.InitialPatience;
                    var turn = root.TryGetProperty("turn_count", out var tc) && tc.TryGetInt32(out var tVal) ? tVal : 0;

                    return new PersonaStateSnapshot
                    {
                        Mood = string.IsNullOrWhiteSpace(mood) ? persona.InitialMood : mood,
                        Patience = patience > 0m ? patience : persona.InitialPatience,
                        RevealedRequirements = [],
                        TurnCount = turn
                    };
                }
                catch (JsonException)
                {
                    return new PersonaStateSnapshot
                    {
                        Mood = persona.InitialMood,
                        Patience = persona.InitialPatience,
                        RevealedRequirements = [],
                        TurnCount = 0
                    };
                }
            }
        }

        return new PersonaStateSnapshot
        {
            Mood = persona.InitialMood,
            Patience = persona.InitialPatience,
            RevealedRequirements = [],
            TurnCount = 0
        };
    }

    private static string FeedbackVariantFor(Guid sessionId) =>
        sessionId.ToByteArray()[0] % 2 == 0 ? "A" : "B";

    private static RequirementMatchType ParseMatchType(string matchType) =>
        Enum.TryParse<RequirementMatchType>(matchType, true, out var parsed)
            ? parsed
            : RequirementMatchType.Missed;

    private static FeedbackData? DeserializeFeedback(string? feedbackJson)
    {
        if (string.IsNullOrWhiteSpace(feedbackJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FeedbackData>(
                feedbackJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsEvaluationAlreadyPersisted(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgres &&
               postgres.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private async Task<List<RequirementMatchReport>> LoadRequirementMatchReports(Guid evaluationId)
    {
        var matches = await _db.RequirementMatches
            .AsNoTracking()
            .Include(m => m.HiddenRequirement)
            .Include(m => m.ExtractedRequirement)
            .Where(m => m.EvaluationId == evaluationId)
            .OrderBy(m => m.HiddenRequirement.RequirementText)
            .ToListAsync();

        return matches.Select(m => new RequirementMatchReport(
            m.Id.ToString(),
            m.HiddenRequirementId.ToString(),
            m.HiddenRequirement.RequirementText,
            m.ExtractedRequirement?.RequirementText,
            m.SimilarityScore ?? 0,
            m.MatchType.ToString().ToLowerInvariant(),
            BuildStoredMatchReason(m),
            m.OverriddenMatchType?.ToString().ToLowerInvariant()
        )).ToList();
    }

    private static string BuildStoredMatchReason(RequirementMatch match)
    {
        var effectiveType = match.OverriddenMatchType ?? match.MatchType;
        var score = match.SimilarityScore ?? 0;
        return effectiveType switch
        {
            RequirementMatchType.Exact =>
                $"Yêu cầu được trích xuất khớp hoàn toàn với yêu cầu ẩn (độ tương đồng {score:P0}).",
            RequirementMatchType.Semantic =>
                $"Cách diễn đạt khác biệt, nhưng yêu cầu được trích xuất vẫn giữ nguyên ý nghĩa cốt lõi (độ tương đồng {score:P0}).",
            RequirementMatchType.Partial =>
                $"Yêu cầu được trích xuất có liên quan nhưng chưa đầy đủ (độ tương đồng {score:P0}).",
            _ when match.ExtractedRequirement is not null =>
                $"Không có yêu cầu trích xuất nào đạt ngưỡng khớp một phần; ứng viên gần nhất đạt điểm số {score:P0}.",
            _ => "Không có yêu cầu trích xuất nào khả dụng để so khớp với yêu cầu ẩn này."
        };
    }

    private static object ToEvaluationResponse(
        EvaluationResult evaluation,
        FeedbackData? feedback,
        int extractedCount,
        IEnumerable<RequirementMatchReport>? matches = null,
        ScoringPolicyData? scoringPolicy = null,
        int? extraExtractedCount = null) => new
        {
            evaluation.CoverageScore,
            evaluation.OverriddenCoverageScore,
            OverriddenByLecturer = evaluation.OverriddenByLecturer?.Name,
            evaluation.OverriddenAt,
            evaluation.MatchedCount,
            evaluation.PartialCount,
            evaluation.MissedCount,
            Feedback = feedback,
            ExtractedCount = extractedCount,
            ExtraExtractedCount = extraExtractedCount ?? feedback?.ExtractionsToReview?.Count ?? 0,
            Matches = matches ?? [],
            ScoringPolicy = scoringPolicy
        };

    private Dictionary<string, Dictionary<string, string>>? TryReadNormalizationGlossary(string? serializedConfig)
    {
        if (string.IsNullOrWhiteSpace(serializedConfig)) return null;
        try
        {
            var config = JsonSerializer.Deserialize<ScenarioConfigJson>(serializedConfig);
            return config?.NormalizationGlossary is { Count: > 0 }
                ? config.NormalizationGlossary
                : null;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Scenario configuration could not be read for glossary normalization.");
            return null;
        }
    }

    private static string NormalizeRequirementText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Trim().ToLowerInvariant();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
        normalized = normalized.TrimEnd('.', '?', '!', ',', ';');
        return normalized;
    }
}
