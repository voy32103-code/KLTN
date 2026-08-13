using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReqSimulator.API.Services;

/// <summary>Typed client for the supported AI-service chat, extract, and evaluate endpoints.</summary>
public class AiServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiServiceClient> _logger;

    private const string ColdStartMessage =
        "Hệ thống đối tác ảo hiện đang bận hoặc tạm ngưng. Vui lòng đợi vài giây và thử lại.";
    private const string ExtractFallbackMessage =
        "Tính năng trích xuất yêu cầu hiện không khả dụng. Vui lòng thử lại sau.";
    private const string EvaluateFallbackMessage =
        "Dịch vụ chấm điểm hiện không khả dụng. Vui lòng thử lại sau.";

    public AiServiceClient(HttpClient http, ILogger<AiServiceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AiChatResponse> Chat(AiChatRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await _http.PostAsJsonAsync("/api/chat", request, cts.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AiChatResponse>(cts.Token);
            return result ?? CreateChatFallback();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "AI Service chat request failed. SessionId={SessionId}", request.SessionId);
            return CreateChatFallback();
        }
    }

    public async Task<AiExtractResponse> ExtractRequirements(AiExtractRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await _http.PostAsJsonAsync("/api/extract", request, cts.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AiExtractResponse>(cts.Token);
            return result ?? CreateExtractFallback();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "AI Service extraction request failed. SessionId={SessionId}", request.SessionId);
            return CreateExtractFallback();
        }
    }

    public async Task<AiEvaluateResponse> Evaluate(AiEvaluateRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await _http.PostAsJsonAsync("/api/evaluate", request, cts.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AiEvaluateResponse>(cts.Token);
            return result ?? CreateEvaluateFallback(request);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "AI Service evaluation request failed.");
            return CreateEvaluateFallback(request);
        }
    }

    private static AiChatResponse CreateChatFallback() => new(
        StakeholderReply: ColdStartMessage,
        DetectedQuestionType: null,
        StateUpdate: null,
        IsFallback: true);

    private static AiExtractResponse CreateExtractFallback() =>
        new([new ExtractedReq(ExtractFallbackMessage, 0m)], IsFallback: true);

    private static AiEvaluateResponse CreateEvaluateFallback(AiEvaluateRequest request) => new(
        CoverageScore: 0m,
        Matches: request.HiddenRequirements.Select(hidden => new ReqMatch(
            hidden.Id, hidden.Text, null, 0m, "Missed", EvaluateFallbackMessage)).ToList(),
        Feedback: new FeedbackData([], [EvaluateFallbackMessage], [EvaluateFallbackMessage], null),
        ScoringPolicy: null,
        IsFallback: true);
}

public record AiChatRequest(
    string SessionId,
    string? ScenarioTitle,
    string StudentMessage,
    List<ChatMessage> History,
    PersonaProfile Persona,
    string? PersonaStateJson,
    List<string> AvailableRequirements,
    string? SelectedModel,
    ScenarioConfigJson? ScenarioConfig = null);

public record AiChatResponse(
    string StakeholderReply,
    string? DetectedQuestionType,
    PersonaStateUpdate? StateUpdate,
    bool IsFallback = false,
    string? DetectedTopic = null,
    string? QuestionQuality = null);

public record AiExtractRequest(
    string SessionId,
    List<ChatMessage> History,
    string? SelectedModel,
    Dictionary<string, Dictionary<string, string>>? NormalizationGlossary = null);
public record AiExtractResponse(
    List<ExtractedReq> Requirements,
    bool IsFallback = false,
    List<NormalizedRequirementData>? NormalizedRequirements = null);

public record AiEvaluateRequest(
    List<ExtractedReq> Extracted,
    List<HiddenReq> HiddenRequirements,
    string? SelectedModel,
    string? ScenarioDescription = null,
    string FeedbackVariant = "A",
    Dictionary<string, Dictionary<string, string>>? NormalizationGlossary = null);
public record AiEvaluateResponse(
    decimal CoverageScore,
    List<ReqMatch> Matches,
    FeedbackData Feedback,
    ScoringPolicyData? ScoringPolicy,
    bool IsFallback = false,
    int ExtraExtractedCount = 0);

