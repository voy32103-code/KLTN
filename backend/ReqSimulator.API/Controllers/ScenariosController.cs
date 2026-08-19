using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]  // Tất cả endpoints cần JWT
public class ScenariosController : ControllerBase
{
    private const string StudentCatalogDomain = "Information Technology";
    private readonly AppDbContext _db;
    private readonly ScenarioLocalizationCatalog _localizations;

    public ScenariosController(AppDbContext db, ScenarioLocalizationCatalog localizations)
    {
        _db = db;
        _localizations = localizations;
    }

    /// <summary>Lấy danh sách scenarios (cho student chọn)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? lang = null)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var scenariosQuery = _db.Scenarios.Where(s => s.IsActive);

        // The student catalog is intentionally limited to the refactored IT scenarios.
        // Legacy scenarios can remain in the database for audit/history without being selectable.
        if (role == "Student")
        {
            scenariosQuery = scenariosQuery.Where(s => s.Domain == StudentCatalogDomain);
        }

        var scenarios = await scenariosQuery
            .Select(s => new
            {
                s.Id, s.ScenarioKey, s.Title, s.Description, s.Domain, s.Difficulty,
                PersonaCount = s.Personas.Count,
                RequirementCount = s.HiddenRequirements.Count
            })
            .ToListAsync();
        return Ok(scenarios.Select(scenario =>
        {
            var display = _localizations.Resolve(
                scenario.ScenarioKey,
                scenario.Title,
                scenario.Description,
                scenario.Domain,
                lang);
            return new
            {
                scenario.Id,
                display.Title,
                display.Description,
                display.Domain,
                scenario.Difficulty,
                scenario.PersonaCount,
                scenario.RequirementCount,
                display.Language
            };
        }));
    }

    /// <summary>Chi tiết scenario + personas (KHÔNG trả hidden requirements cho student)</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] string? lang = null)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var canViewInactive = role is "Lecturer" or "Admin";
        var canUseLegacyCatalog = role is "Lecturer" or "Admin";

        var scenario = await _db.Scenarios
            .Include(s => s.Personas)
                .ThenInclude(p => p.Stakeholder)
            .FirstOrDefaultAsync(s => s.Id == id
                && (s.IsActive || canViewInactive)
                && (canUseLegacyCatalog || s.Domain == StudentCatalogDomain));

        if (scenario == null) return NotFound();

        var display = _localizations.Resolve(
            scenario.ScenarioKey,
            scenario.Title,
            scenario.Description,
            scenario.Domain,
            lang);

        return Ok(new
        {
            scenario.Id, scenario.ScenarioKey, scenario.Version,
            display.Title, display.Description,
            display.Domain, scenario.Difficulty, display.Language,
            PersonaCount = scenario.Personas.Count,
            RequirementCount = await _db.HiddenRequirements.CountAsync(r => r.ScenarioId == id),
            Personas = scenario.Personas.Select(p => new
            {
                p.Id, p.Name, p.RoleTitle, p.Difficulty,
                p.Label, p.CommunicationStyle, p.KnowledgeLevel,
                Stakeholder = p.Stakeholder == null ? null : new
                {
                    p.Stakeholder.Id,
                    p.Stakeholder.Name,
                    p.Stakeholder.RoleTitle,
                    p.Stakeholder.Department
                }
            }),
            // Chỉ lecturer/admin mới thấy hidden requirements
            HiddenRequirements = role is "Lecturer" or "Admin"
                ? await _db.HiddenRequirements.Where(r => r.ScenarioId == id)
                    .Select(r => new { r.Id, r.RequirementText, r.Category, r.RevealDifficulty })
                    .ToListAsync()
                : null
        });
    }
}
