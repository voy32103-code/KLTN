using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using ReqSimulator.API.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ReqSimulator.API.Controllers;

/// <summary>Publishes an admin-reviewed scenario draft produced by the ingestion pipeline.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin_ingestion")]
public class AdminScenariosController : ControllerBase
{
    private readonly ScenarioVersionPublisher _publisher;
    private readonly AppDbContext _db;
    private readonly ILogger<AdminScenariosController> _logger;

    public AdminScenariosController(
        ScenarioVersionPublisher publisher,
        AppDbContext db,
        ILogger<AdminScenariosController> logger)
    {
        _publisher = publisher;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Loads the active published scenario as an editable draft. Publishing the
    /// returned draft creates a new immutable version; the original is retained
    /// for existing sessions and audit history.
    /// </summary>
    [HttpGet("{scenarioId:guid}/draft")]
    public async Task<IActionResult> GetEditableDraft(
        Guid scenarioId,
        CancellationToken cancellationToken)
    {
        var scenario = await _db.Scenarios.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == scenarioId && item.IsActive, cancellationToken);

        if (scenario is null)
            return NotFound(new { message = "Active scenario not found." });

        if (!string.IsNullOrWhiteSpace(scenario.SerializedConfig))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<ScenarioConfigJson>(scenario.SerializedConfig);
                if (draft is not null && draft.Requirements.Count > 0)
                    return Ok(draft with { ReviewNotes = null });
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception,
                    "Scenario {ScenarioId} has an invalid serialized draft; rebuilding it from persisted requirements.",
                    scenarioId);
            }
        }

        var requirements = await _db.HiddenRequirements.AsNoTracking()
            .Where(item => item.ScenarioId == scenarioId)
            .OrderBy(item => item.GateOrder)
            .ThenBy(item => item.CreatedAt)
            .Select(item => new
            {
                item.RequirementText,
                item.GateOrder,
                item.RevealCondition,
                item.RevealDifficulty,
                item.Actor,
                item.Action,
                item.Object,
                item.Condition,
                item.RequirementType,
                item.Priority
            })
            .ToListAsync(cancellationToken);

        if (requirements.Count == 0)
            return Conflict(new { message = "The scenario has no requirements to edit." });

        var sourceUrls = DeserializeSourceUrls(scenario.SourceUrlsData);
        var fallbackRules = requirements.Select((item, index) => new ScenarioRequirementRuleJson(
            $"R{index + 1}",
            item.RequirementText,
            item.GateOrder,
            [],
            ["OpenEnded"],
            item.RevealCondition ?? "",
            item.RevealDifficulty.ToString(),
            [],
            item.Actor,
            item.Action,
            item.Object,
            item.Condition,
            item.RequirementType,
            item.Priority)).ToList();

        return Ok(new ScenarioConfigJson(
            scenario.ScenarioKey,
            scenario.Title,
            scenario.Description,
            [],
            new Dictionary<string, List<string>>(),
            new Dictionary<string, List<int>>(),
            1,
            fallbackRules,
            sourceUrls));
    }

    [HttpPost("publish")]
    public async Task<IActionResult> PublishScenario(
        [FromBody] ScenarioConfigJson scenario,
        CancellationToken cancellationToken)
    {
        try
        {
            var reviewerClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(reviewerClaim, out var reviewerId)) return Unauthorized();
            var published = await _publisher.PublishAsync(scenario, reviewerId, cancellationToken);
            return Ok(new
            {
                message = "Scenario published successfully.",
                scenarioId = published.Id,
                published.ScenarioKey,
                published.Version,
                published.Title,
                requirementsCount = published.HiddenRequirements.Count
            });
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "The admin-edited scenario failed validation.");
            return BadRequest(new { message = "The scenario draft is invalid." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not publish the admin-edited scenario.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Could not publish the scenario." });
        }
    }

    [HttpGet("{scenarioId:guid}/reviews")]
    public async Task<IActionResult> GetReviewHistory(Guid scenarioId, CancellationToken cancellationToken)
    {
        var history = await _db.ScenarioReviewAudits.AsNoTracking()
            .Where(item => item.ScenarioId == scenarioId)
            .OrderByDescending(item => item.ReviewedAt)
            .Select(item => new
            {
                item.Id,
                item.ReviewerId,
                ReviewerName = item.Reviewer.Name,
                item.Notes,
                item.SourceUrlsData,
                item.RequirementCount,
                item.ReviewedAt
            })
            .ToListAsync(cancellationToken);
        return Ok(history);
    }

    private static List<string>? DeserializeSourceUrls(string? sourceUrlsData)
    {
        if (string.IsNullOrWhiteSpace(sourceUrlsData)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(sourceUrlsData);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
