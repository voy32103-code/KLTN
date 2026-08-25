using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/admin-ingestion")]
[Authorize(Roles = "Admin")]
public sealed class AdminIngestionController : ControllerBase
{
    private const long MaxMediaBytes = 250L * 1024 * 1024;
    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    { "audio/mpeg", "audio/wav", "audio/x-wav", "audio/mp4", "audio/aac", "audio/ogg", "audio/webm", "video/mp4", "video/webm", "video/quicktime" };
    private readonly AppDbContext _db;
    private readonly IR2ObjectStorage _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminIngestionController> _logger;
    private readonly IngestionWorkflowDispatcher _workflowDispatcher;

    public AdminIngestionController(AppDbContext db, IR2ObjectStorage storage, IConfiguration configuration, ILogger<AdminIngestionController> logger, IngestionWorkflowDispatcher workflowDispatcher)
    { _db = db; _storage = storage; _configuration = configuration; _logger = logger; _workflowDispatcher = workflowDispatcher; }

    public sealed record UploadIntentDto([Required, StringLength(255)] string FileName, [Required, StringLength(128)] string ContentType, [Range(1, MaxMediaBytes)] long Size, [StringLength(100)] string? SelectedModel);
    public sealed record CrawlJobDto([Required, MinLength(1), MaxLength(10)] List<string> Urls, [StringLength(100)] string? SelectedModel);
    public sealed record WorkerCompletionDto([Required] Guid LeaseId, ScenarioConfigJson? Scenario, [StringLength(80)] string? ErrorCode);

    [HttpPost("upload-intents")]
    [EnableRateLimiting("admin_ingestion")]
    public async Task<IActionResult> CreateUploadIntent([FromBody] UploadIntentDto dto, CancellationToken cancellationToken)
    {
        if (!AllowedMediaTypes.Contains(dto.ContentType)) return BadRequest(new { message = "Unsupported audio or video content type." });
        var userId = GetUserId();
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var extension = Path.GetExtension(dto.FileName).ToLowerInvariant();
        if (extension.Length is < 2 or > 10) return BadRequest(new { message = "File name must include a supported extension." });
        var objectKey = $"ingestion/{userId:N}/{jobId:N}/{artifactId:N}{extension}";
        var artifact = new SourceArtifact { Id = artifactId, CreatedByUserId = userId, Kind = IngestionSourceKind.Audio, OriginalFileName = Path.GetFileName(dto.FileName), ContentType = dto.ContentType, ExpectedBytes = dto.Size, ObjectKey = objectKey, ExpiresAt = DateTime.UtcNow.AddHours(24) };
        var job = new IngestionJob { Id = jobId, CreatedByUserId = userId, SourceArtifactId = artifactId, SourceKind = IngestionSourceKind.Audio, Status = "AwaitingUpload", SelectedModel = dto.SelectedModel };
        _db.AddRange(artifact, job);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            var uploadUrl = await _storage.CreateUploadUrlAsync(objectKey, dto.ContentType, cancellationToken);
            return Ok(new { jobId, artifactId, uploadUrl, expiresInSeconds = 600 });
        }
        catch
        {
            _db.RemoveRange(artifact, job);
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    [HttpPost("artifacts/{artifactId:guid}/complete")]
    [EnableRateLimiting("admin_ingestion")]
    public async Task<IActionResult> CompleteUpload(Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await _db.SourceArtifacts.SingleOrDefaultAsync(item => item.Id == artifactId && item.CreatedByUserId == GetUserId(), cancellationToken);
        if (artifact is null) return NotFound();
        if (artifact.Status != "AwaitingUpload") return Conflict(new { message = "This upload has already been completed." });
        var actualBytes = await _storage.GetObjectLengthAsync(artifact.ObjectKey, cancellationToken);
        if (actualBytes <= 0 || actualBytes > artifact.ExpectedBytes || actualBytes > MaxMediaBytes) return BadRequest(new { message = "Uploaded file did not match the approved size." });
        artifact.ActualBytes = actualBytes;
        artifact.Status = "Ready";
        var job = await _db.IngestionJobs.SingleAsync(item => item.SourceArtifactId == artifactId, cancellationToken);
        job.Status = "Queued";
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        var workerDispatchRequested = await _workflowDispatcher.TryDispatchAsync(cancellationToken);
        return Accepted(new { jobId = job.Id, status = job.Status, workerDispatchRequested });
    }

    [HttpPost("crawl-jobs")]
    [EnableRateLimiting("admin_ingestion")]
    public async Task<IActionResult> CreateCrawlJob([FromBody] CrawlJobDto dto, CancellationToken cancellationToken)
    {
        var urls = dto.Urls.Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (urls.Count is < 1 or > 10 || urls.Any(url => url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))) return BadRequest(new { message = "Every source must be a public HTTP(S) URL." });
        var job = new IngestionJob { Id = Guid.NewGuid(), CreatedByUserId = GetUserId(), SourceKind = IngestionSourceKind.Url, SourceUrlsData = JsonSerializer.Serialize(urls), SelectedModel = dto.SelectedModel, Status = "Queued" };
        _db.IngestionJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        var workerDispatchRequested = await _workflowDispatcher.TryDispatchAsync(cancellationToken);
        return Accepted(new { jobId = job.Id, status = job.Status, workerDispatchRequested });
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.IngestionJobs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == jobId && item.CreatedByUserId == GetUserId(), cancellationToken);
        if (job is null) return NotFound();
        var artifactName = job.SourceArtifactId is Guid artifactId
            ? await _db.SourceArtifacts.AsNoTracking().Where(item => item.Id == artifactId).Select(item => item.OriginalFileName).SingleOrDefaultAsync(cancellationToken)
            : null;
        return Ok(ToClientJob(job, artifactName));
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs([FromQuery] int limit = 25, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 50);
        var jobs = await _db.IngestionJobs
            .AsNoTracking()
            .Where(item => item.CreatedByUserId == GetUserId())
            .OrderByDescending(item => item.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var artifactIds = jobs
            .Where(item => item.SourceArtifactId.HasValue)
            .Select(item => item.SourceArtifactId!.Value)
            .ToArray();
        var artifactNames = artifactIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.SourceArtifacts
                .AsNoTracking()
                .Where(item => artifactIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.OriginalFileName, cancellationToken);

        return Ok(jobs.Select(job => ToClientJob(job, artifactNames.GetValueOrDefault(job.SourceArtifactId ?? Guid.Empty), includeDraft: false)));
    }

