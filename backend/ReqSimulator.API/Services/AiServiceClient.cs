namespace ReqSimulator.API.Services;

/// <summary>
/// HTTP client gọi sang Python AI Service (FastAPI).
/// Mọi AI processing (chat, extract, evaluate) đều đi qua service này.
/// </summary>
public class AiServiceClient
{
    private readonly HttpClient _http;

    public AiServiceClient(HttpClient http) => _http = http;

    /// <summary>Gửi tin nhắn student đến AI, nhận response từ stakeholder</summary>
    public async Task<AiChatResponse> Chat(AiChatRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/chat", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiChatResponse>())!;
    }

    /// <summary>Yêu cầu AI trích xuất requirements từ conversation</summary>
    public async Task<AiExtractResponse> ExtractRequirements(AiExtractRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/extract", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiExtractResponse>())!;
    }

    /// <summary>So sánh extracted vs hidden requirements, tính coverage</summary>
    public async Task<AiEvaluateResponse> Evaluate(AiEvaluateRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/evaluate", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiEvaluateResponse>())!;
    }
}

// ===== DTOs cho giao tiếp Backend ↔ AI Service =====

public record AiChatRequest(
    string SessionId,
    string? ScenarioTitle,
    string StudentMessage,
    List<ChatMessage> History,
    PersonaProfile Persona,
    string? PersonaStateJson,
    List<string> AvailableRequirements  // chat_service sẽ gate trước khi đưa vào prompt
);

public record AiChatResponse(
    string StakeholderReply,
    string? DetectedQuestionType,
    PersonaStateUpdate? StateUpdate
);

public record AiExtractRequest(string SessionId, List<ChatMessage> History);
public record AiExtractResponse(List<ExtractedReq> Requirements);

public record AiEvaluateRequest(
    List<ExtractedReq> Extracted,
    List<HiddenReq> HiddenRequirements
);
public record AiEvaluateResponse(
    decimal CoverageScore,
    List<ReqMatch> Matches,
    FeedbackData Feedback,
    ScoringPolicyData? ScoringPolicy
);

// Sub-DTOs
public record ChatMessage(string Role, string Content, DateTime Timestamp);
public record PersonaProfile(string Name, string RoleTitle, string Traits, string Style, string Mood, decimal Patience);
public record PersonaStateUpdate(string Mood, decimal Patience, int TurnCount, List<string> NewlyRevealed);
public record ExtractedReq(string Text, decimal Confidence);
public record HiddenReq(string Id, string Text, string Category);
public record ReqMatch(string HiddenId, string? HiddenText, string? ExtractedText, decimal Score, string MatchType, string Reason);
public record FeedbackData(List<string> Strengths, List<string> Weaknesses, List<string> Suggestions);
public record ScoringPolicyData(
    string Preset,
    decimal ExactThreshold,
    decimal SemanticThreshold,
    decimal PartialThreshold,
    bool RubricPartialMatcher,
    string EmbeddingModel);
