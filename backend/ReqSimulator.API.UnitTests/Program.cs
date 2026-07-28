using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ReqSimulator.API.Data;
using Microsoft.Extensions.Logging.Abstractions;
using ReqSimulator.API.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ai_client_null_chat_response_becomes_fallback", TestNullChatResponseAsync),
    ("ai_client_null_extract_response_becomes_fallback", TestNullExtractResponseAsync),
    ("legacy_sha256_password_verifies_and_requests_upgrade", TestLegacySha256Password),
    ("schema_bootstrap_never_deletes_duplicate_evaluations", TestSchemaBootstrapIsNonDestructive),
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
static AiServiceClient CreateClient()
{
    var httpClient = new HttpClient(new NullJsonHandler())
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
