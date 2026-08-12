using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Services;
using System.Security.Claims;

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
}
