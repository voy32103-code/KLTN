using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Lecturer,Admin")]
[EnableRateLimiting("admin_ingestion")]
public class AdminScenariosController : ControllerBase
{
    private readonly AiServiceClient _ai;
    private readonly ScenarioVersionPublisher _publisher;
    private readonly ILogger<AdminScenariosController> _logger;

    public sealed record CrawlRequestDto(
        [Required, Url, StringLength(2048)] string Url,
        [StringLength(100)] string? SelectedModel);

    public sealed record VideoRequestDto(
        [Required, StringLength(1024)] string VideoPath,
        [StringLength(100)] string? SelectedModel);

    public AdminScenariosController(
        AiServiceClient ai,
        ScenarioVersionPublisher publisher,
        ILogger<AdminScenariosController> logger)
    {
        _ai = ai;
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost("crawl")]
    public async Task<IActionResult> CrawlScenario(
        [FromBody] CrawlRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedModel(dto.SelectedModel))
            return BadRequest(new { message = "Mô hình AI không được hỗ trợ." });

        _logger.LogInformation(
            "Bắt đầu tạo scenario từ host {Host}.",
            GetSafeHost(dto.Url));

        var response = await _ai.CrawlScenario(dto.Url, dto.SelectedModel);
        if (!response.Success || response.Scenario is null)
            return BadRequest(new { message = "Không thể tạo scenario từ nguồn đã cung cấp." });

        try
        {
            var scenario = await _publisher.PublishAsync(response.Scenario, cancellationToken);
            return Ok(new
            {
                message = "Tạo và publish scenario thành công.",
                scenarioId = scenario.Id,
                scenario.ScenarioKey,
                scenario.Version,
                scenario.Title,
                requirementsCount = scenario.HiddenRequirements.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Scenario được tạo không vượt qua validation.");
            return BadRequest(new { message = "Scenario được tạo không hợp lệ." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể publish scenario từ URL.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Không thể publish scenario." });
        }
    }

    [HttpPost("upload-video")]
    public async Task<IActionResult> UploadVideoScenario(
        [FromBody] VideoRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (!AiModelCatalog.IsGemini(dto.SelectedModel))
            return BadRequest(new { message = "Mô hình AI không được hỗ trợ." });

        _logger.LogInformation(
            "Bắt đầu tạo scenario từ video {FileName}.",
            Path.GetFileName(dto.VideoPath));

        var response = await _ai.UploadVideoScenario(dto.VideoPath, dto.SelectedModel);
        if (!response.Success || response.Scenario is null)
            return BadRequest(new { message = "Không thể tạo scenario từ video đã cung cấp." });

        try
        {
            var scenario = await _publisher.PublishAsync(response.Scenario, cancellationToken);
            return Ok(new
            {
                message = "Tạo và publish scenario thành công.",
                scenarioId = scenario.Id,
                scenario.ScenarioKey,
                scenario.Version,
                scenario.Title,
                requirementsCount = scenario.HiddenRequirements.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Scenario được tạo từ video không vượt qua validation.");
            return BadRequest(new { message = "Scenario được tạo không hợp lệ." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể publish scenario từ video.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Không thể publish scenario." });
        }
    }

    private static bool IsAllowedModel(string? model) =>
        AiModelCatalog.IsSupported(model);

    private static string GetSafeHost(string rawUrl) =>
        Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            ? uri.IdnHost
            : "invalid-host";
}
