using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using ReqSimulator.API.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;

LoadDotEnvFiles(
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "backend", "ReqSimulator.API", ".env"),
    Path.Combine(AppContext.BaseDirectory, ".env"),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env")),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env")));

var builder = WebApplication.CreateBuilder(args);

// Định nghĩa tên cho chính sách CORS
var AllowVercelOrigin = "_allowVercelOrigin";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, ".keys")))
    .SetApplicationName("ReqSimulator");

static void LoadDotEnvFiles(params string[] paths)
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

static string GetRequiredConfig(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value) || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Missing required configuration value: {key}");

    return value;
}

static string NormalizePostgresConnectionString(string value)
{
    NpgsqlConnectionStringBuilder builder;
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "postgres" || uri.Scheme == "postgresql"))
    {
        var credentials = uri.UserInfo.Split(':', 2);
        var username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "";
        var password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "";

        builder = new NpgsqlConnectionStringBuilder
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
    }
    else
    {
        builder = new NpgsqlConnectionStringBuilder(value);
    }

    // Giới hạn kết nối pool để bảo vệ Neon PostgreSQL Free Tier khỏi rủi ro quá tải slots (max 10-20 connections)
    builder.MaxPoolSize = 8;
    builder.Pooling = true;

    return builder.ConnectionString;
}

static Dictionary<string, string> ParseQueryString(string query)
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

// ===== Npgsql: register enum mappings before building the DataSource =====
var connString = NormalizePostgresConnectionString(
    GetRequiredConfig(builder.Configuration, "ConnectionStrings:DefaultConnection"));
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
dataSourceBuilder.MapEnum<UserRole>("user_role");
dataSourceBuilder.MapEnum<SenderType>("sender_type");
dataSourceBuilder.MapEnum<RequirementCategory>("requirement_category");
dataSourceBuilder.MapEnum<ReqSimulator.API.Models.MatchType>("match_type");
dataSourceBuilder.MapEnum<QuestionType>("question_type");
dataSourceBuilder.MapEnum<PersonaDifficulty>("persona_difficulty");
var dataSource = dataSourceBuilder.Build();

// ===== Database =====
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(dataSource, npgsqlOptions =>
    {
        npgsqlOptions.MapEnum<UserRole>("user_role");
        npgsqlOptions.MapEnum<SenderType>("sender_type");
        npgsqlOptions.MapEnum<RequirementCategory>("requirement_category");
        npgsqlOptions.MapEnum<ReqSimulator.API.Models.MatchType>("match_type");
        npgsqlOptions.MapEnum<QuestionType>("question_type");
        npgsqlOptions.MapEnum<PersonaDifficulty>("persona_difficulty");
    }));

// ===== JWT Authentication =====
var jwtConfig = builder.Configuration.GetSection("Jwt");
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be configured and must be at least 32 bytes long.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtConfig["Issuer"],
            ValidAudience = jwtConfig["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// ===== Services (DI) =====
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ScenarioVersionPublisher>();
builder.Services.AddSingleton<ScenarioLocalizationCatalog>();
builder.Services.AddSingleton<IR2ObjectStorage, R2ObjectStorage>();
var aiServiceBaseUrl = GetRequiredConfig(builder.Configuration, "AiService:BaseUrl");
var aiServiceInternalKey = GetRequiredConfig(builder.Configuration, "AiService:InternalKey");
if (Encoding.UTF8.GetByteCount(aiServiceInternalKey) < 32)
    throw new InvalidOperationException("AiService:InternalKey must be at least 32 bytes long.");

builder.Services.AddHttpClient<AiServiceClient>(client =>
{
    client.BaseAddress = new Uri(aiServiceBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120); // LLM calls can be slow.
    client.DefaultRequestHeaders.Add("X-AI-Service-Key", aiServiceInternalKey);
});
builder.Services.AddHttpClient<IngestionWorkflowDispatcher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ===== CORS: allow the React frontend (localhost + Vercel) to call the API =====
var allowedOriginsSetting = builder.Configuration["Cors:AllowedOrigins"] ?? "";
var allowedOrigins = allowedOriginsSetting
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: AllowVercelOrigin,
        policy =>
        {
            policy.WithOrigins(allowedOrigins.ToArray())
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

// ===== Rate Limiting =====
var globalPermitLimit = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("GLOBAL_RATE_LIMIT_PERMIT_LIMIT") ??
    builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 120));
