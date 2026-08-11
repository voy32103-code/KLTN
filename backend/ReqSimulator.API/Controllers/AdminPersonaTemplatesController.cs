using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Controllers;

/// <summary>Admin-owned reusable persona catalog. Published scenarios copy a snapshot of a selected template.</summary>
[ApiController]
[Route("api/admin-persona-templates")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin_ingestion")]
public sealed class AdminPersonaTemplatesController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminPersonaTemplatesController(AppDbContext db) => _db = db;

    public sealed record UpsertDto(
        [Required, StringLength(80)] string TemplateKey,
        [Required, StringLength(100)] string Label,
        [Required, StringLength(4000)] string PersonalityTraits,
        [Required, StringLength(50)] string CommunicationStyle,
        [Required, StringLength(50)] string KnowledgeLevel,
        PersonaDifficulty Difficulty,
        [Required, StringLength(50)] string InitialMood,
        [Range(typeof(decimal), "0.05", "1.00")] decimal InitialPatience,
        bool IsActive = true);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var templates = await _db.PersonaTemplates.AsNoTracking()
            .OrderByDescending(item => item.IsSystemDefault)
            .ThenBy(item => item.TemplateKey)
            .Select(item => new
            {
                item.Id,
                item.TemplateKey,
                item.Label,
                item.PersonalityTraits,
                item.CommunicationStyle,
                item.KnowledgeLevel,
                item.Difficulty,
                item.InitialMood,
                item.InitialPatience,
                item.IsActive,
                item.IsSystemDefault,
                item.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertDto dto, CancellationToken cancellationToken)
    {
        if (!TryNormalize(dto, out var key, out var traits, out var error)) return BadRequest(new { message = error });
        if (await _db.PersonaTemplates.AnyAsync(item => item.TemplateKey == key, cancellationToken))
            return Conflict(new { message = "A persona template with this key already exists." });

        var template = new PersonaTemplate
        {
            Id = Guid.NewGuid(),
            TemplateKey = key,
            Label = dto.Label.Trim(),
            PersonalityTraits = traits,
            CommunicationStyle = dto.CommunicationStyle.Trim(),
            KnowledgeLevel = dto.KnowledgeLevel.Trim(),
            Difficulty = dto.Difficulty,
            InitialMood = dto.InitialMood.Trim(),
            InitialPatience = dto.InitialPatience,
            IsActive = dto.IsActive,
            IsSystemDefault = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.PersonaTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/admin-persona-templates/{template.Id}", new { template.Id, template.TemplateKey });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertDto dto, CancellationToken cancellationToken)
    {
        var template = await _db.PersonaTemplates.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null) return NotFound();
        if (!TryNormalize(dto, out var key, out var traits, out var error)) return BadRequest(new { message = error });
        if (template.IsSystemDefault && !string.Equals(template.TemplateKey, key, StringComparison.Ordinal))
            return BadRequest(new { message = "System template keys cannot be renamed." });
        if (await _db.PersonaTemplates.AnyAsync(item => item.Id != id && item.TemplateKey == key, cancellationToken))
            return Conflict(new { message = "A persona template with this key already exists." });

        template.TemplateKey = key;
        template.Label = dto.Label.Trim();
        template.PersonalityTraits = traits;
        template.CommunicationStyle = dto.CommunicationStyle.Trim();
        template.KnowledgeLevel = dto.KnowledgeLevel.Trim();
        template.Difficulty = dto.Difficulty;
        template.InitialMood = dto.InitialMood.Trim();
        template.InitialPatience = dto.InitialPatience;
        template.IsActive = dto.IsActive;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { template.Id, template.TemplateKey, template.IsActive, template.UpdatedAt });
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var template = await _db.PersonaTemplates.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null) return NotFound();
        if (template.IsSystemDefault)
            return BadRequest(new { message = "System default templates cannot be archived." });
        template.IsActive = false;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { template.Id, template.IsActive });
    }

    private static bool TryNormalize(UpsertDto dto, out string key, out string traits, out string error)
    {
        key = new string((dto.TemplateKey ?? "").Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray()).Trim('_', '-');
        traits = dto.PersonalityTraits?.Trim() ?? "";
        error = "";
        if (key.Length is < 2 or > 80)
        {
            error = "Template key must contain 2–80 letters, numbers, underscores, or hyphens.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(dto.Label) || string.IsNullOrWhiteSpace(dto.CommunicationStyle) ||
            string.IsNullOrWhiteSpace(dto.KnowledgeLevel) || string.IsNullOrWhiteSpace(dto.InitialMood))
        {
            error = "Persona template fields cannot be blank.";
            return false;
        }
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(traits);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                error = "Personality traits must be a JSON object.";
                return false;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            error = "Personality traits must be valid JSON.";
            return false;
        }
        return true;
    }
}
