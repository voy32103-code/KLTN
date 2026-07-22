using System.IdentityModel.Tokens.Jwt;
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
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<User> Register(string name, string email, string password, UserRole role = UserRole.Student)
    {
        if (await _db.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("Email đã tồn tại");

        var user = new User
        {
            Name = name,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<string> Login(string email, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email)
            ?? throw new UnauthorizedAccessException("Thông tin đăng nhập không chính xác");

        bool isValidPassword = false;

        // 1. Kiểm tra bằng BCrypt chuẩn
        if (BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            isValidPassword = true;
        }
        // 2. Tương thích ngược: Nếu hash cũ dùng SHA-256 (chuỗi hex 64 ký tự)
        else if (user.PasswordHash.Length == 64 && IsSha256Match(password, user.PasswordHash))
        {
            isValidPassword = true;
            // Tự động nâng cấp mật khẩu sang BCrypt
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            await _db.SaveChangesAsync();
        }

        if (!isValidPassword)
            throw new UnauthorizedAccessException("Thông tin đăng nhập không chính xác");

        return GenerateToken(user);
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
