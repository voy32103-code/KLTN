using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]  // Tất cả endpoints cần JWT
public class ScenariosController : ControllerBase
{
    private readonly AppDbContext _db;
    public ScenariosController(AppDbContext db) => _db = db;

    /// <summary>Lấy danh sách scenarios (cho student chọn)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var scenarios = await _db.Scenarios
            .Where(s => s.IsActive)
            .Select(s => new
            {
                s.Id, s.Title, s.Description, s.Domain, s.Difficulty,
                PersonaCount = s.Personas.Count,
                RequirementCount = s.HiddenRequirements.Count
            })
            .ToListAsync();
        return Ok(scenarios);
    }

    /// <summary>Chi tiết scenario + personas (KHÔNG trả hidden requirements cho student)</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var canViewInactive = role is "Lecturer" or "Admin";

        var scenario = await _db.Scenarios
            .Include(s => s.Personas)
            .FirstOrDefaultAsync(s => s.Id == id && (s.IsActive || canViewInactive));

        if (scenario == null) return NotFound();

        return Ok(new
        {
            scenario.Id, scenario.ScenarioKey, scenario.Version,
            scenario.Title, scenario.Description,
            scenario.Domain, scenario.Difficulty,
            PersonaCount = scenario.Personas.Count,
            RequirementCount = await _db.HiddenRequirements.CountAsync(r => r.ScenarioId == id),
            Personas = scenario.Personas.Select(p => new
            {
                p.Id, p.Name, p.RoleTitle, p.Difficulty,
                p.CommunicationStyle, p.KnowledgeLevel
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