var globalWindowSeconds = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("GLOBAL_RATE_LIMIT_WINDOW_SECONDS") ??
    builder.Configuration.GetValue("RateLimiting:Global:WindowSeconds", 60));
var authPermitLimit = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("AUTH_RATE_LIMIT_ATTEMPTS") ??
    builder.Configuration.GetValue("RateLimiting:Auth:PermitLimit", 5));
var authWindowSeconds = Math.Max(
    1,
    (builder.Configuration.GetValue<int?>("AUTH_RATE_LIMIT_WINDOW_MINUTES") is int authWindowMinutes
        ? authWindowMinutes * 60
        : builder.Configuration.GetValue("RateLimiting:Auth:WindowSeconds", 900)));
var aiPermitLimit = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("AI_RATE_LIMIT_PERMIT_LIMIT") ??
    builder.Configuration.GetValue("RateLimiting:Ai:PermitLimit", 10));
var aiWindowSeconds = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("AI_RATE_LIMIT_WINDOW_SECONDS") ??
    builder.Configuration.GetValue("RateLimiting:Ai:WindowSeconds", 60));
var adminPermitLimit = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("ADMIN_INGESTION_RATE_LIMIT_PERMIT_LIMIT") ??
    builder.Configuration.GetValue("RateLimiting:AdminIngestion:PermitLimit", 20));
var adminWindowSeconds = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("ADMIN_INGESTION_RATE_LIMIT_WINDOW_SECONDS") ??
    builder.Configuration.GetValue("RateLimiting:AdminIngestion:WindowSeconds", 60));

static string GetRateLimitPartitionKey(HttpContext context)
{
    var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrWhiteSpace(subject))
        return $"user:{subject}";

    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

static RateLimitPartition<string> CreateFixedWindowPartition(
    string key,
    int permitLimit,
    int windowSeconds) =>
    RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
    {
        AutoReplenishment = true,
        PermitLimit = permitLimit,
        Window = TimeSpan.FromSeconds(windowSeconds),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        if (!context.HttpContext.Response.HasStarted)
        {
            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Please retry later." },
                cancellationToken);
        }
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => CreateFixedWindowPartition(
            GetRateLimitPartitionKey(httpContext),
            globalPermitLimit,
            globalWindowSeconds));

    options.AddPolicy("auth_strict", httpContext =>
        CreateFixedWindowPartition(
            $"auth:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            authPermitLimit,
            authWindowSeconds));

    options.AddPolicy("ai_expensive", httpContext =>
        CreateFixedWindowPartition(
            $"ai:{GetRateLimitPartitionKey(httpContext)}",
            aiPermitLimit,
            aiWindowSeconds));

    options.AddPolicy("admin_ingestion", httpContext =>
        CreateFixedWindowPartition(
            $"admin-ingestion:{GetRateLimitPartitionKey(httpContext)}",
            adminPermitLimit,
            adminWindowSeconds));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.Services.EnsureOperationalSchemaAsync(app.Logger);
await app.Services.EnsureStudentCatalogAsync(app.Logger);

if (builder.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    await app.Services.SeedScenarioV1Async(app.Logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        logger.LogError(exception, "Unhandled request failure for {Method} {Path}.", context.Request.Method, context.Request.Path);
        var origin = context.Request.Headers["Origin"].ToString();

        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Đã xảy ra lỗi hệ thống nội bộ." });
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseRouting();
app.UseCors(AllowVercelOrigin);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();
