using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReqSimulator.API.Data;
using Microsoft.Extensions.Logging.Abstractions;
using ReqSimulator.API.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ai_client_null_chat_response_becomes_fallback", TestNullChatResponseAsync),
    ("ai_client_null_extract_response_becomes_fallback", TestNullExtractResponseAsync),
    ("legacy_sha256_password_verifies_and_requests_upgrade", TestLegacySha256Password),
    ("schema_bootstrap_never_deletes_duplicate_evaluations", TestSchemaBootstrapIsNonDestructive),
    ("extract_request_serializes_scenario_glossary", TestExtractRequestSerializesGlossaryAsync),
    ("pipeline_enhancement_migration_is_versioned_and_non_destructive", TestPipelineMigrationIsVersioned),
    ("published_scenario_catalog_contains_10_scenarios_and_100_requirements", TestPublishedScenarioCatalog),
    ("scenario_localization_catalog_covers_vi_and_en", TestScenarioLocalizationCatalog),
    ("ai_model_catalog_accepts_verified_gemini_fallback_models", TestGeminiFallbackModelCatalog),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

return failures == 0 ? 0 : 1;

static async Task TestNullChatResponseAsync()
{
    var client = CreateClient();
    var result = await client.Chat(new AiChatRequest(
        "session",
        "Scenario",
        "question",
        [],
        new PersonaProfile("Name", "Role", "{}", "neutral", "neutral", 1m),
        null,
        [],
        null));

    Assert(result is not null && result.IsFallback, "null chat payload must produce an explicit fallback");
}

static async Task TestNullExtractResponseAsync()
{
    var client = CreateClient();
    var result = await client.ExtractRequirements(new AiExtractRequest("session", [], null));
    Assert(result is not null && result.IsFallback, "null extract payload must produce an explicit fallback");
}

static Task TestLegacySha256Password()
{
    const string password = "legacy-password";
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
    var method = typeof(AuthService).GetMethod("VerifyPassword", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("VerifyPassword helper not found.");
    var arguments = new object?[] { password, hash, false };
    var valid = (bool)(method.Invoke(null, arguments) ?? false);
    Assert(valid, "legacy SHA-256 password must verify");
    Assert(arguments[2] is true, "legacy SHA-256 password must be marked for BCrypt upgrade");
    return Task.CompletedTask;
}

static Task TestSchemaBootstrapIsNonDestructive()
{
    var field = typeof(SchemaBootstrapper).GetField(
        "CleanAndEnforceUniqueSql",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Schema bootstrap SQL not found.");
    var sql = (string)(field.GetRawConstantValue() ?? "");
    Assert(!sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
        "schema bootstrap must not delete duplicate evaluation data");
    Assert(sql.Contains("RAISE EXCEPTION", StringComparison.OrdinalIgnoreCase),
        "schema bootstrap must fail explicitly when duplicate data exists");
    return Task.CompletedTask;
}

static async Task TestExtractRequestSerializesGlossaryAsync()
{
    var handler = new StaticJsonHandler("{\"requirements\":[],\"isFallback\":false}");
    var client = CreateClient(handler);
    var glossary = new Dictionary<string, Dictionary<string, string>>
    {
        ["object"] = new() { ["desk pass"] = "study desk" }
    };
    var result = await client.ExtractRequirements(new AiExtractRequest("session", [], null, glossary));
    Assert(!result.IsFallback, "valid extract response should not become fallback");
    Assert(handler.LastRequestBody?.Contains("normalizationGlossary", StringComparison.Ordinal) == true,
        "extract contract must forward the scenario glossary");
    Assert(handler.LastRequestBody?.Contains("study desk", StringComparison.Ordinal) == true,
        "extract contract must preserve the reviewed glossary entry");
}

static Task TestPipelineMigrationIsVersioned()
{
    var type = typeof(PipelineEnhancementSchemaMigration);
    var version = (string)(type.GetField("Version", BindingFlags.NonPublic | BindingFlags.Static)
        ?.GetRawConstantValue() ?? "");
    var sql = (string)(type.GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)
        ?.GetRawConstantValue() ?? "");
    Assert(version.Contains("pipeline_enhancements", StringComparison.Ordinal),
        "pipeline changes must use a named schema migration version");
    Assert(sql.Contains("persona_templates", StringComparison.Ordinal) &&
           sql.Contains("scenario_review_audits", StringComparison.Ordinal),
        "migration must create reusable personas and review evidence tables");
    Assert(!sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
        "pipeline migration must not delete existing scenario data");
    return Task.CompletedTask;
}

static Task TestPublishedScenarioCatalog()
{
    var assemblyDirectory = Path.GetDirectoryName(typeof(SeedData).Assembly.Location)
        ?? throw new InvalidOperationException("Could not resolve API assembly directory.");
    var catalogDirectory = Path.Combine(assemblyDirectory, "Data", "ScenarioCatalog");
    var files = Directory.GetFiles(catalogDirectory, "*.json").OrderBy(path => path).ToArray();
    Assert(files.Length == 10, $"published catalog must have 10 scenarios, found {files.Length}");
    var requirementCount = files.Sum(path =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("requirements").GetArrayLength();
    });
    Assert(requirementCount == 100,
        $"published catalog must have 100 requirements, found {requirementCount}");
    return Task.CompletedTask;
}

