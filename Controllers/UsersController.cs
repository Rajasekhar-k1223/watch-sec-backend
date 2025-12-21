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
}
