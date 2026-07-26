using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    // ==================== 1. ANALYTICS & STATS ENDPOINTS ====================

    /// <summary>Thống kê tổng quan cho Admin Dashboard (SQL aggregated)</summary>
    [HttpGet("stats/overview")]
    public async Task<IActionResult> GetOverviewStats()
    {
        var totalSessions = await _db.SimulationSessions.CountAsync();
        var totalStudents = await _db.Users.CountAsync(u => u.Role == UserRole.Student);
        var totalScenarios = await _db.Scenarios.CountAsync(s => s.IsActive);

        var avgScoreRaw = await _db.EvaluationResults
            .AsNoTracking()
            .AverageAsync(e => (double?)(e.OverriddenCoverageScore ?? e.CoverageScore)) ?? 0.0;
        var averageCoverage = Math.Round(avgScoreRaw, 1);

        var completedSessions = await _db.SimulationSessions
            .CountAsync(s => s.FinalizationStatus == SessionFinalizationStatus.Completed);
        var activeSessions = await _db.SimulationSessions
            .CountAsync(s => s.IsActive);

        return Ok(new
        {
            totalSessions,
            totalStudents,
            totalScenarios,
            averageCoverage,
            completedSessions,
            activeSessions
        });
    }

    /// <summary>Phân bổ Coverage Score theo 5 khoảng (histogram, SQL aggregated)</summary>
    [HttpGet("stats/coverage-distribution")]
    public async Task<IActionResult> GetCoverageDistribution()
    {
        var evaluations = _db.EvaluationResults.AsNoTracking();

        var bin1 = await evaluations.CountAsync(e => (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) <= 20m);
        var bin2 = await evaluations.CountAsync(e => (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) > 20m && (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) <= 40m);
        var bin3 = await evaluations.CountAsync(e => (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) > 40m && (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) <= 60m);
        var bin4 = await evaluations.CountAsync(e => (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) > 60m && (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) <= 80m);
        var bin5 = await evaluations.CountAsync(e => (e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m) > 80m);

        var bins = new[]
        {
            new { label = "0-20%", count = bin1 },
            new { label = "21-40%", count = bin2 },
            new { label = "41-60%", count = bin3 },
            new { label = "61-80%", count = bin4 },
            new { label = "81-100%", count = bin5 }
        };

        return Ok(new { bins });
    }

    /// <summary>Số lượng phiên interview theo ngày (30 ngày gần nhất)</summary>
    [HttpGet("stats/sessions-over-time")]
    public async Task<IActionResult> GetSessionsOverTime([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 7, 90);
        var startDate = DateTime.UtcNow.Date.AddDays(-days + 1);

        var sessionDates = await _db.SimulationSessions
            .AsNoTracking()
            .Where(s => s.StartedAt >= startDate)
            .Select(s => s.StartedAt.Date)
            .ToListAsync();

        var dateGroups = sessionDates
            .GroupBy(d => d.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.Count());

        var labels = new List<string>();
        var counts = new List<int>();

        for (int i = 0; i < days; i++)
        {
            var d = startDate.AddDays(i).ToString("yyyy-MM-dd");
            labels.Add(d);
            counts.Add(dateGroups.TryGetValue(d, out var c) ? c : 0);
        }

        return Ok(new { labels, counts });
    }

    /// <summary>Thống kê hiệu suất theo kịch bản (Scenario, SQL grouped)</summary>
    [HttpGet("stats/by-scenario")]
    public async Task<IActionResult> GetScenarioStats()
    {
        var scenarios = await _db.Scenarios.AsNoTracking().Where(s => s.IsActive).ToListAsync();

        var sessionStats = await _db.SimulationSessions
            .AsNoTracking()
            .GroupBy(s => s.ScenarioId)
            .Select(g => new
            {
                scenarioId = g.Key,
                sessionCount = g.Count(),
                averageCoverage = g.Where(s => s.EvaluationResult != null)
                    .Average(s => (double?)(s.EvaluationResult!.OverriddenCoverageScore ?? s.EvaluationResult!.CoverageScore)) ?? 0.0,
                averageTurns = g.Count() > 0
                    ? g.SelectMany(s => s.Messages).Count(m => m.Sender == SenderType.Student) / (double)g.Count()
                    : 0.0
            })
            .ToListAsync();

        var statsDict = sessionStats.ToDictionary(s => s.scenarioId);

        var result = scenarios.Select(s =>
        {
            var stat = statsDict.TryGetValue(s.Id, out var v) ? v : null;
            return new
            {
                scenarioId = s.Id,
                scenarioTitle = s.Title,
                sessionCount = stat?.sessionCount ?? 0,
                averageCoverage = Math.Round(stat?.averageCoverage ?? 0.0, 1),
                averageTurns = Math.Round(stat?.averageTurns ?? 0.0, 1)
            };
        }).OrderByDescending(x => x.sessionCount).ToList();

        return Ok(result);
    }

    /// <summary>Bảng xếp hạng sinh viên (Top Students, SQL projected)</summary>
    [HttpGet("stats/top-students")]
    public async Task<IActionResult> GetTopStudents([FromQuery] int limit = 10)
    {
        limit = Math.Clamp(limit, 5, 50);

        var studentsQuery = await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Student && u.Sessions.Any(s => s.EvaluationResult != null))
            .Select(u => new
            {
                studentId = u.Id,
                studentName = u.Name,
                studentEmail = u.Email,
                sessionCount = u.Sessions.Count,
                completedCount = u.Sessions.Count(s => s.EvaluationResult != null),
                bestCoverage = (double)(u.Sessions
                    .Where(s => s.EvaluationResult != null)
                    .Max(s => s.EvaluationResult!.OverriddenCoverageScore ?? s.EvaluationResult!.CoverageScore) ?? 0m),
                averageCoverage = (double)(u.Sessions
                    .Where(s => s.EvaluationResult != null)
                    .Average(s => s.EvaluationResult!.OverriddenCoverageScore ?? s.EvaluationResult!.CoverageScore) ?? 0m)
            })
            .OrderByDescending(x => x.averageCoverage)
            .ThenByDescending(x => x.completedCount)
            .Take(limit)
            .ToListAsync();

        var result = studentsQuery.Select(x => new
        {
            x.studentId,
            x.studentName,
            x.studentEmail,
            x.sessionCount,
            x.completedCount,
            bestCoverage = Math.Round(x.bestCoverage, 1),
            averageCoverage = Math.Round(x.averageCoverage, 1)
        }).ToList();

        return Ok(result);
    }

    /// <summary>Phân bổ các loại MatchType tổng thể (SQL aggregated)</summary>
    [HttpGet("stats/match-type-breakdown")]
    public async Task<IActionResult> GetMatchTypeBreakdown()
    {
        var matches = _db.RequirementMatches.AsNoTracking();

        var exact = await matches.CountAsync(m => (m.OverriddenMatchType ?? m.MatchType) == Models.MatchType.Exact);
        var semantic = await matches.CountAsync(m => (m.OverriddenMatchType ?? m.MatchType) == Models.MatchType.Semantic);
        var partial = await matches.CountAsync(m => (m.OverriddenMatchType ?? m.MatchType) == Models.MatchType.Partial);
        var missed = await matches.CountAsync(m => (m.OverriddenMatchType ?? m.MatchType) == Models.MatchType.Missed);

        return Ok(new { exact, semantic, partial, missed });
    }

    // ==================== 2. USER MANAGEMENT (CRUD) ENDPOINTS ====================

    /// <summary>Lấy danh sách tất cả người dùng</summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? role, [FromQuery] string? search)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var parsedRole))
        {
            query = query.Where(u => u.Role == parsedRole);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.Name.ToLower().Contains(s) || u.Email.ToLower().Contains(s));
        }

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                Role = u.Role.ToString(),
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    public record CreateUserDto(
        [Required, StringLength(100)] string Name,
        [Required, EmailAddress] string Email,
        [Required, StringLength(100, MinimumLength = 6)] string Password,
        [Required] string Role);

    /// <summary>Tạo mới người dùng (Student, Lecturer, hoặc Admin)</summary>
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        var targetEmailLower = (dto.Email ?? "").ToLower();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == targetEmailLower))
        {
            return BadRequest(new { message = "Email này đã được sử dụng." });
        }

        if (!Enum.TryParse<UserRole>(dto.Role, true, out var role))
        {
            return BadRequest(new { message = "Vai trò không hợp lệ (Student, Lecturer, Admin)." });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            user.Id,
            user.Name,
            user.Email,
            Role = user.Role.ToString(),
            user.CreatedAt
        });
    }

    public record UpdateUserDto(
        [Required, StringLength(100)] string Name,
        [Required, EmailAddress] string Email,
        [Required] string Role,
        string? NewPassword);

    /// <summary>Cập nhật thông tin người dùng</summary>
    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        var targetEmailLower = (dto.Email ?? "").ToLower();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == targetEmailLower && u.Id != id))
        {
            return BadRequest(new { message = "Email đã tồn tại cho tài khoản khác." });
        }

        if (!Enum.TryParse<UserRole>(dto.Role, true, out var role))
        {
            return BadRequest(new { message = "Vai trò không hợp lệ." });
        }

        user.Name = dto.Name.Trim();
        user.Email = dto.Email.Trim().ToLowerInvariant();
        user.Role = role;

        if (!string.IsNullOrWhiteSpace(dto.NewPassword) && dto.NewPassword.Length >= 6)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            user.Id,
            user.Name,
            user.Email,
            Role = user.Role.ToString(),
            user.CreatedAt
        });
    }

    /// <summary>Xóa người dùng có kiểm tra ràng buộc phụ thuộc dữ liệu</summary>
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        // 1. Không cho phép tự xóa tài khoản của chính mình
        var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(currentUserIdClaim, out var currentId) && currentId == id)
        {
            return BadRequest(new { message = "Không thể tự xóa tài khoản Admin đang đăng nhập." });
        }

        // 2. Kiểm tra ràng buộc dữ liệu liên quan (SimulationSessions, EvaluationResults, LecturerOverrides)
        var hasSessions = await _db.SimulationSessions.AnyAsync(s => s.StudentId == id);
        var hasOverrides = await _db.LecturerOverrides.AnyAsync(o => o.LecturerId == id);
        var hasEvaluations = await _db.EvaluationResults.AnyAsync(e => e.OverriddenByLecturerId == id);

        if (hasSessions || hasOverrides || hasEvaluations)
        {
            return BadRequest(new { message = "Không thể xóa người dùng này vì đã có dữ liệu phiên phỏng vấn hoặc đánh giá liên quan trong hệ thống. Bạn có thể thay đổi thông tin/vai trò thay vì xóa." });
        }

        try
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Đã xóa người dùng thành công." });
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "Không thể xóa người dùng do có ràng buộc dữ liệu liên quan khác." });
        }
    }
}
