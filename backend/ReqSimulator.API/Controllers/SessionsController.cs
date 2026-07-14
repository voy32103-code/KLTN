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
    }

    public record CreateSessionDto(Guid ScenarioId, Guid PersonaId);
    public record SendMessageDto([Required, StringLength(4000)] string Content);

    private record RequirementMatchReport(
        string HiddenId,
        string? HiddenText,
        string? ExtractedText,
        decimal Score,
        string MatchType,
        string Reason);

    private sealed record FinalizationLeaseClaim(Guid LeaseId, DateTime StartedAt, DateTime ExpiresAt);
    private sealed record FinalizationLeaseSnapshot(SessionFinalizationState Status, DateTime? ExpiresAt);

    public SessionsController(AppDbContext db, AiServiceClient ai)
    {
        _db = db;
        _ai = ai;
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
                m.Timestamp
            })
            .ToListAsync();

        var messages = messageRows.Select(m => new
        {
            Sender = m.Sender.ToString(),
            m.Content,
            DetectedQuestionType = m.DetectedQuestionType?.ToString(),
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSessionDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        if (dto.ScenarioId == Guid.Empty || dto.PersonaId == Guid.Empty)
            return BadRequest("ScenarioId and PersonaId are required.");

        var scenario = await _db.Scenarios
            .Include(s => s.Personas)
            .FirstOrDefaultAsync(s => s.Id == dto.ScenarioId && s.IsActive);

        if (scenario is null)
            return NotFound("Scenario not found or inactive.");

        var persona = scenario.Personas.FirstOrDefault(p => p.Id == dto.PersonaId);
        if (persona is null)
            return BadRequest("Persona does not belong to the selected scenario.");

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
                TurnCount = 0
            })
        };

        _db.SimulationSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new { session.Id, session.StartedAt });
    }

    [HttpPost("{sessionId}/messages")]
    [EnableRateLimiting("ai_chat_limit")]
    public async Task<IActionResult> SendMessage(Guid sessionId, [FromBody] SendMessageDto dto)
    {
        var rawUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(rawUserId, out var parsedUserId)) return Unauthorized();

        var content = dto.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest("Message content is required.");

        // 1. Tải thông tin ban đầu không khóa và nằm ngoài transaction
        var sessionInit = await _db.SimulationSessions
            .AsNoTracking()
            .Include(s => s.Persona)
            .Include(s => s.Scenario)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (sessionInit is null) return NotFound();

        var isPrivileged = IsPrivilegedUser();
        if (!isPrivileged && sessionInit.StudentId != parsedUserId) return Forbid();
        if (!sessionInit.IsActive) return BadRequest("Session already ended");

        // Lấy lịch sử chat hiện tại để truyền cho AI
        var messagesHistory = await _db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new ChatMessage(m.Sender.ToString(), m.Content, m.Timestamp))
            .ToListAsync();

        var personaStateInit = DeserializePersonaState(sessionInit.PersonaState, sessionInit.Persona);

        var hiddenRequirements = await _db.HiddenRequirements
            .AsNoTracking()
            .Where(r => r.ScenarioId == sessionInit.ScenarioId)
            .ToListAsync();

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
            AvailableRequirements: hiddenRequirements.Select(r => r.RequirementText).ToList()
        ));

        // 3. Mở transaction cục bộ ngắn và khóa dòng FOR UPDATE để lưu kết quả nhanh
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var session = await _db.SimulationSessions
            .FromSqlInterpolated($"SELECT * FROM simulation_sessions WHERE id = {sessionId} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (session is null) return NotFound();
        await _db.Entry(session).Reference(s => s.Persona).LoadAsync();
        if (!session.IsActive) return BadRequest("Session already ended");

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
            stateUpdate = aiResponse.StateUpdate
        });
    }

    [HttpPost("{sessionId}/end")]
    [EnableRateLimiting("ai_chat_limit")]
    public async Task<IActionResult> EndSession(Guid sessionId)
    {
        var rawUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(rawUserId, out var parsedUserId)) return Unauthorized();

        var session = await _db.SimulationSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is null) return NotFound();

        var isPrivileged = IsPrivilegedUser();
        if (!isPrivileged && session.StudentId != parsedUserId) return Forbid();

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

                return Conflict("Session finalization is already in progress.");
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

            var extractResult = await _ai.ExtractRequirements(
                new AiExtractRequest(sessionId.ToString(), history));

            var hiddenRequirements = await _db.HiddenRequirements
                .Where(r => r.ScenarioId == session.ScenarioId)
                .ToListAsync();

            var evaluateResult = await _ai.Evaluate(new AiEvaluateRequest(
                extractResult.Requirements,
                hiddenRequirements.Select(r => new HiddenReq(
                    r.Id.ToString(),
                    r.RequirementText,
                    r.Category.ToString())).ToList()
            ));

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
                Feedback = JsonSerializer.Serialize(evaluateResult.Feedback)
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
                var extractedEntities = extractResult.Requirements.Select(req => new ExtractedRequirement
                {
                    SessionId = sessionId,
                    RequirementText = req.Text,
                    ConfidenceScore = req.Confidence
                }).ToList();

                _db.ExtractedRequirements.AddRange(extractedEntities);
                _db.EvaluationResults.Add(evaluation);

                await _db.SaveChangesAsync();

                foreach (var match in evaluateResult.Matches)
                {
                    if (!Guid.TryParse(match.HiddenId, out var hiddenRequirementId))
                        continue;

                    var extracted = match.ExtractedText is null
                        ? null
                        : extractedEntities.FirstOrDefault(r => string.Equals(r.RequirementText?.Trim(), match.ExtractedText?.Trim(), StringComparison.OrdinalIgnoreCase));

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
                    m.HiddenId,
                    m.HiddenText,
                    m.ExtractedText,
                    m.Score,
                    m.MatchType,
                    m.Reason));

                return Ok(ToEvaluationResponse(
                    evaluation,
                    evaluateResult.Feedback,
                    extractedEntities.Count,
                    matchReports,
                    evaluateResult.ScoringPolicy));
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
    [EnableRateLimiting("ai_chat_limit")]
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
                m.Timestamp
            })
            .ToListAsync();

        var messages = messageRows.Select(m => new
        {
            Sender = m.Sender.ToString(),
            m.Content,
            DetectedQuestionType = m.DetectedQuestionType?.ToString(),
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
                    return state;
                }
            }
            catch (JsonException)
            {
                // Fall back to persona defaults.
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
            m.HiddenRequirementId.ToString(),
            m.HiddenRequirement.RequirementText,
            m.ExtractedRequirement?.RequirementText,
            m.SimilarityScore ?? 0,
            m.MatchType.ToString().ToLowerInvariant(),
            BuildStoredMatchReason(m)
        )).ToList();
    }

    private static string BuildStoredMatchReason(RequirementMatch match)
    {
        var score = match.SimilarityScore ?? 0;
        return match.MatchType switch
        {
            RequirementMatchType.Exact =>
                $"The extracted requirement closely matches the hidden requirement (similarity {score:P0}).",
            RequirementMatchType.Semantic =>
                $"The wording differs, but the extracted requirement preserves the core meaning (similarity {score:P0}).",
            RequirementMatchType.Partial =>
                $"The extracted requirement is related, but incomplete (similarity {score:P0}).",
            _ when match.ExtractedRequirement is not null =>
                $"No extracted requirement reached the partial threshold; the closest stored candidate scored {score:P0}.",
            _ => "No extracted requirement was available to match this hidden requirement."
        };
    }

    private static object ToEvaluationResponse(
        EvaluationResult evaluation,
        FeedbackData? feedback,
        int extractedCount,
        IEnumerable<RequirementMatchReport>? matches = null,
        ScoringPolicyData? scoringPolicy = null) => new
        {
            evaluation.CoverageScore,
            evaluation.MatchedCount,
            evaluation.PartialCount,
            evaluation.MissedCount,
            Feedback = feedback,
            ExtractedCount = extractedCount,
            Matches = matches ?? [],
            ScoringPolicy = scoringPolicy
        };
}
