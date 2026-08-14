using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Data;

internal static class ScenarioCatalogSeeder
{
    private static readonly HashSet<string> BaselineScenarioKeys =
    [
        "university_course_registration",
        "hospital_appointment",
        "small_business_inventory"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<IReadOnlyList<Guid>> SeedAdditionalAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Data", "ScenarioCatalog");
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Scenario catalog directory was not published: {Directory}", directory);
            return [];
        }

        var scenarioIds = new List<Guid>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(item => item, StringComparer.Ordinal))
        {
            var raw = await File.ReadAllTextAsync(path, cancellationToken);
            var catalog = JsonSerializer.Deserialize<CatalogScenario>(raw, JsonOptions)
                ?? throw new InvalidOperationException($"Scenario catalog file is empty: {path}");
            Validate(catalog, path);
            if (BaselineScenarioKeys.Contains(catalog.ScenarioKey))
            {
                continue;
            }

            var scenario = db.Scenarios.Local.FirstOrDefault(item => item.ScenarioKey == catalog.ScenarioKey)
                ?? await db.Scenarios.FirstOrDefaultAsync(
                    item => item.ScenarioKey == catalog.ScenarioKey,
                    cancellationToken);
            if (scenario is null)
            {
                scenario = new Scenario
                {
                    Id = DeterministicGuid($"scenario:{catalog.ScenarioKey}"),
                    ScenarioKey = catalog.ScenarioKey,
                    CreatedAt = DateTime.UtcNow
                };
                db.Scenarios.Add(scenario);
            }

            scenario.Title = catalog.ScenarioTitle;
            scenario.Description = catalog.Context;
            scenario.Domain = catalog.Domain;
            scenario.Difficulty = MapDifficulty(catalog.Difficulty);
            scenario.Version = 1;
            scenario.IsActive = true;
            scenario.PublishedAt = scenario.PublishedAt == default ? scenario.CreatedAt : scenario.PublishedAt;
            scenario.ConfigHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            scenario.SerializedConfig = raw;
            scenario.SourceUrlsData = JsonSerializer.Serialize(catalog.SourceUrls ?? []);
            scenarioIds.Add(scenario.Id);

            foreach (var item in catalog.Requirements)
            {
                var requirementId = DeterministicGuid(
                    $"requirement:{catalog.ScenarioKey}:{item.Id}");
                var requirement = db.HiddenRequirements.Local.FirstOrDefault(row => row.Id == requirementId)
                    ?? await db.HiddenRequirements.FirstOrDefaultAsync(
                        row => row.Id == requirementId,
                        cancellationToken);
                if (requirement is null)
                {
                    requirement = new HiddenRequirement
                    {
                        Id = requirementId,
                        ScenarioId = scenario.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.HiddenRequirements.Add(requirement);
                }

                requirement.ScenarioId = scenario.Id;
                requirement.RequirementText = item.Text;
                requirement.Category = MapCategory(item.Type, item.Gate);
                requirement.RevealDifficulty = MapDifficulty(item.RevealDifficulty);
                requirement.RevealCondition = item.RevealCondition;
                requirement.GateOrder = item.Gate;
                requirement.Actor = item.Actor;
                requirement.Action = item.Action;
                requirement.Object = item.Object;
                requirement.Condition = item.Condition;
                requirement.RequirementType = NormalizeRequirementType(item.Type);
                requirement.Priority = NormalizePriority(item.Priority);
                requirement.NormalizedRequirementData = JsonSerializer.Serialize(new
                {
                    actor = item.Actor,
                    action = item.Action,
                    @object = item.Object,
                    condition = item.Condition,
                    type = NormalizeRequirementType(item.Type),
                    priority = NormalizePriority(item.Priority)
                });
            }
        }

        logger.LogInformation(
            "Loaded {ScenarioCount} additional scenarios from the versioned catalog.",
            scenarioIds.Count);
        return scenarioIds;
    }

    private static void Validate(CatalogScenario scenario, string path)
    {
        if (string.IsNullOrWhiteSpace(scenario.ScenarioKey) ||
            string.IsNullOrWhiteSpace(scenario.ScenarioTitle) ||
            scenario.Requirements.Count == 0)
        {
            throw new InvalidOperationException($"Invalid scenario catalog file: {path}");
        }
        if (!string.Equals(scenario.ReviewStatus, "provisional", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scenario.ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Scenario is not eligible for seeding: {path}");
        }
    }

    private static RequirementCategory MapCategory(string? value, int gate) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "NON_FUNCTIONAL" or "NFR" => RequirementCategory.NonFunctional,
            "BUSINESS_RULE" or "BR" => RequirementCategory.BusinessRule,
            "CONSTRAINT" => RequirementCategory.Constraint,
            "FUNCTIONAL" or "FR" => RequirementCategory.Functional,
            _ when gate == 4 => RequirementCategory.NonFunctional,
            _ when gate == 3 => RequirementCategory.BusinessRule,
            _ => RequirementCategory.Functional
        };

    private static PersonaDifficulty MapDifficulty(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "easy" => PersonaDifficulty.Easy,
            "hard" => PersonaDifficulty.Hard,
            _ => PersonaDifficulty.Medium
        };

    private static string NormalizeRequirementType(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "NON_FUNCTIONAL" or "NFR" or "CONSTRAINT" => "NFR",
            "BUSINESS_RULE" or "BR" => "BR",
            _ => "FR"
        };

    private static string NormalizePriority(string? value) =>
        value?.Trim().ToLowerInvariant() is "high" or "medium" or "low"
            ? value.Trim().ToLowerInvariant()
            : "medium";

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed class CatalogScenario
    {
        [JsonPropertyName("scenario_key")] public string ScenarioKey { get; init; } = "";
        [JsonPropertyName("scenario_title")] public string ScenarioTitle { get; init; } = "";
        [JsonPropertyName("context")] public string Context { get; init; } = "";
        [JsonPropertyName("domain")] public string Domain { get; init; } = "";
        [JsonPropertyName("difficulty")] public string Difficulty { get; init; } = "Medium";
        [JsonPropertyName("source_kind")] public string SourceKind { get; init; } = "synthetic";
        [JsonPropertyName("review_status")] public string ReviewStatus { get; init; } = "provisional";
        [JsonPropertyName("source_urls")] public List<string>? SourceUrls { get; init; }
        [JsonPropertyName("requirements")] public List<CatalogRequirement> Requirements { get; init; } = [];
    }

    private sealed class CatalogRequirement
    {
        [JsonPropertyName("id")] public string Id { get; init; } = "";
        [JsonPropertyName("text")] public string Text { get; init; } = "";
        [JsonPropertyName("gate")] public int Gate { get; init; }
        [JsonPropertyName("reveal_condition")] public string RevealCondition { get; init; } = "";
        [JsonPropertyName("reveal_difficulty")] public string RevealDifficulty { get; init; } = "Medium";
        [JsonPropertyName("actor")] public string? Actor { get; init; }
        [JsonPropertyName("action")] public string? Action { get; init; }
        [JsonPropertyName("object")] public string? Object { get; init; }
        [JsonPropertyName("condition")] public string? Condition { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("priority")] public string? Priority { get; init; }
    }
}