public record ScenarioRequirementRuleJson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("gate")] int Gate,
    [property: JsonPropertyName("keywords")] List<string> Keywords,
    [property: JsonPropertyName("question_types")] List<string> QuestionTypes,
    [property: JsonPropertyName("reveal_condition")] string RevealCondition,
    [property: JsonPropertyName("reveal_difficulty")] string RevealDifficulty,
    [property: JsonPropertyName("requires")] List<string>? Requires,
    [property: JsonPropertyName("actor")] string? Actor = null,
    [property: JsonPropertyName("action")] string? Action = null,
    [property: JsonPropertyName("object")] string? Object = null,
    [property: JsonPropertyName("condition")] string? Condition = null,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("priority")] string? Priority = null);

public record ScenarioConfigJson(
    [property: JsonPropertyName("scenario_key")] string ScenarioKey,
    [property: JsonPropertyName("scenario_title")] string ScenarioTitle,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("general_keywords")] List<string> GeneralKeywords,
    [property: JsonPropertyName("gate_keyword_groups")] Dictionary<string, List<string>> GateKeywordGroups,
    [property: JsonPropertyName("question_type_gate_map")] Dictionary<string, List<int>> QuestionTypeGateMap,
    [property: JsonPropertyName("max_new_reveals_per_turn")] int MaxNewRevealsPerTurn,
    [property: JsonPropertyName("requirements")] List<ScenarioRequirementRuleJson> Requirements,
    [property: JsonPropertyName("source_urls")] List<string>? SourceUrls = null,
    [property: JsonPropertyName("persona_template_keys")] List<string>? PersonaTemplateKeys = null,
    [property: JsonPropertyName("normalization_glossary")] Dictionary<string, Dictionary<string, string>>? NormalizationGlossary = null,
    [property: JsonPropertyName("review_notes")] string? ReviewNotes = null);

public record ChatMessage(string Role, string Content, DateTime Timestamp);
public record PersonaProfile(string Name, string RoleTitle, string Traits, string Style, string Mood, decimal Patience);
public record PersonaStateUpdate(string Mood, decimal Patience, int TurnCount, List<string> NewlyRevealed);
public record ExtractedReq(
    string Text,
    decimal Confidence,
    string? Actor = null,
    string? Action = null,
    string? Object = null,
    string? Condition = null,
    string? Type = null,
    string? Priority = null);
public record StructuredRequirementData(
    string Id,
    string Actor,
    string Action,
    string Object,
    string? Condition,
    string Type,
    string Priority,
    decimal Confidence,
    string? RawText);
public record NormalizedRequirementData(
    string Id,
    string ActorNormalized,
    string ActionNormalized,
    string ObjectNormalized,
    string? ConditionNormalized,
    string Type,
    string Priority,
    decimal Confidence,
    string CanonicalKey,
    string CanonicalText,
    StructuredRequirementData Original);
public record HiddenReq(
    string Id,
    string Text,
    string Category,
    string? Actor = null,
    string? Action = null,
    string? Object = null,
    string? Condition = null,
    string? Type = null,
    string? Priority = null);
public record ReqMatch(
    string HiddenId,
    string? HiddenText,
    string? ExtractedText,
    decimal Score,
    string MatchType,
    string Reason,
    Dictionary<string, decimal>? ComponentScores = null);
public record DesignSuggestionsData(
    string UseCaseMermaid,
    string ErdMermaid,
    List<string> MainActors,
    List<string> MainEntities,
    string ValidationStatus = "valid",
    List<string>? ValidationErrors = null);
public record FeedbackData(
    List<string> Strengths,
    List<string> Weaknesses,
    List<string> Suggestions,
    DesignSuggestionsData? DesignSuggestions,
    List<string>? ExtractionsToReview = null,
    string ExperimentVariant = "A");
public record ScoringPolicyData(
    string Preset,
    decimal ExactThreshold,
    decimal SemanticThreshold,
    decimal PartialThreshold,
    bool RubricPartialMatcher,
    string EmbeddingModel,
    string MatchingMethod = "semantic_similarity",
    decimal ActorWeight = 0.20m,
    decimal ActionWeight = 0.30m,
    decimal ObjectWeight = 0.30m,
    decimal ConditionWeight = 0.20m,
    decimal MatchThreshold = 0.80m);
