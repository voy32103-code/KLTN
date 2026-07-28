using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using ReqSimulator.API.Services;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth_strict")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    public sealed record RegisterDto(
        [Required, StringLength(100, MinimumLength = 2)] string Name,
        [Required, EmailAddress, StringLength(255)] string Email,
        [Required, StringLength(128, MinimumLength = 12)] string Password);

    public sealed record LoginDto(
        [Required, EmailAddress, StringLength(255)] string Email,
        [Required, StringLength(128, MinimumLength = 1)] string Password);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var user = await _auth.Register(dto.Name, dto.Email, dto.Password);
            return Ok(new { user.Id, user.Name, user.Email, user.Role });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Không thể tạo tài khoản với thông tin đã cung cấp." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            var token = await _auth.Login(dto.Email, dto.Password);
            return Ok(new { token });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Email hoặc mật khẩu không chính xác." });
        }
    }
}
