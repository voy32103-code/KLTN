using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin_ingestion")]
public class AdminScenariosController : ControllerBase
{
    private readonly AiServiceClient _ai;
    private readonly ScenarioVersionPublisher _publisher;
    private readonly ILogger<AdminScenariosController> _logger;

    public sealed record CrawlRequestDto(
        [Required, Url, StringLength(2048)] string Url,
        [StringLength(100)] string? SelectedModel);
    public sealed record MultiSourceCrawlRequestDto(
        [Required, MinLength(1), MaxLength(10)] List<string> Urls,
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

    [NonAction]
    [HttpPost("crawl/preview")]
    public async Task<IActionResult> PreviewCrawlScenario([FromBody] CrawlRequestDto dto)
    {
        if (!IsAllowedModel(dto.SelectedModel))
            return BadRequest(new { message = "The selected AI model is not supported." });
        _logger.LogInformation(
            "Starting scenario preview generation from host {Host}.",
            GetSafeHost(dto.Url));

        var response = await _ai.CrawlScenario(dto.Url, dto.SelectedModel, persist: false);
        if (!response.Success || response.Scenario is null)
            return BadRequest(new { message = "Could not generate a preview from the supplied source." });

        return Ok(new
        {
            message = "Preview generated. Review and edit it before publishing.",
            scenario = response.Scenario with { SourceUrls = [dto.Url] }
        });
    }

    [NonAction]
    [HttpPost("crawl/preview-multiple")]
    public async Task<IActionResult> PreviewMultipleSources(
        [FromBody] MultiSourceCrawlRequestDto dto)
    {
        if (!IsAllowedModel(dto.SelectedModel))
            return BadRequest(new { message = "The selected AI model is not supported." });
        if (dto.Urls.Any(url => url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            return BadRequest(new { message = "Every source must be a valid HTTP(S) URL." });

        var urls = dto.Urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var responses = await Task.WhenAll(urls.Select(url =>
            _ai.CrawlScenario(url, dto.SelectedModel, persist: false)));
        var configs = responses
            .Where(result => result.Success && result.Scenario is not null)
            .Select(result => result.Scenario!)
            .ToList();
        if (configs.Count == 0)
            return BadRequest(new { message = "No supplied source produced a valid preview." });

        var first = configs[0];
        var requirements = configs.SelectMany(config => config.Requirements)
            .GroupBy(rule => string.Join("|",
                rule.Type?.Trim().ToUpperInvariant(),
                rule.Action?.Trim().ToLowerInvariant(),
                rule.Object?.Trim().ToLowerInvariant(),
                rule.Text.Trim().ToLowerInvariant()))
            .Select(group => group.First())
            .ToList();
        var merged = first with
        {
            Context = string.Join("\n\n", configs.Select(config => config.Context)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
            GeneralKeywords = configs.SelectMany(config => config.GeneralKeywords)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Requirements = requirements,
            SourceUrls = urls
        };
        return Ok(new
        {
            message = $"Merged {configs.Count} sources. Review before publishing.",
            scenario = merged
        });
    }

    [NonAction]
    [HttpPost("upload-video/preview")]
    public async Task<IActionResult> PreviewVideoScenario([FromBody] VideoRequestDto dto)
    {
        if (!AiModelCatalog.IsGemini(dto.SelectedModel))
            return BadRequest(new { message = "The selected AI model is not supported." });

        _logger.LogInformation(
            "Starting scenario preview generation from video {FileName}.",
            Path.GetFileName(dto.VideoPath));

        var response = await _ai.UploadVideoScenario(
            dto.VideoPath,
            dto.SelectedModel,
            persist: false);
        if (!response.Success || response.Scenario is null)
            return BadRequest(new { message = "Could not generate a preview from the supplied video." });

        return Ok(new
        {
            message = "Preview generated. Review and edit it before publishing.",
            scenario = response.Scenario
        });
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "The admin-edited scenario failed validation.");
            return BadRequest(new { message = "The scenario draft is invalid." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not publish the admin-edited scenario.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Could not publish the scenario." });
        }
    }
    [NonAction]
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

    [NonAction]
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
