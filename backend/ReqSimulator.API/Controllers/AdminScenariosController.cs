using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

/// <summary>Publishes an admin-reviewed scenario draft produced by the ingestion pipeline.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin_ingestion")]
public class AdminScenariosController : ControllerBase
{
    private readonly ScenarioVersionPublisher _publisher;
    private readonly ILogger<AdminScenariosController> _logger;

    public AdminScenariosController(
        ScenarioVersionPublisher publisher,
        ILogger<AdminScenariosController> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost("publish")]
    public async Task<IActionResult> PublishScenario(
        [FromBody] ScenarioConfigJson scenario,
        CancellationToken cancellationToken)
    {
        try
        {
            var published = await _publisher.PublishAsync(scenario, cancellationToken);
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
}
