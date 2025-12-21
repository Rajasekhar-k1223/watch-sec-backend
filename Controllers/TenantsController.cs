using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize(Roles = "SuperAdmin")] // RBAC: Only SuperAdmin can manage tenants
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TenantsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetTenants()
    {
        var tenants = await _db.Tenants.ToListAsync();
        return Ok(tenants);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantDto dto)
    {
        if (string.IsNullOrEmpty(dto.Name)) return BadRequest("Name is required");

        var tenant = new Tenant
        {
            Name = dto.Name,
            ApiKey = Guid.NewGuid().ToString(), // Auto-generate API Key
            Plan = dto.Plan ?? "Starter"
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        return Ok(tenant);
    }
}

public record CreateTenantDto(string Name, string? Plan);
