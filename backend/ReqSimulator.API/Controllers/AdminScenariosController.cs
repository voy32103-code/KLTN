using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Lecturer,Admin")] // Cho phép cả Giảng viên và Admin quản lý
public class AdminScenariosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AiServiceClient _ai;
    private readonly ILogger<AdminScenariosController> _logger;

    public record CrawlRequestDto([Required] string Url, string? SelectedModel);
    public record VideoRequestDto([Required] string VideoPath, string? SelectedModel);

    public AdminScenariosController(AppDbContext db, AiServiceClient ai, ILogger<AdminScenariosController> logger)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
    }

    [HttpPost("crawl")]
    public async Task<IActionResult> CrawlScenario([FromBody] CrawlRequestDto dto)
    {
        _logger.LogInformation("Admin bắt đầu cào kịch bản từ URL: {Url}", dto.Url);
        
        var response = await _ai.CrawlScenario(dto.Url, dto.SelectedModel);
        if (!response.Success || response.Scenario is null)
        {
            return BadRequest(new { message = response.Message });
        }

        var scenario = await SyncScenarioToDb(response.Scenario);
        return Ok(new
        {
            message = "Cào dữ liệu và tạo kịch bản thành công.",
            scenarioId = scenario.Id,
            title = scenario.Title,
            requirementsCount = scenario.HiddenRequirements.Count
        });
    }

    [HttpPost("upload-video")]
    public async Task<IActionResult> UploadVideoScenario([FromBody] VideoRequestDto dto)
    {
        _logger.LogInformation("Admin bắt đầu nạp kịch bản từ Video Path: {VideoPath}", dto.VideoPath);

        var response = await _ai.UploadVideoScenario(dto.VideoPath, dto.SelectedModel);
        if (!response.Success || response.Scenario is null)
        {
            return BadRequest(new { message = response.Message });
        }

        var scenario = await SyncScenarioToDb(response.Scenario);
        return Ok(new
        {
            message = "Xử lý video và tạo kịch bản thành công.",
            scenarioId = scenario.Id,
            title = scenario.Title,
            requirementsCount = scenario.HiddenRequirements.Count
        });
    }

    private async Task<Scenario> SyncScenarioToDb(ScenarioConfigJson config)
    {
        // 1. Kiểm tra Scenario đã tồn tại chưa
        var scenario = await _db.Scenarios
            .Include(s => s.Personas)
            .Include(s => s.HiddenRequirements)
            .FirstOrDefaultAsync(s => s.Title.ToLower() == config.ScenarioTitle.ToLower());

        if (scenario is null)
        {
            scenario = new Scenario
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
            _db.Scenarios.Add(scenario);
        }

        scenario.Title = config.ScenarioTitle;
        scenario.Description = config.Context.Length > 500 ? config.Context.Substring(0, 500) : config.Context;
        scenario.Domain = "General"; // Có thể mở rộng để AI tự động phân loại
        scenario.Difficulty = PersonaDifficulty.Medium;
        scenario.Version = 1;
        scenario.IsActive = true;

        // 2. Tạo hoặc Cập nhật default Persona
        var persona = scenario.Personas.FirstOrDefault();
        if (persona is null)
        {
            persona = new Persona
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenario.Id,
                CreatedAt = DateTime.UtcNow
            };
            _db.Personas.Add(persona);
        }

        persona.Name = "Khách Hàng (Stakeholder)";
        persona.RoleTitle = "Đại Diện Nghiệp Vụ";
        persona.PersonalityTraits = "{\"traits\": [\"busy\", \"detail_oriented\"], \"jargon_level\": \"medium\"}";
        persona.CommunicationStyle = "professional";
        persona.KnowledgeLevel = "high";
        persona.Difficulty = PersonaDifficulty.Medium;
        persona.InitialMood = "neutral";
        persona.InitialPatience = 1.00m;

        // 3. Cập nhật danh sách Hidden Requirements
        // Để tránh trùng lặp hoặc mâu thuẫn, xóa requirements cũ đi nạp lại hoặc cập nhật
        if (scenario.HiddenRequirements.Count > 0)
        {
            _db.HiddenRequirements.RemoveRange(scenario.HiddenRequirements);
        }

        foreach (var rule in config.Requirements)
        {
            var req = new HiddenRequirement
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenario.Id,
                RequirementText = rule.Text,
                Category = MapCategory(rule.Gate, rule.Text),
                RevealDifficulty = MapDifficulty(rule.RevealDifficulty),
                RevealCondition = rule.RevealCondition,
                GateOrder = rule.Gate,
                CreatedAt = DateTime.UtcNow
            };
            _db.HiddenRequirements.Add(req);
        }

        await _db.SaveChangesAsync();
        return scenario;
    }

    private static RequirementCategory MapCategory(int gate, string text)
    {
        if (gate == 4) return RequirementCategory.NonFunctional;
        if (gate == 3) return RequirementCategory.BusinessRule;
        
        string lower = text.ToLower();
        if (gate == 2 && (lower.Contains("chặn") || lower.Contains("bảo mật") || lower.Contains("phân quyền") || lower.Contains("quyền")))
        {
            return RequirementCategory.Constraint;
        }
        
        return RequirementCategory.Functional;
    }

    private static PersonaDifficulty MapDifficulty(string diff)
    {
        if (string.IsNullOrEmpty(diff)) return PersonaDifficulty.Medium;
        return diff.ToLower() switch
        {
            "easy" => PersonaDifficulty.Easy,
            "hard" => PersonaDifficulty.Hard,
            _ => PersonaDifficulty.Medium
        };
    }
}