    /// <summary>Records that an admin-reviewed ingestion draft has been published.</summary>
    [HttpPost("jobs/{jobId:guid}/mark-published")]
    [EnableRateLimiting("admin_ingestion")]
    public async Task<IActionResult> MarkPublished(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.IngestionJobs.SingleOrDefaultAsync(
            item => item.Id == jobId && item.CreatedByUserId == GetUserId(), cancellationToken);
        if (job is null) return NotFound();
        if (job.Status != "AwaitingReview" && job.Status != "Published")
            return Conflict(new { message = "Only a reviewed ingestion draft can be marked as published." });

        job.Status = "Published";
        job.ErrorCode = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToClientJob(job));
    }

    [AllowAnonymous, HttpPost("worker/claim")]
    public async Task<IActionResult> ClaimWork(CancellationToken cancellationToken)
    {
        if (!IsWorkerRequest()) return Unauthorized();
        await CleanupExpiredArtifacts(cancellationToken);
        await RecoverExpiredLeases(cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var job = await _db.IngestionJobs.FromSqlInterpolated($@"SELECT * FROM ingestion_jobs WHERE status = 'Queued' AND available_at <= {now} FOR UPDATE SKIP LOCKED").OrderBy(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (job is null) { await transaction.CommitAsync(cancellationToken); return NoContent(); }
        job.Status = "Processing"; job.Attempts++; job.LeaseId = Guid.NewGuid(); job.LeaseExpiresAt = now.AddMinutes(15); job.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        SourceArtifact? artifact = job.SourceArtifactId is null ? null : await _db.SourceArtifacts.SingleAsync(item => item.Id == job.SourceArtifactId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        string? downloadUrl;
        try
        {
            downloadUrl = artifact is null ? null : await _storage.CreateDownloadUrlAsync(artifact.ObjectKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not create a download URL for claimed ingestion job {JobId}; releasing its claim.", job.Id);
            job.Status = "Queued";
            job.Attempts = Math.Max(0, job.Attempts - 1);
            job.LeaseId = null;
            job.LeaseExpiresAt = null;
            job.AvailableAt = DateTime.UtcNow.AddMinutes(1);
            job.ErrorCode = "storage_unavailable";
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Ingestion storage is temporarily unavailable." });
        }
        return Ok(new { jobId = job.Id, leaseId = job.LeaseId, sourceKind = job.SourceKind.ToString(), urls = JsonSerializer.Deserialize<List<string>>(job.SourceUrlsData) ?? [], selectedModel = job.SelectedModel, artifact = artifact is null ? null : new { artifact.OriginalFileName, artifact.ContentType, downloadUrl } });
    }

    [AllowAnonymous, HttpPost("worker/jobs/{jobId:guid}/complete")]
    public async Task<IActionResult> CompleteWork(Guid jobId, [FromBody] WorkerCompletionDto dto, CancellationToken cancellationToken)
    {
        if (!IsWorkerRequest()) return Unauthorized();
        var job = await _db.IngestionJobs.SingleOrDefaultAsync(item => item.Id == jobId && item.Status == "Processing" && item.LeaseId == dto.LeaseId && item.LeaseExpiresAt > DateTime.UtcNow, cancellationToken);
        if (job is null) return Conflict();
        if (dto.Scenario is not null)
        {
            job.DraftData = JsonSerializer.Serialize(dto.Scenario);
            job.Status = "AwaitingReview";
            job.ErrorCode = null;
        }
        else
        {
            job.Status = job.Attempts >= job.MaxAttempts ? "Failed" : "Queued";
            job.ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode) ? "processing_failed" : dto.ErrorCode;
            job.AvailableAt = DateTime.UtcNow.AddMinutes(Math.Min(10, job.Attempts * 2));
        }
        job.LeaseId = null; job.LeaseExpiresAt = null; job.UpdatedAt = DateTime.UtcNow;
        if (job.SourceArtifactId is Guid artifactId)
        {
            var artifact = await _db.SourceArtifacts.SingleAsync(item => item.Id == artifactId, cancellationToken);
            artifact.Status = dto.Scenario is null ? "RetryPending" : "Processed";
        }
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToClientJob(job));
    }

    private object ToClientJob(IngestionJob job, string? artifactName = null, bool includeDraft = true) => new
    {
        jobId = job.Id,
        job.Status,
        job.ErrorCode,
        job.Attempts,
        job.CreatedAt,
        job.UpdatedAt,
        job.SelectedModel,
        sourceLabel = GetSourceLabel(job, artifactName),
        hasDraft = job.DraftData is not null,
        draft = includeDraft && job.DraftData is not null ? JsonSerializer.Deserialize<ScenarioConfigJson>(job.DraftData) : null,
    };

    private static string GetSourceLabel(IngestionJob job, string? artifactName)
    {
        if (!string.IsNullOrWhiteSpace(artifactName)) return artifactName;
        if (job.SourceKind != IngestionSourceKind.Url) return "Video/audio upload";
        try
        {
            var urls = JsonSerializer.Deserialize<List<string>>(job.SourceUrlsData) ?? [];
            return urls.Count switch
            {
                0 => "Public URL",
                1 => urls[0],
                _ => $"{urls[0]} (+{urls.Count - 1})",
            };
        }
        catch (JsonException)
        {
            return "Public URL";
        }
    }
    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
    private bool IsWorkerRequest()
    {
        var configured = _configuration["Ingestion:WorkerKey"]?.Trim();
        return !string.IsNullOrWhiteSpace(configured) && configured.Length >= 32 && Request.Headers.TryGetValue("X-Ingestion-Worker-Key", out var supplied) && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied.ToString()), System.Text.Encoding.UTF8.GetBytes(configured));
    }

    private async Task CleanupExpiredArtifacts(CancellationToken cancellationToken)
    {
        var expired = await _db.SourceArtifacts.Where(item => item.ExpiresAt <= DateTime.UtcNow && item.Status != "Expired").Take(20).ToListAsync(cancellationToken);
        foreach (var artifact in expired)
        {
            try
            {
                await _storage.DeleteAsync(artifact.ObjectKey, cancellationToken);
                artifact.Status = "Expired";
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not remove expired source artifact {ArtifactId}.", artifact.Id);
            }
        }
        if (expired.Count > 0) await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecoverExpiredLeases(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.IngestionJobs
            .Where(item => item.Status == "Processing" && item.LeaseExpiresAt <= now)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var job in expired)
        {
            job.Status = job.Attempts >= job.MaxAttempts ? "Failed" : "Queued";
            job.ErrorCode = "lease_expired";
            job.LeaseId = null;
            job.LeaseExpiresAt = null;
            job.AvailableAt = now;
            job.UpdatedAt = now;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Recovered {Count} ingestion jobs with expired worker leases.", expired.Count);
        }
    }
}
