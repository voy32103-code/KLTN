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
    private readonly AppDbContext _db;

    public ScenarioVersionPublisher(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Scenario> PublishAsync(
        ScenarioConfigJson config,
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

        var serializedConfig = JsonSerializer.Serialize(config);
        var configHash = ComputeHash(serializedConfig);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // Serialize publishers for the same logical scenario across application instances.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({scenarioKey}))",
            cancellationToken);

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
            Domain = "General",
            Difficulty = PersonaDifficulty.Medium,
            Version = maxVersion + 1,
            IsActive = true,
            CreatedAt = now,
            PublishedAt = now,
            ConfigHash = configHash,
            SerializedConfig = serializedConfig,
            SourceUrlsData = JsonSerializer.Serialize(
                (config.SourceUrls ?? [])
                    .Where(url => Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList())
        };

        var stakeholderTemplates = new[]
        {
            ("Business Owner", "Decision Maker", "Management"),
            ("Process Expert", "Domain Specialist", "Operations"),
            ("End User", "Operational User", "Delivery")
        };
        var personaTemplates = new[]
        {
            ("Collaborative", "collaborative", "high", PersonaDifficulty.Easy, 1.00m),
            ("Challenging", "concise", "medium", PersonaDifficulty.Hard, 0.70m)
        };
        foreach (var role in stakeholderTemplates)
        {
            var stakeholder = new Stakeholder
            {
                Id = Guid.NewGuid(), ScenarioId = next.Id, Name = role.Item1,
                RoleTitle = role.Item2, Department = role.Item3,
                Description = $"Represents the {role.Item3} viewpoint.", CreatedAt = now
            };
            next.Stakeholders.Add(stakeholder);
            foreach (var profile in personaTemplates)
            {
                var persona = new Persona
                {
                    Id = Guid.NewGuid(), ScenarioId = next.Id, StakeholderId = stakeholder.Id,
                    Name = $"{role.Item1} - {profile.Item1}", Label = profile.Item1,
                    RoleTitle = role.Item2,
                    PersonalityTraits = JsonSerializer.Serialize(new
                    {
                        traits = new[] { profile.Item1.ToLowerInvariant(), "detail_oriented" },
                        jargon_level = profile.Item3
                    }),
                    CommunicationStyle = profile.Item2, KnowledgeLevel = profile.Item3,
                    Difficulty = profile.Item4, InitialMood = "neutral",
                    InitialPatience = profile.Item5, CreatedAt = now
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
