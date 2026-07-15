using System.Text.Json;

namespace ReqSimulator.API.Services;

/// <summary>
/// HTTP client gọi sang Python AI Service (FastAPI).
/// Mọi AI processing (chat, extract, evaluate) đều đi qua service này.
/// Tất cả các phương thức đều bọc try-catch để tránh crash 500 (gây lỗi CORS giả trên Vercel).
/// </summary>
public class AiServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiServiceClient> _logger;

    private const string ColdStartMessage =
        "Stakeholder ảo đang suy nghĩ hơi lâu do máy chủ bận (khởi động lạnh). " +
        "Bạn vui lòng đợi khoảng 10 giây và thử gửi lại tin nhắn nhé!";

    private const string ExtractFallbackMessage =
        "Không thể trích xuất yêu cầu lúc này do máy chủ AI đang khởi động lạnh. " +
        "Vui lòng thử lại sau ít giây.";

    private const string EvaluateFallbackMessage =
        "Không thể đánh giá lúc này do máy chủ AI đang khởi động lạnh. " +
        "Vui lòng thử lại sau ít giây.";

    public AiServiceClient(HttpClient http, ILogger<AiServiceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Gửi tin nhắn student đến AI, nhận response từ stakeholder</summary>
    public async Task<AiChatResponse> Chat(AiChatRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await _http.PostAsJsonAsync("/api/chat", request, cts.Token);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AiChatResponse>(cts.Token))!;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI Service /api/chat không phản hồi (HttpRequestException). SessionId={SessionId}", request.SessionId);
            return CreateChatFallback();
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "AI Service /api/chat bị timeout (Cold Start). SessionId={SessionId}", request.SessionId);
            return CreateChatFallback();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI Service /api/chat trả về JSON không hợp lệ. SessionId={SessionId}", request.SessionId);
            return CreateChatFallback();
        }
    }

    /// <summary>Yêu cầu AI trích xuất requirements từ conversation</summary>
    public async Task<AiExtractResponse> ExtractRequirements(AiExtractRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await _http.PostAsJsonAsync("/api/extract", request, cts.Token);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AiExtractResponse>(cts.Token))!;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI Service /api/extract không phản hồi. SessionId={SessionId}", request.SessionId);
            return new AiExtractResponse([new ExtractedReq(ExtractFallbackMessage, 0m)]);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "AI Service /api/extract bị timeout. SessionId={SessionId}", request.SessionId);
            return new AiExtractResponse([new ExtractedReq(ExtractFallbackMessage, 0m)]);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI Service /api/extract trả về JSON không hợp lệ. SessionId={SessionId}", request.SessionId);
            return new AiExtractResponse([new ExtractedReq(ExtractFallbackMessage, 0m)]);
        }
    }

    /// <summary>So sánh extracted vs hidden requirements, tính coverage</summary>
    public async Task<AiEvaluateResponse> Evaluate(AiEvaluateRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await _http.PostAsJsonAsync("/api/evaluate", request, cts.Token);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AiEvaluateResponse>(cts.Token))!;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI Service /api/evaluate không phản hồi.");
            return CreateEvaluateFallback(request);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "AI Service /api/evaluate bị timeout.");
            return CreateEvaluateFallback(request);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI Service /api/evaluate trả về JSON không hợp lệ.");
            return CreateEvaluateFallback(request);
        }
    }

    // ===== Fallback Factory Methods =====

    private static AiChatResponse CreateChatFallback() => new(
        StakeholderReply: ColdStartMessage,
        DetectedQuestionType: null,
        StateUpdate: null
    );

    private static AiEvaluateResponse CreateEvaluateFallback(AiEvaluateRequest request) => new(
        CoverageScore: 0m,
        Matches: request.HiddenRequirements.Select(h => new ReqMatch(
            h.Id, h.Text, null, 0m, "Missed", EvaluateFallbackMessage)).ToList(),
        Feedback: new FeedbackData(
            Strengths: [],
            Weaknesses: [EvaluateFallbackMessage],
            Suggestions: ["Vui lòng đợi AI Service khởi động xong rồi kết thúc session lại."],
            DesignSuggestions: null),
        ScoringPolicy: null
    );
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
public record DesignSuggestionsData(
    string UseCaseMermaid,
    string ErdMermaid,
    List<string> MainActors,
    List<string> MainEntities
);

public record FeedbackData(
    List<string> Strengths,
    List<string> Weaknesses,
    List<string> Suggestions,
    DesignSuggestionsData? DesignSuggestions
);

public record ScoringPolicyData(
    string Preset,
    decimal ExactThreshold,
    decimal SemanticThreshold,
    decimal PartialThreshold,
    bool RubricPartialMatcher,
    string EmbeddingModel);
