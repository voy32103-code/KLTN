namespace ReqSimulator.API.Services;

public static class AiModelCatalog
{
    public const string DefaultModel = "gemini-2.5-flash";

    private static readonly HashSet<string> SupportedModels = new(StringComparer.Ordinal)
    {
        DefaultModel,
        "gemini-2.5-pro",
        "llama-3.3-70b-versatile",
        "llama-3.1-8b-instant",
        "deepseek-chat",
        "deepseek-v4flash",
        "deepseek-v4pro",
        "mimo-v2.5pro",
        "openrouter/meta-llama/llama-3.3-70b-instruct",
        "openrouter/deepseek/deepseek-chat",
        "openrouter/google/gemini-2.5-flash"
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
