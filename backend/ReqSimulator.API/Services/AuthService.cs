using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReqSimulator.API.Data;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Services;

/// <summary>
/// Xử lý Register + Login.
/// - Register: hash password bằng BCrypt, lưu vào DB.
/// - Login: verify password (BCrypt / SHA256 fallback) → tạo JWT token.
/// </summary>
public class AuthService
{
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<User> Register(string name, string email, string password, UserRole role = UserRole.Student)
    {
        var normalizedName = name?.Trim() ?? "";
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? "";
        if (normalizedName.Length is < 2 or > 100)
            throw new InvalidOperationException("Tên không hợp lệ.");
        if (normalizedEmail.Length > 255 || !MailAddress.TryCreate(normalizedEmail, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Email không hợp lệ.");
        if (password is null || password.Length is < 12 or > 128)
            throw new InvalidOperationException("Mật khẩu không hợp lệ.");

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            throw new InvalidOperationException("Email đã tồn tại.");

        var user = new User
        {
            Name = normalizedName,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<string> Login(string email, string password)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? "";
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        if (user is null)
        {
            // Perform a BCrypt verification even for unknown accounts to reduce
            // observable timing differences that can enable account enumeration.
            BCrypt.Net.BCrypt.Verify(password ?? "", DummyPasswordHash);
            throw new UnauthorizedAccessException("Thông tin đăng nhập không chính xác.");
        }

        var isValidPassword = VerifyPassword(password, user.PasswordHash, out var needsUpgrade);
        if (isValidPassword && needsUpgrade)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            await _db.SaveChangesAsync();
        }

        if (!isValidPassword)
            throw new UnauthorizedAccessException("Thông tin đăng nhập không chính xác");

        return GenerateToken(user);
    }

    private static bool VerifyPassword(string password, string storedHash, out bool needsUpgrade)
    {
        needsUpgrade = storedHash.Length == 64 && storedHash.All(Uri.IsHexDigit);
        if (needsUpgrade)
            return IsSha256Match(password, storedHash);

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, storedHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
    private static bool IsSha256Match(string password, string storedHash)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return string.Equals(hash, storedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tạo JWT chứa userId, email, role — hết hạn theo config</summary>
    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(
                double.Parse(_config["Jwt:ExpiresInHours"]!)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
