namespace ReqSimulator.API.Services;

public static class AiModelCatalog
{
    public const string DefaultModel = "gemini-3.1-flash-lite";

    private static readonly HashSet<string> SupportedModels = new(StringComparer.Ordinal)
    {
        DefaultModel,
        "gemini-2.5-pro",
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemini-3-flash-preview",
        "gemini-3.1-flash-lite",
        "gemini-3.5-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.6-flash",
        "gemini-3.7-flash",
        "llama-3.3-70b-versatile",
        "llama-3.1-8b-instant",
        "deepseek-chat",
        "deepseek-v4flash",
        "deepseek-v4pro",
        "mimo-v2.5pro",
        "openrouter/meta-llama/llama-3.3-70b-instruct",
        "openrouter/deepseek/deepseek-chat",
        "openrouter/google/gemini-2.5-flash",
        "omniroute/kmc/k3",
        "omniroute/kmc/kimi-for-coding",
        "omniroute/kmc/kimi-for-coding-highspeed",
        "omniroute/cp/cline-pass/glm-5.2",
        "omniroute/cp/cline-pass/minimax-m3",
        "omniroute/cp/cline-pass/deepseek-v4-pro",
        "omniroute/cp/cline-pass/deepseek-v4-flash",
        "omniroute/cp/cline-pass/kimi-k3",
        "omniroute/cp/cline-pass/kimi-k2.7-code",
        "omniroute/cp/cline-pass/mimo-v2.5-pro",
        "omniroute/cp/cline-pass/mimo-v2.5",
        "omniroute/cp/cline-pass/qwen3.7-max",
        "omniroute/cp/cline-pass/qwen3.7-plus",
        "omniroute/kr/claude-sonnet-5",
        "omniroute/kr/claude-sonnet-4.5",
        "omniroute/kr/claude-haiku-4.5",
        "omniroute/kr/deepseek-3.2",
        "omniroute/kr/minimax-m2.5",
        "omniroute/kr/minimax-m2.1",
        "omniroute/kr/glm-5",
        "omniroute/kr/qwen3-coder-next",
        "omniroute/kr/gpt-5.6-sol",
        "omniroute/kr/gpt-5.6-terra",
        "omniroute/kr/gpt-5.6-luna"
    };

    public static bool IsSupported(string? model) =>
        string.IsNullOrWhiteSpace(model) || SupportedModels.Contains(model);

    public static bool IsGemini(string? model) =>
        IsSupported(model) &&
        NormalizeOrDefault(model).StartsWith("gemini-", StringComparison.Ordinal);

    public static string NormalizeOrDefault(string? model) =>
        !string.IsNullOrWhiteSpace(model) && SupportedModels.Contains(model)
            ? model
            : DefaultModel;
}
