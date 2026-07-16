using System.Text;
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
builder.Services.AddHttpClient<AiServiceClient>(client =>
{
    client.BaseAddress = new Uri(GetRequiredConfig(builder.Configuration, "AiService:BaseUrl"));
    client.Timeout = TimeSpan.FromSeconds(120); // LLM calls can be slow.
    client.DefaultRequestHeaders.Add("X-AI-Service-Key", builder.Configuration["AiService:InternalKey"] ?? "dev-internal-key");
});

// ===== CORS: allow the React frontend (localhost + Vercel) to call the API =====
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: AllowVercelOrigin,
        policy =>
        {
            policy.WithOrigins(
                      "https://kltn-chi.vercel.app",
                      "http://localhost:5173",
                      "http://127.0.0.1:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

// ===== Rate Limiting =====
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ai_chat_limit", httpContext =>
    {
        var userKey = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                      ?? httpContext.Connection.RemoteIpAddress?.ToString() 
                      ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(userKey, _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.Services.EnsureOperationalSchemaAsync(app.Logger);

if (builder.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    await app.Services.SeedScenarioV1Async(app.Logger);
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
        var origin = context.Request.Headers["Origin"].ToString();
        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://kltn-chi.vercel.app",
            "http://localhost:5173",
            "http://127.0.0.1:5173"
        };

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

app.UseRouting();
app.UseCors(AllowVercelOrigin);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();