static Task TestScenarioLocalizationCatalog()
{
    var catalog = new ScenarioLocalizationCatalog(NullLogger<ScenarioLocalizationCatalog>.Instance);
    var vietnamese = catalog.Resolve(
        "bank_loan_application",
        "fallback",
        "fallback",
        "fallback",
        "vi");
    var english = catalog.Resolve(
        "bank_loan_application",
        "fallback",
        "fallback",
        "fallback",
        "en");
    Assert(vietnamese.Title == "Quy trình Đăng ký Vay Ngân hàng",
        "Vietnamese scenario title must come from the localization catalog");
    Assert(english.Title == "Bank Loan Application Workflow",
        "English scenario title must come from the localization catalog");
    Assert(ScenarioLocalizationCatalog.NormalizeLanguage("fr") == "vi",
        "unsupported languages must fail safely to Vietnamese");

    var assemblyDirectory = Path.GetDirectoryName(typeof(SeedData).Assembly.Location)
        ?? throw new InvalidOperationException("Could not resolve API assembly directory.");
    var path = Path.Combine(
        assemblyDirectory,
        "Data",
        "ScenarioLocalizations",
        "scenarios.i18n.json");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var scenarios = document.RootElement.GetProperty("scenarios");
    Assert(scenarios.EnumerateObject().Count() == 10,
        "localization catalog must cover all 10 scenarios");
    foreach (var scenario in scenarios.EnumerateObject())
    {
        Assert(scenario.Value.TryGetProperty("vi", out _) && scenario.Value.TryGetProperty("en", out _),
            $"scenario {scenario.Name} must provide both vi and en");
    }
    return Task.CompletedTask;
}

static Task TestGeminiFallbackModelCatalog()
{
    var models = new[]
    {
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemini-3-flash-preview",
        "gemini-3.1-flash-lite",
        "gemini-3.5-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.6-flash",
        "gemini-3.7-flash"
    };
    foreach (var model in models)
    {
        Assert(AiModelCatalog.IsSupported(model), $"Gemini fallback model must be supported: {model}");
        Assert(AiModelCatalog.IsGemini(model), $"Gemini fallback model must route to Gemini: {model}");
    }
    Assert(!AiModelCatalog.IsSupported("gemini-3-flash"),
        "display name must not be accepted as an API model ID; use gemini-3-flash-preview");
    return Task.CompletedTask;
}
static AiServiceClient CreateClient(HttpMessageHandler? handler = null)
{
    var httpClient = new HttpClient(handler ?? new NullJsonHandler())
    {
        BaseAddress = new Uri("http://ai-service.test")
    };
    return new AiServiceClient(httpClient, NullLogger<AiServiceClient>.Instance);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class StaticJsonHandler(string json) : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
sealed class NullJsonHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        });
}
