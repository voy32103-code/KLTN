using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>Thống kê tổng quan cho Admin Dashboard</summary>
    [HttpGet("stats/overview")]
    public async Task<IActionResult> GetOverviewStats()
    {
        var totalSessions = await _db.SimulationSessions.CountAsync();
        var totalStudents = await _db.Users.CountAsync(u => u.Role == UserRole.Student);
        var totalScenarios = await _db.Scenarios.CountAsync(s => s.IsActive);

        var evaluations = await _db.EvaluationResults.AsNoTracking().ToListAsync();
        var averageCoverage = evaluations.Count > 0
            ? Math.Round(evaluations.Average(e => (double)(e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m)), 1)
            : 0.0;

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

    /// <summary>Phân bổ Coverage Score theo 5 khoảng (histogram)</summary>
    [HttpGet("stats/coverage-distribution")]
    public async Task<IActionResult> GetCoverageDistribution()
    {
        var evaluations = await _db.EvaluationResults.AsNoTracking().ToListAsync();

        int bin1 = 0, bin2 = 0, bin3 = 0, bin4 = 0, bin5 = 0;
        foreach (var e in evaluations)
        {
            var score = (double)(e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m);
            if (score <= 20) bin1++;
            else if (score <= 40) bin2++;
            else if (score <= 60) bin3++;
            else if (score <= 80) bin4++;
            else bin5++;
        }

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

    /// <summary>Thống kê hiệu suất theo kịch bản (Scenario)</summary>
    [HttpGet("stats/by-scenario")]
    public async Task<IActionResult> GetScenarioStats()
    {
        var scenarios = await _db.Scenarios.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var sessions = await _db.SimulationSessions
            .AsNoTracking()
            .Include(s => s.EvaluationResult)
            .Include(s => s.Messages)
            .ToListAsync();

        var scenarioGroups = sessions.GroupBy(s => s.ScenarioId).ToDictionary(g => g.Key, g => g.ToList());

        var result = scenarios.Select(s =>
        {
            var scenarioSessions = scenarioGroups.TryGetValue(s.Id, out var list) ? list : new List<SimulationSession>();
            var evaluations = scenarioSessions
                .Where(x => x.EvaluationResult != null)
                .Select(x => x.EvaluationResult!)
                .ToList();

            var avgCoverage = evaluations.Count > 0
                ? Math.Round(evaluations.Average(e => (double)(e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m)), 1)
                : 0.0;

            var avgTurns = scenarioSessions.Count > 0
                ? Math.Round(scenarioSessions.Average(x => x.Messages.Count(m => m.Sender == SenderType.Student)), 1)
                : 0.0;

            return new
            {
                scenarioId = s.Id,
                scenarioTitle = s.Title,
                sessionCount = scenarioSessions.Count,
                averageCoverage = avgCoverage,
                averageTurns = avgTurns
            };
        }).OrderByDescending(x => x.sessionCount).ToList();

        return Ok(result);
    }

    /// <summary>Bảng xếp hạng sinh viên (Top Students)</summary>
    [HttpGet("stats/top-students")]
    public async Task<IActionResult> GetTopStudents([FromQuery] int limit = 10)
    {
        limit = Math.Clamp(limit, 5, 50);

        var students = await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Student)
            .Include(u => u.Sessions)
                .ThenInclude(s => s.EvaluationResult)
            .ToListAsync();

        var result = students
            .Where(u => u.Sessions.Any(s => s.EvaluationResult != null))
            .Select(u =>
            {
                var evals = u.Sessions
                    .Where(s => s.EvaluationResult != null)
                    .Select(s => s.EvaluationResult!)
                    .ToList();

                var scores = evals.Select(e => (double)(e.OverriddenCoverageScore ?? e.CoverageScore ?? 0m)).ToList();

                return new
                {
                    studentId = u.Id,
                    studentName = u.Name,
                    studentEmail = u.Email,
                    sessionCount = u.Sessions.Count,
                    completedCount = evals.Count,
                    bestCoverage = Math.Round(scores.Max(), 1),
                    averageCoverage = Math.Round(scores.Average(), 1)
                };
            })
            .OrderByDescending(x => x.averageCoverage)
            .ThenByDescending(x => x.completedCount)
            .Take(limit)
            .ToList();

        return Ok(result);
    }

    /// <summary>Phân bổ các loại MatchType tổng thể</summary>
    [HttpGet("stats/match-type-breakdown")]
    public async Task<IActionResult> GetMatchTypeBreakdown()
    {
        var matches = await _db.RequirementMatches.AsNoTracking().ToListAsync();

        int exact = 0, semantic = 0, partial = 0, missed = 0;
        foreach (var m in matches)
        {
            var effectiveType = m.OverriddenMatchType ?? m.MatchType;
            switch (effectiveType)
            {
                case Models.MatchType.Exact: exact++; break;
                case Models.MatchType.Semantic: semantic++; break;
                case Models.MatchType.Partial: partial++; break;
                case Models.MatchType.Missed: default: missed++; break;
            }
        }

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
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == dto.Email.ToLower()))
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
            PasswordHash = HashPassword(dto.Password),
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

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == dto.Email.ToLower() && u.Id != id))
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
            user.PasswordHash = HashPassword(dto.NewPassword);
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

    /// <summary>Xóa người dùng</summary>
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        // Không cho phép tự xóa tài khoản của chính mình
        var currentUserIdClaim = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (Guid.TryParse(currentUserIdClaim, out var currentId) && currentId == id)
        {
            return BadRequest(new { message = "Không thể tự xóa tài khoản Admin đang đăng nhập." });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Đã xóa người dùng thành công." });
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
