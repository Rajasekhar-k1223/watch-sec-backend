using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        // 1. Identify User Role and Tenant
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;
        int? tenantId = string.IsNullOrEmpty(tenantIdClaim) ? null : int.Parse(tenantIdClaim);

        if (role == "SuperAdmin")
        {
            // SuperAdmin sees ALL users
            var allUsers = await _db.Users
                .Select(u => new 
                { 
                    u.Id, 
                    u.Username, 
                    u.Role, 
                    u.TenantId,
                    TenantName = _db.Tenants.Where(t => t.Id == u.TenantId).Select(t => t.Name).FirstOrDefault() ?? "N/A"
                })
                .ToListAsync();
            return Ok(allUsers);
        }
        else if (role == "TenantAdmin" && tenantId.HasValue)
        {
            // TenantAdmin sees users in their OWN Tenant
            var tenantUsers = await _db.Users
                .Where(u => u.TenantId == tenantId.Value)
                .Select(u => new 
                { 
                    u.Id, 
                    u.Username, 
                    u.Role, 
                    u.TenantId,
                    TenantName = _db.Tenants.Where(t => t.Id == u.TenantId).Select(t => t.Name).FirstOrDefault()
                })
                .ToListAsync();
            return Ok(tenantUsers);
        }

        return Forbid();
    }
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, [FromServices] AuditService audit)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return NotFound();

        // Validate Old Password (Simple string comparison as per seed data)
        if (user.PasswordHash != req.OldPassword)
        {
            return BadRequest("Incorrect current password.");
        }

        user.PasswordHash = req.NewPassword;
        await _db.SaveChangesAsync();

        // Log
        await audit.LogAsync(user.TenantId ?? 0, user.Username, "Change Password", "User Account", "User changed their password.");

        return Ok(new { Message = "Password updated successfully." });
    }
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
