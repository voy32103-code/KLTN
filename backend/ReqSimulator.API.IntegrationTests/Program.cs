using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReqSimulator.API.Controllers;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.IntegrationTests;

internal static class Program
{
    private const string ScenarioTitle = "University Course Registration System";
    private const string PersonaName = "Ms. Nguyen";
    private const string SuiteName = "ReqSimulator.API.IntegrationTests";
    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> Main()
    {
        var runStartedAtUtc = DateTimeOffset.UtcNow;
        IntegrationRunner? runner = null;

        try
        {
            LoadDotEnvFiles(
                Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                Path.Combine(Directory.GetCurrentDirectory(), "backend", "ReqSimulator.API", ".env"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "ReqSimulator.API", ".env"));

            using var provider = BuildServiceProvider();
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("ReqSimulator.API.IntegrationTests");

            await provider.EnsureOperationalSchemaAsync(logger);
            await provider.SeedScenarioV1Async(logger);

            runner = new IntegrationRunner(provider, logger);
            await runner.RunAsync();

            logger.LogInformation("All backend integration scenarios passed.");
            return 0;
        }
        catch (Exception ex)
        {
            if (runner is null || !runner.HasWrittenSummary)
            {
                try
                {
                    await WriteMachineReadableSummaryAsync(
                        CreateProcessFailureSummary(runStartedAtUtc, ex),
                        null);
                }
                catch (Exception summaryEx)
                {
                    Console.Error.WriteLine($"Failed to write machine-readable summary: {summaryEx}");
                }
            }

            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var verboseSqlLogging = string.Equals(
            Environment.GetEnvironmentVariable("INTEGRATION_VERBOSE_SQL"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);

            if (!verboseSqlLogging)
            {
                builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
                builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Transaction", LogLevel.Warning);
                builder.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.Warning);
            }
        });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ReqSimulator",
                ["Jwt:Audience"] = "ReqSimulator",
                ["Jwt:ExpiresInHours"] = "24"
            })
            .AddEnvironmentVariables()
            .Build());
        services.AddScoped<AuthService>();

        var connectionString = NormalizePostgresConnectionString(
            GetRequiredConfig("ConnectionStrings:DefaultConnection"));
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<UserRole>("user_role");
        dataSourceBuilder.MapEnum<SenderType>("sender_type");
        dataSourceBuilder.MapEnum<RequirementCategory>("requirement_category");
        dataSourceBuilder.MapEnum<ReqSimulator.API.Models.MatchType>("match_type");
        dataSourceBuilder.MapEnum<QuestionType>("question_type");
        dataSourceBuilder.MapEnum<PersonaDifficulty>("persona_difficulty");
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.MapEnum<UserRole>("user_role");
                npgsqlOptions.MapEnum<SenderType>("sender_type");
                npgsqlOptions.MapEnum<RequirementCategory>("requirement_category");
                npgsqlOptions.MapEnum<ReqSimulator.API.Models.MatchType>("match_type");
                npgsqlOptions.MapEnum<QuestionType>("question_type");
                npgsqlOptions.MapEnum<PersonaDifficulty>("persona_difficulty");
            }));

        return services.BuildServiceProvider();
    }

    private static string GetRequiredConfig(string key)
    {
        var value = Environment.GetEnvironmentVariable(key.Replace(':', '_')) ??
                    Environment.GetEnvironmentVariable(key.Replace(":", "__")) ??
                    Environment.GetEnvironmentVariable(key);

        if (string.IsNullOrWhiteSpace(value) || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Missing required configuration value: {key}");

        return value;
    }

    private static void LoadDotEnvFiles(params string[] paths)
    {
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            var path = Path.GetFullPath(rawPath);
            if (!loadedPaths.Add(path) || !File.Exists(path))
                continue;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                    line = line["export ".Length..].TrimStart();

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                if (!string.IsNullOrWhiteSpace(key) && Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static IntegrationRunSummary CreateProcessFailureSummary(
        DateTimeOffset startedAtUtc,
        Exception exception)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        return new IntegrationRunSummary(
            SuiteName,
            "setup_failed",
            Environment.GetEnvironmentVariable("INTEGRATION_SCENARIO_FILTER"),
            string.Equals(
                Environment.GetEnvironmentVariable("INTEGRATION_SUMMARY_ONLY"),
                "true",
                StringComparison.OrdinalIgnoreCase),
            startedAtUtc,
            completedAtUtc,
            0,
            0,
            0,
            1,
            0,
            false,
            (int)(completedAtUtc - startedAtUtc).TotalMilliseconds,
            exception.ToString(),
            []);
    }

    private static string ResolveSummaryJsonPath()
    {
        var rawPath = Environment.GetEnvironmentVariable("INTEGRATION_SUMMARY_JSON_PATH");
        var normalizedPath = string.IsNullOrWhiteSpace(rawPath)
            ? Path.Combine("tools", "logs", "summary.json")
            : rawPath.Trim();

        return Path.GetFullPath(
            Path.IsPathRooted(normalizedPath)
                ? normalizedPath
                : Path.Combine(Directory.GetCurrentDirectory(), normalizedPath));
    }

    private static async Task WriteMachineReadableSummaryAsync(
        IntegrationRunSummary summary,
        ILogger? logger)
    {
        var path = ResolveSummaryJsonPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(summary, SummaryJsonOptions));

        if (logger is not null)
            logger.LogInformation("Wrote machine-readable summary to {Path}", path);
    }

    private static string NormalizePostgresConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return value;
        }

        var credentials = uri.UserInfo.Split(':', 2);
        var username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "";
        var password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "";

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = username,
            Password = password
        };

        foreach (var parameter in ParseQueryString(uri.Query))
        {
            switch (parameter.Key)
            {
                case "sslmode":
                    builder["SSL Mode"] = parameter.Value;
                    break;
                case "channel_binding":
                    builder["Channel Binding"] = parameter.Value;
                    break;
            }
        }

        return builder.ConnectionString;
    }

    private sealed record IntegrationScenarioSummary(
        string Name,
        string Status,
        int DurationMs,
        string? FailureMessage);

    private sealed record IntegrationRunSummary(
        string Suite,
        string Status,
        string? Filter,
        bool SummaryOnlyMode,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        int ExpectedScenarioCount,
        int ExecutedScenarioCount,
        int PassedCount,
        int FailedCount,
        int SkippedCount,
        bool StoppedEarly,
        int TotalDurationMs,
        string? ProcessFailure,
        IReadOnlyList<IntegrationScenarioSummary> Scenarios);

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(normalized))
            return parameters;

        foreach (var part in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
                continue;

            var key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
            var value = Uri.UnescapeDataString(pair[1].Replace("+", " "));
            parameters[key] = value;
        }

        return parameters;
    }

    private sealed class IntegrationRunner(IServiceProvider provider, ILogger logger)
    {
        private readonly List<Guid> _createdUserIds = [];
        private readonly List<Guid> _createdSessionIds = [];
        private readonly List<ScenarioExecutionResult> _scenarioResults = [];
        private FakeAiService? _sharedHttpAiService;
        private FakeAiHttpServer? _sharedHttpAiServer;
        private BackendApiServer? _sharedHttpBackendServer;
        private readonly bool _summaryOnlyMode = string.Equals(
            Environment.GetEnvironmentVariable("INTEGRATION_SUMMARY_ONLY"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        private readonly DateTimeOffset _runStartedAtUtc = DateTimeOffset.UtcNow;

        public bool HasWrittenSummary { get; private set; }

        public async Task RunAsync()
        {
            var scenarioFilter = Environment.GetEnvironmentVariable("INTEGRATION_SCENARIO_FILTER");
            var scenarios = new (string Name, Func<Task> Test)[]
            {
                ("student_happy_path_register_login_create_send_end", TestStudentHappyPathAsync),
                ("http_auth_header_middleware_rejects_missing_invalid_and_forbidden_tokens", TestHttpAuthHeaderMiddlewareAsync),
                ("http_lecturer_and_admin_can_review_hidden_requirements_and_session_history", TestHttpLecturerAdminReviewAccessAsync),
                ("login_with_wrong_password_returns_unauthorized", TestLoginWrongPasswordAsync),
                ("register_duplicate_email_returns_conflict", TestRegisterDuplicateEmailAsync),
                ("send_message_without_user_id_returns_unauthorized", TestUnauthorizedSendMessageAsync),
                ("send_message_as_other_student_returns_forbid", TestForbiddenSendMessageAsync),
                ("send_message_after_session_end_returns_bad_request", TestSendMessageAfterSessionEndedAsync),
                ("end_session_after_session_end_returns_existing_evaluation", TestEndSessionAfterClosedAsync),
                ("duplicate_end_session_returns_two_ok_results_but_runs_ai_once", TestDuplicateEndSessionAsync),
                ("expired_finalization_lease_can_be_reclaimed", TestReclaimAfterLeaseExpiryAsync)
            };
            var matchedScenarios = scenarios
                .Where(s => string.IsNullOrWhiteSpace(scenarioFilter) ||
                            s.Name.Contains(scenarioFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchedScenarios.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No integration scenario matched filter '{scenarioFilter}'.");
            }

            Exception? scenarioException = null;
            Exception? summaryWriteException = null;

            try
            {
                foreach (var (name, test) in matchedScenarios)
                {
                    await RunScenarioAsync(name, test);
                }
            }
            catch (Exception ex)
            {
                scenarioException = ex;
            }
            finally
            {
                try
                {
                    PrintScenarioSummary(matchedScenarios.Length);
                    await WriteScenarioSummaryJsonAsync(matchedScenarios.Length, scenarioFilter);
                    HasWrittenSummary = true;
                }
                catch (Exception ex)
                {
                    summaryWriteException = ex;
                    logger.LogError(ex, "Failed to write machine-readable summary.");
                }
                finally
                {
                    await DisposeSharedHttpTestEnvironmentAsync();
                    await CleanupAsync();
                }
            }

            if (scenarioException is not null)
                ExceptionDispatchInfo.Capture(scenarioException).Throw();

            if (summaryWriteException is not null)
                ExceptionDispatchInfo.Capture(summaryWriteException).Throw();
        }

        private async Task RunScenarioAsync(string name, Func<Task> test)
        {
            if (!_summaryOnlyMode)
                logger.LogInformation("Running scenario: {Scenario}", name);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await test();
                stopwatch.Stop();
                _scenarioResults.Add(new ScenarioExecutionResult(name, true, stopwatch.Elapsed));

                if (!_summaryOnlyMode)
                    logger.LogInformation("Passed scenario: {Scenario}", name);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _scenarioResults.Add(new ScenarioExecutionResult(
                    name,
                    false,
                    stopwatch.Elapsed,
                    ex.Message));
                logger.LogError(ex, "Failed scenario: {Scenario}", name);
                throw;
            }
        }

        private void PrintScenarioSummary(int expectedScenarioCount)
        {
            var passedCount = _scenarioResults.Count(result => result.Passed);
            var failedCount = _scenarioResults.Count(result => !result.Passed);
            var totalDuration = TimeSpan.FromMilliseconds(_scenarioResults.Sum(result => result.Duration.TotalMilliseconds));

            logger.LogInformation(
                "Suite summary: {Passed}/{Expected} passed, {Failed} failed in {DurationMs} ms",
                passedCount,
                expectedScenarioCount,
                failedCount,
                (int)totalDuration.TotalMilliseconds);

            for (var index = 0; index < _scenarioResults.Count; index++)
            {
                var result = _scenarioResults[index];
                var status = result.Passed ? "PASS" : "FAIL";
                var failureSuffix = result.Passed || string.IsNullOrWhiteSpace(result.FailureMessage)
                    ? ""
                    : $" - {result.FailureMessage}";

                logger.LogInformation(
                    "{Index}. {Status} {Scenario} ({DurationMs} ms){FailureSuffix}",
                    index + 1,
                    status,
                    result.Name,
                    (int)result.Duration.TotalMilliseconds,
                    failureSuffix);
            }

            var skippedCount = expectedScenarioCount - _scenarioResults.Count;
            if (skippedCount > 0)
            {
                logger.LogWarning(
                    "{SkippedCount} scenario(s) were not executed because the suite stopped after the first failure.",
                    skippedCount);
            }
        }

        private async Task WriteScenarioSummaryJsonAsync(int expectedScenarioCount, string? scenarioFilter)
        {
            await WriteMachineReadableSummaryAsync(
                BuildRunSummary(expectedScenarioCount, scenarioFilter),
                logger);
        }

        private IntegrationRunSummary BuildRunSummary(int expectedScenarioCount, string? scenarioFilter)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            var passedCount = _scenarioResults.Count(result => result.Passed);
            var failedCount = _scenarioResults.Count(result => !result.Passed);
            var skippedCount = expectedScenarioCount - _scenarioResults.Count;
            var totalDurationMs = (int)_scenarioResults.Sum(result => result.Duration.TotalMilliseconds);
            var status = failedCount == 0 && skippedCount == 0 ? "passed" : "failed";

            return new IntegrationRunSummary(
                SuiteName,
                status,
                scenarioFilter,
                _summaryOnlyMode,
                _runStartedAtUtc,
                completedAtUtc,
                expectedScenarioCount,
                _scenarioResults.Count,
                passedCount,
                failedCount,
                skippedCount,
                skippedCount > 0,
                totalDurationMs,
                null,
                _scenarioResults
                    .Select(result => new IntegrationScenarioSummary(
                        result.Name,
                        result.Passed ? "passed" : "failed",
                        (int)result.Duration.TotalMilliseconds,
                        result.FailureMessage))
                    .ToArray());
        }

        private async Task TestDuplicateEndSessionAsync()
        {
            var scenario = await GetScenarioAsync();
            var persona = await GetPersonaAsync(scenario.Id);
            var user = await CreateTestUserAsync("duplicate-end");
            var session = await CreateSessionWithMessagesAsync(user.Id, scenario.Id, persona.Id);

            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);
            var action1 = RunEndSessionAsync(session.Id, user.Id, aiClient);
            var action2 = RunEndSessionAsync(session.Id, user.Id, aiClient);

            await Task.WhenAll(action1, action2);

            var json1 = AssertOk(action1.Result, "first duplicate /end");
            var json2 = AssertOk(action2.Result, "second duplicate /end");

            Assert(aiHandler.ExtractCalls == 1, $"expected 1 extract call, got {aiHandler.ExtractCalls}");
            Assert(aiHandler.EvaluateCalls == 1, $"expected 1 evaluate call, got {aiHandler.EvaluateCalls}");

            Assert(json1.RootElement.GetProperty("CoverageScore").GetDecimal() ==
                   json2.RootElement.GetProperty("CoverageScore").GetDecimal(),
                "duplicate /end should return the same coverage score");

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evaluationCount = await db.EvaluationResults.CountAsync(e => e.SessionId == session.Id);
            Assert(evaluationCount == 1, $"expected exactly 1 evaluation row, got {evaluationCount}");

            var sessionState = await db.SimulationSessions
                .AsNoTracking()
                .Where(s => s.Id == session.Id)
                .Select(s => new
                {
                    s.FinalizationStatus,
                    s.FinalizationLeaseId,
                    s.FinalizationExpiresAt
                })
                .SingleAsync();

            Assert(sessionState.FinalizationStatus == SessionFinalizationStatus.Completed,
                $"expected finalization status Completed, got {sessionState.FinalizationStatus}");
            Assert(sessionState.FinalizationLeaseId is null, "completed session should clear finalization lease id");
            Assert(sessionState.FinalizationExpiresAt is null, "completed session should clear finalization expiry");
        }

        private async Task TestReclaimAfterLeaseExpiryAsync()
        {
            var scenario = await GetScenarioAsync();
            var persona = await GetPersonaAsync(scenario.Id);
            var user = await CreateTestUserAsync("reclaim-expired-lease");
            var session = await CreateSessionWithMessagesAsync(user.Id, scenario.Id, persona.Id);

            await using (var setupScope = provider.CreateAsyncScope())
            {
                var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var expiredAt = DateTime.UtcNow.AddMinutes(-2);
                await setupDb.SimulationSessions
                    .Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.IsActive, false)
                        .SetProperty(s => s.EndedAt, expiredAt)
                        .SetProperty(s => s.FinalizationStatus, SessionFinalizationStatus.InProgress)
                        .SetProperty(s => s.FinalizationLeaseId, Guid.NewGuid())
                        .SetProperty(s => s.FinalizationStartedAt, expiredAt.AddMinutes(-1))
                        .SetProperty(s => s.FinalizationExpiresAt, expiredAt));
            }

            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);
            var result = await RunEndSessionAsync(session.Id, user.Id, aiClient);
            using var json = AssertOk(result, "reclaim after lease expiry");

            Assert(aiHandler.ExtractCalls == 1, $"expected 1 extract call after reclaim, got {aiHandler.ExtractCalls}");
            Assert(aiHandler.EvaluateCalls == 1, $"expected 1 evaluate call after reclaim, got {aiHandler.EvaluateCalls}");
            Assert(json.RootElement.GetProperty("CoverageScore").GetDecimal() > 0,
                "reclaimed finalization should return a coverage score");

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessionState = await db.SimulationSessions
                .AsNoTracking()
                .Where(s => s.Id == session.Id)
                .Select(s => new
                {
                    s.FinalizationStatus,
                    s.FinalizationLeaseId,
                    s.FinalizationExpiresAt
                })
                .SingleAsync();

            Assert(sessionState.FinalizationStatus == SessionFinalizationStatus.Completed,
                $"expected reclaimed session to finish as Completed, got {sessionState.FinalizationStatus}");
            Assert(sessionState.FinalizationLeaseId is null,
                "reclaimed session should clear finalization lease id after success");
            Assert(sessionState.FinalizationExpiresAt is null,
                "reclaimed session should clear finalization expiry after success");
        }

        private async Task TestStudentHappyPathAsync()
        {
            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);
            var email = $"student-happy-path-{Guid.NewGuid():N}@example.com";
            const string password = "IntegrationPass!123";

            using var registerJson = AssertOk(
                await RunRegisterAsync("Integration Student", email, password),
                "register student");
            var userId = registerJson.RootElement.GetProperty("Id").GetGuid();
            _createdUserIds.Add(userId);

            Assert(string.Equals(
                    registerJson.RootElement.GetProperty("Email").GetString(),
                    email,
                    StringComparison.OrdinalIgnoreCase),
                "register should return the same email that was submitted");

            using var loginJson = AssertOk(
                await RunLoginAsync(email, password),
                "login student");
            var token = loginJson.RootElement.GetProperty("token").GetString();
            Assert(!string.IsNullOrWhiteSpace(token), "login should return a JWT token");

            var principal = BuildPrincipalFromJwt(token!);
            var principalUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            Assert(principalUserId == userId.ToString(), "JWT should contain the registered user id");

            using var scenariosJson = AssertOk(await RunGetScenariosAsync(principal), "list scenarios");
            var scenarioElement = scenariosJson.RootElement
                .EnumerateArray()
                .FirstOrDefault(s => string.Equals(
                    s.GetProperty("Title").GetString(),
                    ScenarioTitle,
                    StringComparison.Ordinal));
            Assert(scenarioElement.ValueKind != JsonValueKind.Undefined,
                $"scenario list should include '{ScenarioTitle}'");
            var scenarioId = scenarioElement.GetProperty("Id").GetGuid();

            using var scenarioDetailJson = AssertOk(
                await RunGetScenarioByIdAsync(scenarioId, principal),
                "get scenario detail");
            var hiddenRequirements = scenarioDetailJson.RootElement.GetProperty("HiddenRequirements");
            Assert(hiddenRequirements.ValueKind == JsonValueKind.Null,
                "student scenario detail should not expose hidden requirements");

            var personaElement = scenarioDetailJson.RootElement
                .GetProperty("Personas")
                .EnumerateArray()
                .FirstOrDefault(p => string.Equals(
                    p.GetProperty("Name").GetString(),
                    PersonaName,
                    StringComparison.Ordinal));
            Assert(personaElement.ValueKind != JsonValueKind.Undefined,
                $"scenario detail should include persona '{PersonaName}'");
            var personaId = personaElement.GetProperty("Id").GetGuid();

            using var createJson = AssertOk(
                await RunCreateSessionAsync(scenarioId, personaId, principal, aiClient),
                "create session");
            var sessionId = createJson.RootElement.GetProperty("Id").GetGuid();
            _createdSessionIds.Add(sessionId);

            const string studentMessage =
                "Can students register for courses online, and how are prerequisite rules handled?";
            using var sendJson = AssertOk(
                await RunSendMessageAsync(sessionId, studentMessage, principal, aiClient),
                "send message");

            Assert(aiHandler.ChatCalls == 1, $"expected 1 chat call, got {aiHandler.ChatCalls}");
            Assert(string.Equals(
                    sendJson.RootElement.GetProperty("questionType").GetString(),
                    QuestionType.Probing.ToString(),
                    StringComparison.Ordinal),
                "chat flow should persist the detected probing question type");
            Assert(sendJson.RootElement.GetProperty("reply").GetString()?
                .Contains("register online", StringComparison.OrdinalIgnoreCase) == true,
                "chat reply should mention online registration");

            var stateUpdate = sendJson.RootElement.GetProperty("stateUpdate");
            Assert(stateUpdate.GetProperty("TurnCount").GetInt32() >= 1,
                "chat state update should advance turn count");
            Assert(stateUpdate.GetProperty("NewlyRevealed").GetArrayLength() >= 1,
                "chat state update should reveal at least one requirement");

            await using (var afterSendScope = provider.CreateAsyncScope())
            {
                var db = afterSendScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var messages = await db.Messages
                    .AsNoTracking()
                    .Where(m => m.SessionId == sessionId)
                    .OrderBy(m => m.Timestamp)
                    .ToListAsync();
                Assert(messages.Count == 2, $"expected 2 messages after one chat turn, got {messages.Count}");
                Assert(messages[0].Sender == SenderType.Student, "first message should belong to the student");
                Assert(messages[0].DetectedQuestionType == QuestionType.Probing,
                    $"expected student message question type Probing, got {messages[0].DetectedQuestionType}");
                Assert(messages[1].Sender == SenderType.Stakeholder,
                    "second message should be the stakeholder reply");

                var sessionStateJson = await db.SimulationSessions
                    .AsNoTracking()
                    .Where(s => s.Id == sessionId)
                    .Select(s => s.PersonaState)
                    .SingleAsync();
                Assert(!string.IsNullOrWhiteSpace(sessionStateJson),
                    "session should persist updated persona state after chat");
            }

            using var endJson = AssertOk(
                await RunEndSessionAsync(sessionId, principal, aiClient),
                "end session after happy path");

            Assert(aiHandler.ExtractCalls == 1, $"expected 1 extract call, got {aiHandler.ExtractCalls}");
            Assert(aiHandler.EvaluateCalls == 1, $"expected 1 evaluate call, got {aiHandler.EvaluateCalls}");
            Assert(endJson.RootElement.GetProperty("CoverageScore").GetDecimal() > 0,
                "happy path evaluation should return positive coverage");
            Assert(string.Equals(
                    endJson.RootElement.GetProperty("ScoringPolicy").GetProperty("Preset").GetString(),
                    "integration-test",
                    StringComparison.Ordinal),
                "happy path evaluation should include integration scoring policy metadata");

            await using var finalScope = provider.CreateAsyncScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var finalSession = await finalDb.SimulationSessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new
                {
                    s.IsActive,
                    s.FinalizationStatus,
                    s.EndedAt
                })
                .SingleAsync();
            Assert(!finalSession.IsActive, "ended session should no longer be active");
            Assert(finalSession.FinalizationStatus == SessionFinalizationStatus.Completed,
                $"happy path session should finish as Completed, got {finalSession.FinalizationStatus}");
            Assert(finalSession.EndedAt is not null, "ended session should persist an ended timestamp");
        }

        private async Task TestHttpAuthHeaderMiddlewareAsync()
        {
            var (fakeAiService, backendServer) = await GetSharedHttpTestEnvironmentAsync();
            var chatCallsBefore = fakeAiService.ChatCalls;
            var extractCallsBefore = fakeAiService.ExtractCalls;
            var evaluateCallsBefore = fakeAiService.EvaluateCalls;

            await AssertHttpStatusAsync(
                backendServer.Client,
                HttpMethod.Get,
                "/api/Scenarios",
                HttpStatusCode.Unauthorized,
                "anonymous GET /api/Scenarios should return 401");

            await AssertHttpStatusAsync(
                backendServer.Client,
                HttpMethod.Get,
                "/api/Scenarios",
                HttpStatusCode.Unauthorized,
                "invalid bearer token should return 401",
                bearerToken: "definitely-not-a-real-jwt");

            var ownerEmail = $"http-owner-{Guid.NewGuid():N}@example.com";
            const string ownerPassword = "IntegrationPass!123";
            using var ownerRegisterJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                "/api/Auth/register",
                HttpStatusCode.OK,
                "HTTP register owner",
                payload: new
                {
                    name = "HTTP Owner Student",
                    email = ownerEmail,
                    password = ownerPassword
                });
            _createdUserIds.Add(GetRequiredProperty(ownerRegisterJson.RootElement, "Id").GetGuid());

            var ownerToken = await LoginOverHttpAsync(backendServer.Client, ownerEmail, ownerPassword);

            using var scenariosJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                "/api/Scenarios",
                HttpStatusCode.OK,
                "HTTP GET /api/Scenarios with valid bearer token",
                bearerToken: ownerToken);

            var scenarioElement = scenariosJson.RootElement
                .EnumerateArray()
                .FirstOrDefault(s => string.Equals(
                    GetRequiredProperty(s, "Title").GetString(),
                    ScenarioTitle,
                    StringComparison.Ordinal));
            Assert(scenarioElement.ValueKind != JsonValueKind.Undefined,
                $"HTTP scenario list should include '{ScenarioTitle}'");
            var scenarioId = GetRequiredProperty(scenarioElement, "Id").GetGuid();

            using var scenarioDetailJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Scenarios/{scenarioId}",
                HttpStatusCode.OK,
                "HTTP GET /api/Scenarios/{id} with valid bearer token",
                bearerToken: ownerToken);
            Assert(GetRequiredProperty(scenarioDetailJson.RootElement, "HiddenRequirements").ValueKind == JsonValueKind.Null,
                "student HTTP scenario detail should not expose hidden requirements");

            var personaElement = GetRequiredProperty(scenarioDetailJson.RootElement, "Personas")
                .EnumerateArray()
                .FirstOrDefault(p => string.Equals(
                    GetRequiredProperty(p, "Name").GetString(),
                    PersonaName,
                    StringComparison.Ordinal));
            Assert(personaElement.ValueKind != JsonValueKind.Undefined,
                $"HTTP scenario detail should include persona '{PersonaName}'");
            var personaId = GetRequiredProperty(personaElement, "Id").GetGuid();

            using var createSessionJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                "/api/Sessions",
                HttpStatusCode.OK,
                "HTTP create session with valid bearer token",
                bearerToken: ownerToken,
                payload: new
                {
                    scenarioId,
                    personaId
                });
            var sessionId = GetRequiredProperty(createSessionJson.RootElement, "Id").GetGuid();
            _createdSessionIds.Add(sessionId);

            using var sendJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.OK,
                "HTTP send message with valid bearer token",
                bearerToken: ownerToken,
                payload: new
                {
                    content = "Can students register online, and how are prerequisite checks enforced?"
                });
            Assert(string.Equals(
                    GetRequiredProperty(sendJson.RootElement, "questionType").GetString(),
                    QuestionType.Probing.ToString(),
                    StringComparison.Ordinal),
                "HTTP send message should preserve detected question type");

            var otherEmail = $"http-other-{Guid.NewGuid():N}@example.com";
            using var otherRegisterJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                "/api/Auth/register",
                HttpStatusCode.OK,
                "HTTP register second student",
                payload: new
                {
                    name = "HTTP Other Student",
                    email = otherEmail,
                    password = ownerPassword
                });
            _createdUserIds.Add(GetRequiredProperty(otherRegisterJson.RootElement, "Id").GetGuid());

            var otherToken = await LoginOverHttpAsync(backendServer.Client, otherEmail, ownerPassword);
            await AssertHttpStatusAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.Forbidden,
                "other student's valid JWT should return 403 on someone else's session",
                bearerToken: otherToken,
                payload: new
                {
                    content = "I should not have access to this session."
                });

            using var endJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/end",
                HttpStatusCode.OK,
                "HTTP end session with valid bearer token",
                bearerToken: ownerToken);
            Assert(GetRequiredProperty(endJson.RootElement, "CoverageScore").GetDecimal() > 0,
                "HTTP end session should return positive coverage");

            await AssertHttpStatusWithExactBodyAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.BadRequest,
                "Session already ended",
                "HTTP send after session end should return bad request",
                bearerToken: ownerToken,
                payload: new
                {
                    content = "Can I keep chatting after ending the session?"
                });

            var chatCalls = fakeAiService.ChatCalls - chatCallsBefore;
            var extractCalls = fakeAiService.ExtractCalls - extractCallsBefore;
            var evaluateCalls = fakeAiService.EvaluateCalls - evaluateCallsBefore;

            Assert(chatCalls >= 1, $"HTTP flow should call chat at least once, got {chatCalls}");
            Assert(extractCalls == 1, $"HTTP flow should call extract once, got {extractCalls}");
            Assert(evaluateCalls == 1, $"HTTP flow should call evaluate once, got {evaluateCalls}");
        }

        private async Task TestHttpLecturerAdminReviewAccessAsync()
        {
            var (fakeAiService, backendServer) = await GetSharedHttpTestEnvironmentAsync();
            var chatCallsBefore = fakeAiService.ChatCalls;
            var extractCallsBefore = fakeAiService.ExtractCalls;
            var evaluateCallsBefore = fakeAiService.EvaluateCalls;

            var ownerEmail = $"http-review-owner-{Guid.NewGuid():N}@example.com";
            const string password = "IntegrationPass!123";
            using var ownerRegisterJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                "/api/Auth/register",
                HttpStatusCode.OK,
                "HTTP register review owner",
                payload: new
                {
                    name = "HTTP Review Owner",
                    email = ownerEmail,
                    password
                });
            _createdUserIds.Add(GetRequiredProperty(ownerRegisterJson.RootElement, "Id").GetGuid());

            var ownerToken = await LoginOverHttpAsync(backendServer.Client, ownerEmail, password);

            using var scenariosJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                "/api/Scenarios",
                HttpStatusCode.OK,
                "HTTP scenario list for review owner",
                bearerToken: ownerToken);

            var scenarioElement = scenariosJson.RootElement
                .EnumerateArray()
                .FirstOrDefault(s => string.Equals(
                    GetRequiredProperty(s, "Title").GetString(),
                    ScenarioTitle,
                    StringComparison.Ordinal));
            Assert(scenarioElement.ValueKind != JsonValueKind.Undefined,
                $"review access scenario list should include '{ScenarioTitle}'");
            var scenarioId = GetRequiredProperty(scenarioElement, "Id").GetGuid();

            using var scenarioDetailJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Scenarios/{scenarioId}",
                HttpStatusCode.OK,
                "HTTP scenario detail for review owner",
                bearerToken: ownerToken);
            var personaElement = GetRequiredProperty(scenarioDetailJson.RootElement, "Personas")
                .EnumerateArray()
                .FirstOrDefault(p => string.Equals(
                    GetRequiredProperty(p, "Name").GetString(),
                    PersonaName,
                    StringComparison.Ordinal));
            Assert(personaElement.ValueKind != JsonValueKind.Undefined,
                $"review access scenario detail should include persona '{PersonaName}'");
            var personaId = GetRequiredProperty(personaElement, "Id").GetGuid();

            using var createSessionJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                "/api/Sessions",
                HttpStatusCode.OK,
                "HTTP create session for review access",
                bearerToken: ownerToken,
                payload: new
                {
                    scenarioId,
                    personaId
                });
            var sessionId = GetRequiredProperty(createSessionJson.RootElement, "Id").GetGuid();
            _createdSessionIds.Add(sessionId);

            await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.OK,
                "HTTP send message before review access",
                bearerToken: ownerToken,
                payload: new
                {
                    content = "Can students register online and what prerequisite rules should I know?"
                });

            using var ownerEndJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/end",
                HttpStatusCode.OK,
                "HTTP owner end session before review access",
                bearerToken: ownerToken);
            var ownerCoverage = GetRequiredProperty(ownerEndJson.RootElement, "CoverageScore").GetDecimal();
            Assert(ownerCoverage > 0, "review setup should produce positive coverage");

            var otherStudentEmail = $"http-review-other-{Guid.NewGuid():N}@example.com";
            using var otherStudentRegisterJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                "/api/Auth/register",
                HttpStatusCode.OK,
                "HTTP register other student for review access",
                payload: new
                {
                    name = "HTTP Review Other Student",
                    email = otherStudentEmail,
                    password
                });
            _createdUserIds.Add(GetRequiredProperty(otherStudentRegisterJson.RootElement, "Id").GetGuid());
            var otherStudentToken = await LoginOverHttpAsync(backendServer.Client, otherStudentEmail, password);

            await AssertHttpStatusAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.Forbidden,
                "other student should not read someone else's session history",
                bearerToken: otherStudentToken);

            var lecturerEmail = $"http-review-lecturer-{Guid.NewGuid():N}@example.com";
            await CreatePrivilegedUserAsync("http-review-lecturer", lecturerEmail, password, UserRole.Lecturer);
            var lecturerToken = await LoginOverHttpAsync(backendServer.Client, lecturerEmail, password);

            var adminEmail = $"http-review-admin-{Guid.NewGuid():N}@example.com";
            await CreatePrivilegedUserAsync("http-review-admin", adminEmail, password, UserRole.Admin);
            var adminToken = await LoginOverHttpAsync(backendServer.Client, adminEmail, password);

            using var lecturerScenarioJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Scenarios/{scenarioId}",
                HttpStatusCode.OK,
                "HTTP lecturer scenario detail review",
                bearerToken: lecturerToken);
            var lecturerHiddenRequirements = GetRequiredProperty(lecturerScenarioJson.RootElement, "HiddenRequirements");
            Assert(lecturerHiddenRequirements.ValueKind == JsonValueKind.Array &&
                   lecturerHiddenRequirements.GetArrayLength() > 0,
                "lecturer should see hidden requirements for scenario review");

            using var adminScenarioJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Scenarios/{scenarioId}",
                HttpStatusCode.OK,
                "HTTP admin scenario detail review",
                bearerToken: adminToken);
            var adminHiddenRequirements = GetRequiredProperty(adminScenarioJson.RootElement, "HiddenRequirements");
            Assert(adminHiddenRequirements.ValueKind == JsonValueKind.Array &&
                   adminHiddenRequirements.GetArrayLength() == lecturerHiddenRequirements.GetArrayLength(),
                "admin should see the same hidden requirement set for scenario review");

            using var lecturerMessagesJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.OK,
                "HTTP lecturer session history review",
                bearerToken: lecturerToken);
            Assert(lecturerMessagesJson.RootElement.ValueKind == JsonValueKind.Array &&
                   lecturerMessagesJson.RootElement.GetArrayLength() == 2,
                "lecturer should read the full 2-message transcript");

            using var adminMessagesJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Get,
                $"/api/Sessions/{sessionId}/messages",
                HttpStatusCode.OK,
                "HTTP admin session history review",
                bearerToken: adminToken);
            Assert(adminMessagesJson.RootElement.ValueKind == JsonValueKind.Array &&
                   adminMessagesJson.RootElement.GetArrayLength() == 2,
                "admin should read the full 2-message transcript");

            var lecturerFirstContent = GetRequiredProperty(lecturerMessagesJson.RootElement[0], "content").GetString();
            var adminSecondContent = GetRequiredProperty(adminMessagesJson.RootElement[1], "content").GetString();
            Assert(lecturerFirstContent?.Contains("register online", StringComparison.OrdinalIgnoreCase) == true,
                "lecturer transcript should include the student's original question");
            Assert(adminSecondContent?.Contains("prerequisite checking", StringComparison.OrdinalIgnoreCase) == true,
                "admin transcript should include the stakeholder reply");

            using var lecturerEndJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/end",
                HttpStatusCode.OK,
                "HTTP lecturer re-open evaluation for session review",
                bearerToken: lecturerToken);
            using var adminEndJson = await AssertHttpJsonAsync(
                backendServer.Client,
                HttpMethod.Post,
                $"/api/Sessions/{sessionId}/end",
                HttpStatusCode.OK,
                "HTTP admin re-open evaluation for session review",
                bearerToken: adminToken);

            Assert(GetRequiredProperty(lecturerEndJson.RootElement, "CoverageScore").GetDecimal() == ownerCoverage,
                "lecturer review should read the same stored evaluation coverage");
            Assert(GetRequiredProperty(adminEndJson.RootElement, "CoverageScore").GetDecimal() == ownerCoverage,
                "admin review should read the same stored evaluation coverage");

            var chatCalls = fakeAiService.ChatCalls - chatCallsBefore;
            var extractCalls = fakeAiService.ExtractCalls - extractCallsBefore;
            var evaluateCalls = fakeAiService.EvaluateCalls - evaluateCallsBefore;

            Assert(chatCalls == 1, $"review access should not add extra chat calls, got {chatCalls}");
            Assert(extractCalls == 1, $"review access should not re-run extract, got {extractCalls}");
            Assert(evaluateCalls == 1, $"review access should not re-run evaluate, got {evaluateCalls}");
        }

        private async Task TestLoginWrongPasswordAsync()
        {
            var email = $"login-fail-{Guid.NewGuid():N}@example.com";
            const string password = "IntegrationPass!123";

            using var registerJson = AssertOk(
                await RunRegisterAsync("Login Failure Student", email, password),
                "register student for wrong-password login test");
            _createdUserIds.Add(registerJson.RootElement.GetProperty("Id").GetGuid());

            using var unauthorizedJson = AssertUnauthorizedWithJson(
                await RunLoginAsync(email, "WrongPassword!456"),
                "login with wrong password");
            Assert(string.Equals(
                    unauthorizedJson.RootElement.GetProperty("error").GetString(),
                    "Invalid email or password",
                    StringComparison.Ordinal),
                "login with wrong password should return the expected error payload");
        }

        private async Task TestRegisterDuplicateEmailAsync()
        {
            var email = $"register-conflict-{Guid.NewGuid():N}@example.com";

            using var registerJson = AssertOk(
                await RunRegisterAsync("Duplicate Student", email, "IntegrationPass!123"),
                "initial register for duplicate-email test");
            var userId = registerJson.RootElement.GetProperty("Id").GetGuid();
            _createdUserIds.Add(userId);

            using var conflictJson = AssertConflictWithJson(
                await RunRegisterAsync("Duplicate Student 2", email, "AnotherPass!123"),
                "register duplicate email");
            Assert(string.Equals(
                    conflictJson.RootElement.GetProperty("error").GetString(),
                    "Email already exists",
                    StringComparison.Ordinal),
                "duplicate register should return the expected conflict error");

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailCount = await db.Users.CountAsync(u => u.Email == email);
            Assert(emailCount == 1, $"duplicate register should leave exactly 1 user row, got {emailCount}");
        }

        private async Task TestUnauthorizedSendMessageAsync()
        {
            var scenario = await GetScenarioAsync();
            var persona = await GetPersonaAsync(scenario.Id);
            var user = await CreateTestUserAsync("unauthorized-send-owner");
            var session = await CreateSessionWithMessagesAsync(user.Id, scenario.Id, persona.Id);
            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);

            AssertUnauthorizedResult(
                await RunSendMessageAsync(session.Id, "Can I ask another question?", BuildAnonymousPrincipal(), aiClient),
                "send message without user id");
            Assert(aiHandler.ChatCalls == 0, $"unauthorized send should not call AI chat, got {aiHandler.ChatCalls}");
        }

        private async Task TestForbiddenSendMessageAsync()
        {
            var scenario = await GetScenarioAsync();
            var persona = await GetPersonaAsync(scenario.Id);
            var owner = await CreateTestUserAsync("forbidden-send-owner");
            var otherUser = await CreateTestUserAsync("forbidden-send-other");
            var session = await CreateSessionWithMessagesAsync(owner.Id, scenario.Id, persona.Id);
            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);

            AssertForbidResult(
                await RunSendMessageAsync(session.Id, "I should not have access to this session.", BuildPrincipal(otherUser.Id), aiClient),
                "send message as other student");
            Assert(aiHandler.ChatCalls == 0, $"forbidden send should not call AI chat, got {aiHandler.ChatCalls}");
        }

        private async Task TestSendMessageAfterSessionEndedAsync()
        {
            var scenario = await GetScenarioAsync();
            var persona = await GetPersonaAsync(scenario.Id);
            var user = await CreateTestUserAsync("send-after-end");
            var session = await CreateSessionWithMessagesAsync(user.Id, scenario.Id, persona.Id);
            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);
            var principal = BuildPrincipal(user.Id);

            using var endJson = AssertOk(
                await RunEndSessionAsync(session.Id, principal, aiClient),
                "end session before send-after-closed check");
            Assert(endJson.RootElement.GetProperty("CoverageScore").GetDecimal() > 0,
                "setup end-session call should produce a coverage score");

            AssertBadRequestWithMessage(
                await RunSendMessageAsync(session.Id, "Can I continue chatting after closing the session?", principal, aiClient),
                "send message after session ended",
                "Session already ended");

            Assert(aiHandler.ChatCalls == 0, $"send-after-end should not call AI chat, got {aiHandler.ChatCalls}");
            Assert(aiHandler.ExtractCalls == 1, $"setup end-session should call extract once, got {aiHandler.ExtractCalls}");
            Assert(aiHandler.EvaluateCalls == 1, $"setup end-session should call evaluate once, got {aiHandler.EvaluateCalls}");
        }

        private async Task TestEndSessionAfterClosedAsync()
        {
            var scenario = await GetScenarioAsync();
            var persona = await GetPersonaAsync(scenario.Id);
            var user = await CreateTestUserAsync("end-after-end");
            var session = await CreateSessionWithMessagesAsync(user.Id, scenario.Id, persona.Id);
            var aiHandler = new FakeAiMessageHandler();
            var aiClient = CreateAiClient(aiHandler);
            var principal = BuildPrincipal(user.Id);

            using var firstJson = AssertOk(
                await RunEndSessionAsync(session.Id, principal, aiClient),
                "first end session after closed test");
            using var secondJson = AssertOk(
                await RunEndSessionAsync(session.Id, principal, aiClient),
                "second end session after closed test");

            Assert(aiHandler.ExtractCalls == 1, $"sequential duplicate end should call extract once, got {aiHandler.ExtractCalls}");
            Assert(aiHandler.EvaluateCalls == 1, $"sequential duplicate end should call evaluate once, got {aiHandler.EvaluateCalls}");
            Assert(firstJson.RootElement.GetProperty("CoverageScore").GetDecimal() ==
                   secondJson.RootElement.GetProperty("CoverageScore").GetDecimal(),
                "sequential duplicate end should return the same coverage score");

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evaluationCount = await db.EvaluationResults.CountAsync(e => e.SessionId == session.Id);
            Assert(evaluationCount == 1, $"sequential duplicate end should keep exactly 1 evaluation row, got {evaluationCount}");
        }

        private async Task<(FakeAiService Service, BackendApiServer Server)> GetSharedHttpTestEnvironmentAsync()
        {
            if (_sharedHttpAiService is not null && _sharedHttpBackendServer is not null)
                return (_sharedHttpAiService, _sharedHttpBackendServer);

            _sharedHttpAiService = new FakeAiService();
            _sharedHttpAiServer = await FakeAiHttpServer.StartAsync(_sharedHttpAiService);

            try
            {
                _sharedHttpBackendServer = await BackendApiServer.StartAsync(
                    Directory.GetCurrentDirectory(),
                    _sharedHttpAiServer.BaseUrl);
                return (_sharedHttpAiService, _sharedHttpBackendServer);
            }
            catch
            {
                await DisposeSharedHttpTestEnvironmentAsync();
                throw;
            }
        }

        private async Task<Scenario> GetScenarioAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Scenarios.AsNoTracking().SingleAsync(s => s.Title == ScenarioTitle);
        }

        private async Task<Persona> GetPersonaAsync(Guid scenarioId)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Personas.AsNoTracking().SingleAsync(p => p.ScenarioId == scenarioId && p.Name == PersonaName);
        }

        private async Task<User> CreateTestUserAsync(string prefix)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = $"Integration {prefix}",
                Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
                PasswordHash = "integration-test",
                Role = UserRole.Student
            };

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();

            _createdUserIds.Add(user.Id);
            return user;
        }

        private async Task<User> CreatePrivilegedUserAsync(
            string prefix,
            string email,
            string password,
            UserRole role)
        {
            await using var scope = provider.CreateAsyncScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            var user = await auth.Register(
                $"Integration {role} {prefix}",
                email,
                password,
                role);

            _createdUserIds.Add(user.Id);
            return user;
        }

        private async Task<SimulationSession> CreateSessionWithMessagesAsync(Guid userId, Guid scenarioId, Guid personaId)
        {
            var session = new SimulationSession
            {
                Id = Guid.NewGuid(),
                StudentId = userId,
                ScenarioId = scenarioId,
                PersonaId = personaId,
                PersonaState = """
                    {"mood":"neutral_busy","patience":0.65,"revealed_requirements":[],"turn_count":3}
                    """
            };

            var startedAt = DateTime.UtcNow.AddMinutes(-5);
            session.Messages =
            [
                new Message
                {
                    Id = Guid.NewGuid(),
                    Sender = SenderType.Student,
                    Content = "What is the main purpose of this registration system?",
                    Timestamp = startedAt.AddSeconds(10)
                },
                new Message
                {
                    Id = Guid.NewGuid(),
                    Sender = SenderType.Stakeholder,
                    Content = "Students need to register for courses online.",
                    Timestamp = startedAt.AddSeconds(20)
                },
                new Message
                {
                    Id = Guid.NewGuid(),
                    Sender = SenderType.Student,
                    Content = "Are there prerequisite rules before registering?",
                    Timestamp = startedAt.AddSeconds(30)
                },
                new Message
                {
                    Id = Guid.NewGuid(),
                    Sender = SenderType.Stakeholder,
                    Content = "There are prerequisite rules before registration.",
                    Timestamp = startedAt.AddSeconds(40)
                }
            ];

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SimulationSessions.Add(session);
            await db.SaveChangesAsync();

            _createdSessionIds.Add(session.Id);
            return session;
        }

        private async Task<IActionResult> RunRegisterAsync(string name, string email, string password)
        {
            await using var scope = provider.CreateAsyncScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            var controller = new AuthController(auth);
            return await controller.Register(new AuthController.RegisterDto(name, email, password));
        }

        private async Task<IActionResult> RunLoginAsync(string email, string password)
        {
            await using var scope = provider.CreateAsyncScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            var controller = new AuthController(auth);
            return await controller.Login(new AuthController.LoginDto(email, password));
        }

        private async Task<IActionResult> RunGetScenariosAsync(ClaimsPrincipal principal)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var controller = new ScenariosController(db)
            {
                ControllerContext = CreateAuthorizedControllerContext(principal)
            };

            return await controller.GetAll();
        }

        private async Task<IActionResult> RunGetScenarioByIdAsync(Guid scenarioId, ClaimsPrincipal principal)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var controller = new ScenariosController(db)
            {
                ControllerContext = CreateAuthorizedControllerContext(principal)
            };

            return await controller.GetById(scenarioId);
        }

        private async Task<IActionResult> RunCreateSessionAsync(
            Guid scenarioId,
            Guid personaId,
            ClaimsPrincipal principal,
            AiServiceClient aiClient)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var controller = new SessionsController(db, aiClient)
            {
                ControllerContext = CreateAuthorizedControllerContext(principal)
            };

            return await controller.Create(new SessionsController.CreateSessionDto(scenarioId, personaId));
        }

        private async Task<IActionResult> RunSendMessageAsync(
            Guid sessionId,
            string content,
            ClaimsPrincipal principal,
            AiServiceClient aiClient)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var controller = new SessionsController(db, aiClient)
            {
                ControllerContext = CreateAuthorizedControllerContext(principal)
            };

            return await controller.SendMessage(sessionId, new SessionsController.SendMessageDto(content));
        }

        private static AiServiceClient CreateAiClient(FakeAiMessageHandler handler)
        {
            var httpClient = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://fake-ai.local")
            };
            return new AiServiceClient(httpClient);
        }

        private async Task<IActionResult> RunEndSessionAsync(Guid sessionId, Guid userId, AiServiceClient aiClient)
            => await RunEndSessionAsync(sessionId, BuildPrincipal(userId), aiClient);

        private async Task<IActionResult> RunEndSessionAsync(
            Guid sessionId,
            ClaimsPrincipal principal,
            AiServiceClient aiClient)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var controller = new SessionsController(db, aiClient)
            {
                ControllerContext = CreateAuthorizedControllerContext(principal)
            };

            return await controller.EndSession(sessionId);
        }

        private async Task CleanupAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (_createdSessionIds.Count > 0)
            {
                await db.RequirementMatches
                    .Where(m => _createdSessionIds.Contains(m.Evaluation.SessionId))
                    .ExecuteDeleteAsync();
                await db.EvaluationResults
                    .Where(e => _createdSessionIds.Contains(e.SessionId))
                    .ExecuteDeleteAsync();
                await db.ExtractedRequirements
                    .Where(r => _createdSessionIds.Contains(r.SessionId))
                    .ExecuteDeleteAsync();
                await db.Messages
                    .Where(m => _createdSessionIds.Contains(m.SessionId))
                    .ExecuteDeleteAsync();
                await db.SimulationSessions
                    .Where(s => _createdSessionIds.Contains(s.Id))
                    .ExecuteDeleteAsync();
            }

            if (_createdUserIds.Count > 0)
            {
                await db.Users
                    .Where(u => _createdUserIds.Contains(u.Id))
                    .ExecuteDeleteAsync();
            }
        }

        private async Task DisposeSharedHttpTestEnvironmentAsync()
        {
            if (_sharedHttpBackendServer is not null)
            {
                await _sharedHttpBackendServer.DisposeAsync();
                _sharedHttpBackendServer = null;
            }

            if (_sharedHttpAiServer is not null)
            {
                await _sharedHttpAiServer.DisposeAsync();
                _sharedHttpAiServer = null;
            }

            _sharedHttpAiService = null;
        }

        private sealed record ScenarioExecutionResult(
            string Name,
            bool Passed,
            TimeSpan Duration,
            string? FailureMessage = null);

        private static ClaimsPrincipal BuildPrincipalFromJwt(string token)
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var userIdValue = FindClaimValue(jwt.Claims, ClaimTypes.NameIdentifier, "nameidentifier", "sub");
            var role = FindClaimValue(jwt.Claims, ClaimTypes.Role, "role") ?? UserRole.Student.ToString();
            var email = FindClaimValue(jwt.Claims, ClaimTypes.Email, "email");

            if (!Guid.TryParse(userIdValue, out var userId))
                throw new InvalidOperationException("JWT token did not contain a valid user id claim.");

            return BuildPrincipal(userId, role, email);
        }

        private static string? FindClaimValue(IEnumerable<Claim> claims, params string[] expectedTypes)
        {
            foreach (var claim in claims)
            {
                foreach (var expectedType in expectedTypes)
                {
                    if (string.Equals(claim.Type, expectedType, StringComparison.OrdinalIgnoreCase) ||
                        claim.Type.EndsWith(expectedType, StringComparison.OrdinalIgnoreCase))
                    {
                        return claim.Value;
                    }
                }
            }

            return null;
        }

        private static ClaimsPrincipal BuildPrincipal(Guid userId) =>
            BuildPrincipal(userId, UserRole.Student.ToString());

        private static ClaimsPrincipal BuildPrincipal(Guid userId, string role, string? email = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            };

            if (!string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim(ClaimTypes.Email, email));

            var identity = new ClaimsIdentity(claims, "IntegrationHarness");

            return new ClaimsPrincipal(identity);
        }

        private static ClaimsPrincipal BuildAnonymousPrincipal() =>
            new(new ClaimsIdentity());

        private static async Task<string> LoginOverHttpAsync(HttpClient client, string email, string password)
        {
            using var loginJson = await AssertHttpJsonAsync(
                client,
                HttpMethod.Post,
                "/api/Auth/login",
                HttpStatusCode.OK,
                "HTTP login",
                payload: new
                {
                    email,
                    password
                });

            var token = loginJson.RootElement.GetProperty("token").GetString();
            Assert(!string.IsNullOrWhiteSpace(token), "HTTP login should return a non-empty token");
            return token!;
        }

        private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Expected JSON object when reading property '{propertyName}'.");

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            }

            throw new KeyNotFoundException($"JSON property '{propertyName}' was not found.");
        }

        private static ControllerContext CreateAuthorizedControllerContext(ClaimsPrincipal principal) =>
            new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal
                }
            };

        private static JsonDocument AssertOk(IActionResult result, string context)
        {
            if (result is not OkObjectResult ok)
                throw new InvalidOperationException($"{context} expected OkObjectResult but got {result.GetType().Name}");

            return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        }

        private static JsonDocument AssertConflictWithJson(IActionResult result, string context)
        {
            if (result is not ConflictObjectResult conflict)
                throw new InvalidOperationException($"{context} expected ConflictObjectResult but got {result.GetType().Name}");

            return JsonDocument.Parse(JsonSerializer.Serialize(conflict.Value));
        }

        private static JsonDocument AssertUnauthorizedWithJson(IActionResult result, string context)
        {
            if (result is not UnauthorizedObjectResult unauthorized)
                throw new InvalidOperationException($"{context} expected UnauthorizedObjectResult but got {result.GetType().Name}");

            return JsonDocument.Parse(JsonSerializer.Serialize(unauthorized.Value));
        }

        private static async Task<JsonDocument> AssertHttpJsonAsync(
            HttpClient client,
            HttpMethod method,
            string path,
            HttpStatusCode expectedStatus,
            string context,
            string? bearerToken = null,
            object? payload = null)
        {
            using var request = CreateHttpRequest(method, path, bearerToken, payload);
            using var response = await client.SendAsync(request);

            if (response.StatusCode != expectedStatus)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"{context} expected HTTP {(int)expectedStatus} but got {(int)response.StatusCode}. Body: {body}");
            }

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException($"{context} expected a JSON body but response was empty.");

            return JsonDocument.Parse(content);
        }

        private static async Task AssertHttpStatusAsync(
            HttpClient client,
            HttpMethod method,
            string path,
            HttpStatusCode expectedStatus,
            string context,
            string? bearerToken = null,
            object? payload = null)
        {
            using var request = CreateHttpRequest(method, path, bearerToken, payload);
            using var response = await client.SendAsync(request);

            if (response.StatusCode != expectedStatus)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"{context} expected HTTP {(int)expectedStatus} but got {(int)response.StatusCode}. Body: {body}");
            }
        }

        private static async Task AssertHttpStatusWithExactBodyAsync(
            HttpClient client,
            HttpMethod method,
            string path,
            HttpStatusCode expectedStatus,
            string expectedBody,
            string context,
            string? bearerToken = null,
            object? payload = null)
        {
            using var request = CreateHttpRequest(method, path, bearerToken, payload);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != expectedStatus)
            {
                throw new InvalidOperationException(
                    $"{context} expected HTTP {(int)expectedStatus} but got {(int)response.StatusCode}. Body: {body}");
            }

            if (!string.Equals(body, expectedBody, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} expected response body '{expectedBody}' but got '{body}'.");
            }
        }

        private static HttpRequestMessage CreateHttpRequest(
            HttpMethod method,
            string path,
            string? bearerToken,
            object? payload)
        {
            var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrWhiteSpace(bearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            if (payload is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");
            }

            return request;
        }

        private static void AssertUnauthorizedResult(IActionResult result, string context)
        {
            if (result is not UnauthorizedResult)
                throw new InvalidOperationException($"{context} expected UnauthorizedResult but got {result.GetType().Name}");
        }

        private static void AssertForbidResult(IActionResult result, string context)
        {
            if (result is not ForbidResult)
                throw new InvalidOperationException($"{context} expected ForbidResult but got {result.GetType().Name}");
        }

        private static void AssertBadRequestWithMessage(IActionResult result, string context, string expectedMessage)
        {
            if (result is not BadRequestObjectResult badRequest)
                throw new InvalidOperationException($"{context} expected BadRequestObjectResult but got {result.GetType().Name}");

            if (badRequest.Value is not string message || !string.Equals(message, expectedMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} expected bad request message '{expectedMessage}' but got '{badRequest.Value}'.");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeAiService
    {
        private int _chatCalls;
        private int _extractCalls;
        private int _evaluateCalls;

        public int ChatCalls => _chatCalls;
        public int ExtractCalls => _extractCalls;
        public int EvaluateCalls => _evaluateCalls;

        public async Task<(HttpStatusCode StatusCode, string Json)> HandleAsync(
            string path,
            string? payload,
            CancellationToken cancellationToken)
        {
            return path switch
            {
                "/api/chat" => (HttpStatusCode.OK, await BuildChatResponseAsync(payload, cancellationToken)),
                "/api/extract" => (HttpStatusCode.OK, await BuildExtractResponseAsync(cancellationToken)),
                "/api/evaluate" => (HttpStatusCode.OK, await BuildEvaluateResponseAsync(payload, cancellationToken)),
                _ => (HttpStatusCode.NotFound, CreateJsonPayload(new { error = "Not found" }))
            };
        }

        private async Task<string> BuildChatResponseAsync(string? payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _chatCalls);
            await Task.Delay(150, cancellationToken);

            using var document = JsonDocument.Parse(payload ?? "{}");
            var studentMessage = document.RootElement.GetProperty("studentMessage").GetString() ?? "";
            var historyCount = document.RootElement.GetProperty("history").GetArrayLength();

            var newlyRevealed = document.RootElement
                .GetProperty("availableRequirements")
                .EnumerateArray()
                .Select(r => r.GetString())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Take(1)
                .ToArray();

            var stakeholderReply = studentMessage.Contains("prerequisite", StringComparison.OrdinalIgnoreCase)
                ? "Yes, students can register online, and prerequisite checking is applied before enrollment is accepted."
                : "Yes, students can register online through the portal.";

            return CreateJsonPayload(new
            {
                stakeholderReply,
                detectedQuestionType = "Probing",
                stateUpdate = new
                {
                    mood = "more_open",
                    patience = 0.72m,
                    turnCount = historyCount + 1,
                    newlyRevealed
                }
            });
        }

        private async Task<string> BuildExtractResponseAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _extractCalls);
            await Task.Delay(300, cancellationToken);

            return CreateJsonPayload(new
            {
                requirements = new object[]
                {
                    new
                    {
                        text = "Students must be able to register for courses online.",
                        confidence = 0.93m
                    },
                    new
                    {
                        text = "The system must enforce prerequisite checking before allowing registration.",
                        confidence = 0.89m
                    }
                }
            });
        }

        private async Task<string> BuildEvaluateResponseAsync(string? payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _evaluateCalls);
            await Task.Delay(300, cancellationToken);

            using var document = JsonDocument.Parse(payload ?? "{}");
            var hiddenRequirements = document.RootElement.GetProperty("hiddenRequirements");

            var matches = new List<object>();
            var matchedCount = 0;

            foreach (var hiddenRequirement in hiddenRequirements.EnumerateArray())
            {
                var hiddenId = hiddenRequirement.GetProperty("id").GetString()!;
                var hiddenText = hiddenRequirement.GetProperty("text").GetString()!;

                if (hiddenText.Contains("register for courses online", StringComparison.OrdinalIgnoreCase))
                {
                    matchedCount++;
                    matches.Add(new
                    {
                        hiddenId,
                        hiddenText,
                        extractedText = "Students must be able to register for courses online.",
                        score = 0.94m,
                        matchType = "Exact",
                        reason = "The extracted requirement closely matches the hidden requirement."
                    });
                    continue;
                }

                if (hiddenText.Contains("prerequisite", StringComparison.OrdinalIgnoreCase))
                {
                    matchedCount++;
                    matches.Add(new
                    {
                        hiddenId,
                        hiddenText,
                        extractedText = "The system must enforce prerequisite checking before allowing registration.",
                        score = 0.88m,
                        matchType = "Semantic",
                        reason = "The extracted requirement preserves the core prerequisite meaning."
                    });
                    continue;
                }

                matches.Add(new
                {
                    hiddenId,
                    hiddenText,
                    extractedText = (string?)null,
                    score = 0.0m,
                    matchType = "Missed",
                    reason = "No extracted requirement was available to match this hidden requirement."
                });
            }

            var total = hiddenRequirements.GetArrayLength();
            var coverageScore = total == 0 ? 0 : Math.Round((decimal)matchedCount * 100m / total, 2);

            return CreateJsonPayload(new
            {
                coverageScore,
                matches,
                feedback = new
                {
                    strengths = new[] { "Captured key functional requirements." },
                    weaknesses = new[] { "Missed several detailed rules." },
                    suggestions = new[] { "Probe more for deadline, exception, and quality constraints." }
                },
                scoringPolicy = new
                {
                    preset = "integration-test",
                    exactThreshold = 0.85m,
                    semanticThreshold = 0.75m,
                    partialThreshold = 0.60m,
                    rubricPartialMatcher = false,
                    embeddingModel = "fake-test-model"
                }
            });
        }

        private static string CreateJsonPayload(object payload) =>
            JsonSerializer.Serialize(payload);
    }

    private sealed class FakeAiMessageHandler(FakeAiService? service = null) : HttpMessageHandler
    {
        private readonly FakeAiService _service = service ?? new FakeAiService();

        public int ChatCalls => _service.ChatCalls;
        public int ExtractCalls => _service.ExtractCalls;
        public int EvaluateCalls => _service.EvaluateCalls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var (statusCode, json) = await _service.HandleAsync(
                request.RequestUri?.AbsolutePath ?? "",
                payload,
                cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class FakeAiHttpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly FakeAiService _service;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;

        public string BaseUrl { get; }

        private FakeAiHttpServer(FakeAiService service, int port)
        {
            _service = service;
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{BaseUrl}/");
            _listener.Start();
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        public static Task<FakeAiHttpServer> StartAsync(FakeAiService service)
        {
            return Task.FromResult(new FakeAiHttpServer(service, GetAvailablePort()));
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (context is null)
                    continue;

                _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                using var reader = new StreamReader(
                    context.Request.InputStream,
                    context.Request.ContentEncoding ?? Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(cancellationToken);
                var (statusCode, json) = await _service.HandleAsync(
                    context.Request.Url?.AbsolutePath ?? "",
                    payload,
                    cancellationToken);

                var bytes = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = (int)statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
                context.Response.Close();
            }
            catch
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();

            try
            {
                await _loopTask;
            }
            catch
            {
                // Ignore listener shutdown exceptions during cleanup.
            }

            _cts.Dispose();
        }
    }

    private sealed class BackendApiServer : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();

        public HttpClient Client { get; }
        public string BaseUrl { get; }

        private BackendApiServer(Process process, string baseUrl)
        {
            _process = process;
            BaseUrl = baseUrl;
            Client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public static async Task<BackendApiServer> StartAsync(string repoRoot, string aiBaseUrl)
        {
            var port = GetAvailablePort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var (fileName, arguments) = GetBackendLaunchCommand(repoRoot);

            var startInfo = new ProcessStartInfo(fileName)
            {
                Arguments = arguments,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
            startInfo.Environment["AiService__BaseUrl"] = aiBaseUrl;
            // The harness seeds the shared baseline before any scenario runs.
            // Disabling bootstrap seeding here keeps HTTP server startup fast and stable.
            startInfo.Environment["SeedData__Enabled"] = "false";
            startInfo.Environment["Jwt__Issuer"] = "ReqSimulator";
            startInfo.Environment["Jwt__Audience"] = "ReqSimulator";
            startInfo.Environment["Jwt__ExpiresInHours"] = "24";

            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (!string.IsNullOrWhiteSpace(connectionString))
                startInfo.Environment["ConnectionStrings__DefaultConnection"] = connectionString;

            var jwtKey = Environment.GetEnvironmentVariable("Jwt__Key");
            if (!string.IsNullOrWhiteSpace(jwtKey))
                startInfo.Environment["Jwt__Key"] = jwtKey;

            var internalKey = Environment.GetEnvironmentVariable("AiService__InternalKey");
            if (!string.IsNullOrWhiteSpace(internalKey))
                startInfo.Environment["AiService__InternalKey"] = internalKey;

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start backend API process for HTTP integration test.");

            var server = new BackendApiServer(process, baseUrl);
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                    server._stdout.AppendLine(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                    server._stderr.AppendLine(args.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await server.WaitUntilReadyAsync();
                return server;
            }
            catch
            {
                await server.DisposeAsync();
                throw;
            }
        }

        private async Task WaitUntilReadyAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(120);

            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Backend API process exited early with code {_process.ExitCode}.{Environment.NewLine}{GetLogs()}");
                }

                try
                {
                    using var response = await Client.GetAsync("/api/Scenarios");
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        return;
                }
                catch
                {
                    // Keep polling until the process is ready or exits.
                }

                await Task.Delay(500);
            }

            throw new TimeoutException(
                $"Timed out waiting for backend API process to become ready at {BaseUrl}.{Environment.NewLine}{GetLogs()}");
        }

        private string GetLogs()
        {
            return $"STDOUT:{Environment.NewLine}{_stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{_stderr}";
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore shutdown races during cleanup.
                }
            }

            try
            {
                await _process.WaitForExitAsync();
            }
            catch
            {
                // Ignore cleanup races if the process is already gone.
            }

            _process.Dispose();
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static (string FileName, string Arguments) GetBackendLaunchCommand(string repoRoot)
    {
        var buildDirectory = Path.Combine(repoRoot, "backend", "ReqSimulator.API", "bin", "Release", "net9.0");
        var exePath = Path.Combine(buildDirectory, "ReqSimulator.API.exe");
        if (File.Exists(exePath))
            return (exePath, "");

        var dllPath = Path.Combine(buildDirectory, "ReqSimulator.API.dll");
        if (File.Exists(dllPath))
            return ("dotnet", $"\"{dllPath}\"");

        throw new FileNotFoundException(
            "Could not locate backend API build output for HTTP integration testing.",
            Path.Combine(buildDirectory, "ReqSimulator.API.exe"));
    }
}
