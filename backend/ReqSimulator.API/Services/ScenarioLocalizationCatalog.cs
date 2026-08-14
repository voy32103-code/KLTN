using System.Text.Json;

namespace ReqSimulator.API.Services;

public sealed class ScenarioLocalizationCatalog
{
    public const string DefaultLanguage = "vi";
    private static readonly HashSet<string> SupportedLanguages = ["vi", "en"];
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ScenarioDisplayText>> _scenarios;

    public ScenarioLocalizationCatalog(ILogger<ScenarioLocalizationCatalog> logger)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "ScenarioLocalizations",
            "scenarios.i18n.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Scenario localization catalog was not published: {Path}", path);
            _scenarios = new Dictionary<string, IReadOnlyDictionary<string, ScenarioDisplayText>>();
            return;
        }

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<LocalizationDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        _scenarios = document?.Scenarios
            .ToDictionary(
                item => item.Key,
                item => (IReadOnlyDictionary<string, ScenarioDisplayText>)item.Value,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, IReadOnlyDictionary<string, ScenarioDisplayText>>();
    }

    public static string NormalizeLanguage(string? language) =>
        SupportedLanguages.Contains(language?.Trim().ToLowerInvariant() ?? "")
            ? language!.Trim().ToLowerInvariant()
            : DefaultLanguage;

    public LocalizedScenarioDisplay Resolve(
        string scenarioKey,
        string fallbackTitle,
        string fallbackDescription,
        string? fallbackDomain,
        string? language)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        if (_scenarios.TryGetValue(scenarioKey, out var translations) &&
            translations.TryGetValue(normalizedLanguage, out var translation))
        {
            return new LocalizedScenarioDisplay(
                translation.Title,
                translation.Description,
                translation.Domain,
                normalizedLanguage);
        }

        return new LocalizedScenarioDisplay(
            fallbackTitle,
            fallbackDescription,
            fallbackDomain,
            normalizedLanguage);
    }

    private sealed class LocalizationDocument
    {
        public Dictionary<string, Dictionary<string, ScenarioDisplayText>> Scenarios { get; init; } = [];
    }

    private sealed class ScenarioDisplayText
    {
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string? Domain { get; init; }
    }
}

public sealed record LocalizedScenarioDisplay(
    string Title,
    string Description,
    string? Domain,
    string Language);
