using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Services;

/// <summary>
/// Publishes immutable scenario snapshots. A published version is never edited or
/// deleted, so historical sessions and evaluation matches keep stable foreign keys.
/// </summary>
public sealed partial class ScenarioVersionPublisher
{
    // Student-facing endpoints deliberately expose only this catalog domain.
    // Imported scenarios must therefore be published into it, not "General".
    private const string StudentCatalogDomain = "Information Technology";
    private readonly AppDbContext _db;

    public ScenarioVersionPublisher(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Scenario> PublishAsync(
        ScenarioConfigJson config,
        Guid? reviewerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var scenarioKey = NormalizeScenarioKey(config.ScenarioKey);
        if (string.IsNullOrWhiteSpace(scenarioKey))
            throw new InvalidOperationException("Scenario key is required.");
        if (string.IsNullOrWhiteSpace(config.ScenarioTitle))
            throw new InvalidOperationException("Scenario title is required.");
        if (config.Requirements is null || config.Requirements.Count == 0)
            throw new InvalidOperationException("A scenario must contain at least one requirement.");

        // Review notes are evidence about this publication, not scenario behavior.
        // Excluding them from the snapshot prevents a comment-only change from
        // creating a duplicate scenario version.
        var persistedConfig = config with { ReviewNotes = null };
        var serializedConfig = JsonSerializer.Serialize(persistedConfig);
        var configHash = ComputeHash(serializedConfig);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // Serialize publishers for the same logical scenario across application instances.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({scenarioKey}))",
            cancellationToken);

        var personaTemplates = await ResolvePersonaTemplatesAsync(config, cancellationToken);

        var current = await _db.Scenarios
            .Include(s => s.Personas)
            .Include(s => s.HiddenRequirements)
            .Where(s => s.IsActive &&
                (s.ScenarioKey == scenarioKey || s.Title == config.ScenarioTitle))
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null &&
            (string.Equals(current.ConfigHash, configHash, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(current.SerializedConfig) &&
              string.Equals(ComputeHash(current.SerializedConfig), configHash, StringComparison.OrdinalIgnoreCase))))
        {
            // Publishing an unchanged scenario is idempotent, but an admin review is still
            // material evidence. Record it rather than silently dropping the review notes.
            var catalogUpdated = !string.Equals(current.Domain, StudentCatalogDomain, StringComparison.Ordinal);
            if (catalogUpdated) current.Domain = StudentCatalogDomain;
            if (reviewerId is Guid previousReviewer)
            {
                current.ReviewedByUserId = previousReviewer;
                current.ReviewedAt = now;
                current.ReviewNotes = Limit(config.ReviewNotes, 1000);
                _db.ScenarioReviewAudits.Add(new ScenarioReviewAudit
                {
                    Id = Guid.NewGuid(),
                    ScenarioId = current.Id,
                    ReviewerId = previousReviewer,
                    Notes = current.ReviewNotes,
                    SourceUrlsData = current.SourceUrlsData,
                    RequirementCount = current.HiddenRequirements.Count,
                    ReviewedAt = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (catalogUpdated)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return current;
        }

        var maxVersion = await _db.Scenarios
            .Where(s => s.ScenarioKey == scenarioKey ||
                (current != null && s.Id == current.Id))
            .Select(s => (int?)s.Version)
            .MaxAsync(cancellationToken) ?? 0;

        if (current is not null)
        {
            current.ScenarioKey = scenarioKey;
            current.IsActive = false;
            current.SupersededAt = now;
            // Persist deactivation before inserting the replacement so the
            // partial unique index can never observe two active versions.
            await _db.SaveChangesAsync(cancellationToken);
        }

        var next = new Scenario
        {
            Id = Guid.NewGuid(),
            ScenarioKey = scenarioKey,
            Title = config.ScenarioTitle.Trim(),
            Description = Limit(config.Context?.Trim(), 500) ?? "General scenario context.",
            Domain = StudentCatalogDomain,
            Difficulty = PersonaDifficulty.Medium,
            Version = maxVersion + 1,
            IsActive = true,
            CreatedAt = now,
            PublishedAt = now,
            ConfigHash = configHash,
            SerializedConfig = serializedConfig,
            ReviewedByUserId = reviewerId,
            ReviewedAt = reviewerId is null ? null : now,
            ReviewNotes = Limit(config.ReviewNotes, 1000),
            SourceUrlsData = JsonSerializer.Serialize(
                (config.SourceUrls ?? [])
                    .Where(url => Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList())
        };

        var stakeholderTemplates = new[]
        {
            ("Chủ sở hữu nghiệp vụ", "Người ra quyết định", "Quản lý", "Đại diện cho góc nhìn quản lý."),
            ("Chuyên gia quy trình", "Chuyên gia nghiệp vụ", "Vận hành", "Đại diện cho góc nhìn vận hành."),
            ("Người dùng cuối", "Người dùng vận hành", "Triển khai", "Đại diện cho góc nhìn sử dụng hằng ngày.")
        };
        foreach (var role in stakeholderTemplates)
        {
            var stakeholder = new Stakeholder
            {
                Id = Guid.NewGuid(), ScenarioId = next.Id, Name = role.Item1,
                RoleTitle = role.Item2, Department = role.Item3,
                Description = role.Item4, CreatedAt = now
            };
            next.Stakeholders.Add(stakeholder);
            foreach (var profile in personaTemplates)
            {
                var roleProfile = role.Item1 == "Người dùng cuối"
                    ? (Traits: """{"traits":["practical","experience_based"],"jargon_level":"low","technical_scope":"none"}""", KnowledgeLevel: "low")
                    : (Traits: profile.PersonalityTraits, KnowledgeLevel: profile.KnowledgeLevel);
                var persona = new Persona
                {
                    Id = Guid.NewGuid(), ScenarioId = next.Id, StakeholderId = stakeholder.Id,
                    TemplateId = profile.Id == Guid.Empty ? null : profile.Id,
                    Name = $"{role.Item1} - {profile.Label}", Label = profile.Label,
                    RoleTitle = role.Item2,
                    PersonalityTraits = roleProfile.Traits,
                    CommunicationStyle = profile.CommunicationStyle, KnowledgeLevel = roleProfile.KnowledgeLevel,
                    Difficulty = profile.Difficulty, InitialMood = profile.InitialMood,
                    InitialPatience = profile.InitialPatience, CreatedAt = now
                };
                stakeholder.Personas.Add(persona);
                next.Personas.Add(persona);
            }
        }

        foreach (var rule in config.Requirements)
        {
            if (string.IsNullOrWhiteSpace(rule.Text))
                throw new InvalidOperationException("Scenario requirements cannot be empty.");

            next.HiddenRequirements.Add(new HiddenRequirement
            {
                Id = Guid.NewGuid(),
                ScenarioId = next.Id,
                RequirementText = rule.Text.Trim(),
                Category = MapCategory(rule.Gate, rule.Text),
                RevealDifficulty = MapDifficulty(rule.RevealDifficulty),
                RevealCondition = Limit(rule.RevealCondition?.Trim(), 1000),
                GateOrder = Math.Clamp(rule.Gate, 0, 4),
                Actor = Limit(rule.Actor?.Trim(), 160),
                Action = Limit(rule.Action?.Trim(), 160),
                Object = Limit(rule.Object?.Trim(), 240),
                Condition = Limit(rule.Condition?.Trim(), 500),
                RequirementType = NormalizeRequirementType(rule.Type),
                Priority = NormalizePriority(rule.Priority),
                NormalizedRequirementData = JsonSerializer.Serialize(new
                {
                    id = rule.Id, actor = rule.Actor, action = rule.Action,
                    @object = rule.Object, condition = rule.Condition,
                    type = NormalizeRequirementType(rule.Type),
                    priority = NormalizePriority(rule.Priority)
                }),
                CreatedAt = now
            });
        }

        _db.Scenarios.Add(next);
        if (reviewerId is Guid reviewer)
        {
            _db.ScenarioReviewAudits.Add(new ScenarioReviewAudit
            {
                Id = Guid.NewGuid(),
                ScenarioId = next.Id,
                ReviewerId = reviewer,
                Notes = Limit(config.ReviewNotes, 1000),
                SourceUrlsData = next.SourceUrlsData,
                RequirementCount = next.HiddenRequirements.Count,
                ReviewedAt = now
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    internal static string NormalizeScenarioKey(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        normalized = InvalidScenarioKeyCharacters().Replace(normalized, "_");
        normalized = RepeatedUnderscores().Replace(normalized, "_").Trim('_', '-');
        return normalized.Length <= 100 ? normalized : normalized[..100].TrimEnd('_', '-');
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];

    private async Task<List<PersonaTemplate>> ResolvePersonaTemplatesAsync(
        ScenarioConfigJson config,
        CancellationToken cancellationToken)
    {
        var requestedKeys = (config.PersonaTemplateKeys ?? [])
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requestedKeys.Count > 0 && (requestedKeys.Count < 2 || requestedKeys.Count > 3))
            throw new InvalidOperationException("Select between two and three reusable persona templates.");

        var query = _db.PersonaTemplates.AsNoTracking().Where(item => item.IsActive);
        var templates = requestedKeys.Count == 0
            ? await query.Where(item => item.IsSystemDefault)
                .OrderBy(item => item.TemplateKey)
                .ToListAsync(cancellationToken)
            : await query.Where(item => requestedKeys.Contains(item.TemplateKey))
                .OrderBy(item => item.TemplateKey)
                .ToListAsync(cancellationToken);

        if (requestedKeys.Count > 0 && templates.Count != requestedKeys.Count)
            throw new InvalidOperationException("One or more selected persona templates are unavailable.");

        // Tests and a newly provisioned database can publish before the migration
        // seeds the catalog; keep the existing safe defaults as a non-persistent fallback.
        return templates.Count >= 2 && templates.Count <= 3 ? templates : DefaultPersonaTemplates();
    }

    private static List<PersonaTemplate> DefaultPersonaTemplates() =>
    [
        new PersonaTemplate
        {
            TemplateKey = "collaborative", Label = "Hợp tác",
            PersonalityTraits = "{\"traits\":[\"collaborative\",\"detail_oriented\"]}",
            CommunicationStyle = "collaborative", KnowledgeLevel = "high",
            Difficulty = PersonaDifficulty.Easy, InitialMood = "neutral", InitialPatience = 1.00m,
            IsActive = true, IsSystemDefault = true
        },
        new PersonaTemplate
        {
            TemplateKey = "challenging", Label = "Phản biện",
            PersonalityTraits = "{\"traits\":[\"challenging\",\"detail_oriented\"]}",
            CommunicationStyle = "concise", KnowledgeLevel = "medium",
            Difficulty = PersonaDifficulty.Hard, InitialMood = "neutral", InitialPatience = 0.70m,
            IsActive = true, IsSystemDefault = true
        }
    ];

    private static RequirementCategory MapCategory(int gate, string text)
    {
        if (gate == 4) return RequirementCategory.NonFunctional;
        if (gate == 3) return RequirementCategory.BusinessRule;

        var normalized = text.ToLowerInvariant();
        if (gate == 2 &&
            (normalized.Contains("security") || normalized.Contains("authorization") ||
             normalized.Contains("bảo mật") || normalized.Contains("phân quyền")))
        {
            return RequirementCategory.Constraint;
        }

        return RequirementCategory.Functional;
    }

    private static PersonaDifficulty MapDifficulty(string? difficulty) =>
        difficulty?.Trim().ToLowerInvariant() switch
        {
            "easy" => PersonaDifficulty.Easy,
            "hard" => PersonaDifficulty.Hard,
            _ => PersonaDifficulty.Medium
        };

    private static string NormalizeRequirementType(string? value) =>
        value?.Trim().ToUpperInvariant() is "FR" or "NFR" or "BR"
            ? value.Trim().ToUpperInvariant()
            : "FR";

    private static string NormalizePriority(string? value) =>
        value?.Trim().ToLowerInvariant() is "high" or "medium" or "low"
            ? value.Trim().ToLowerInvariant()
            : "medium";

    [GeneratedRegex("[^a-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidScenarioKeyCharacters();

    [GeneratedRegex("_+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedUnderscores();
}
