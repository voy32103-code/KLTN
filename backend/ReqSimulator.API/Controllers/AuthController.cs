using Microsoft.AspNetCore.Mvc;
using ReqSimulator.API.Services;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    public record RegisterDto(string Name, string Email, string Password);
    public record LoginDto(string Email, string Password);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var user = await _auth.Register(dto.Name, dto.Email, dto.Password);
            return Ok(new { user.Id, user.Name, user.Email, user.Role });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
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
