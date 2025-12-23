using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using watch_sec_backend.Services;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IEmailService _email;

    public AuthController(AppDbContext db, IConfiguration config, IEmailService email)
    {
        _db = db;
        _config = config;
        _email = email;
    }

    [HttpPost("register-tenant")]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantDto dto)
    {
        // 1. Validation
        if (await _db.Tenants.AnyAsync(t => t.Name == dto.TenantName))
            return BadRequest("Organization name already exists.");
        
        if (await _db.Users.AnyAsync(u => u.Username == dto.AdminUsername))
            return BadRequest("Username already taken.");

        // 2. Create Tenant
        var planLimit = dto.Plan == "Professional" ? 50 : 5; // Simple Logic
        
        var tenant = new Tenant 
        { 
            Name = dto.TenantName,
            ApiKey = Guid.NewGuid().ToString(),
            Plan = dto.Plan ?? "Starter",
            AgentLimit = planLimit,
            NextBillingDate = DateTime.UtcNow.AddDays(30)
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        // 3. Create Admin User
        var user = new User 
        { 
            Username = dto.AdminUsername,
            PasswordHash = dto.Password, // In prod, hash this!
            Role = "TenantAdmin",
            TenantId = tenant.Id
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // A. Send Welcome Email to New Tenant
        var emailTarget = dto.AdminUsername.Contains("@") ? dto.AdminUsername : $"{dto.AdminUsername}@example.com";
        await _email.SendEmailAsync(
            emailTarget,
            "Welcome to Watch Sec!",
            $@"
                <h1>Welcome to Watch Sec</h1>
                <p>Your Organization <b>{tenant.Name}</b> has been successfully created.</p>
                <p><b>Plan:</b> {tenant.Plan}</p>
                <p><b>Your API Key:</b> {tenant.ApiKey}</p>
                <p>Get started by logging in and downloading the agent from your dashboard.</p>
            "
        );

        // B. Send Notification to Super Admin
        var adminEmail = _config["Email:AdminNotificationTo"] ?? "admin@watchsec.io";
        await _email.SendEmailAsync(
            adminEmail,
            $"[New Sign-up] {tenant.Name}",
            $@"
                <h2>New Tenant Registered</h2>
                <ul>
                    <li><b>Organization:</b> {tenant.Name}</li>
                    <li><b>Admin User:</b> {user.Username}</li>
                    <li><b>Plan:</b> {tenant.Plan}</li>
                    <li><b>Date:</b> {DateTime.UtcNow}</li>
                </ul>
            "
        );

        // 5. Generate Token (Auto-Login)
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_config["Jwt:Secret"] ?? "super-secret-key-that-should-be-in-env-vars-and-very-long");
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("TenantId", user.TenantId.ToString())
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new 
        { 
            Token = tokenString, 
            User = new { user.Id, user.Username, user.Role, user.TenantId },
            Tenant = new { tenant.Id, tenant.Name, tenant.ApiKey }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        // ... (Existing Login Logic) ...
        // 1. Validate User
        // In real app: Compare Hashed Password
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username && u.PasswordHash == dto.Password);
        
        if (user == null)
        {
            return Unauthorized("Invalid credentials");
        }

        // 2. Generate JWT
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_config["Jwt:Secret"] ?? "super-secret-key-that-should-be-in-env-vars-and-very-long");
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("TenantId", user.TenantId?.ToString() ?? "")
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new 
        { 
            Token = tokenString, 
            User = new { user.Id, user.Username, user.Role, user.TenantId } 
        });
    }
}

public record LoginDto(string Username, string Password);
public record RegisterTenantDto(string TenantName, string AdminUsername, string Password, string Plan);
