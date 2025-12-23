using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/policies")]
[Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
public class PolicyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public PolicyController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicies([FromQuery] int? tenantId)
    {
        var query = _db.Policies.AsQueryable();
        if (tenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == tenantId.Value);
        }
        return Ok(await query.ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> CreatePolicy([FromBody] Policy policy)
    {
        policy.CreatedAt = DateTime.UtcNow;
        _db.Policies.Add(policy);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(policy.TenantId, "SuperAdmin", "Create Policy", $"Policy #{policy.Id}", $"Created policy '{policy.Name}'");

        return Created($"/api/policies/{policy.Id}", policy);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePolicy(int id, [FromBody] Policy updated)
    {
        var policy = await _db.Policies.FindAsync(id);
        if (policy == null) return NotFound();

        policy.Name = updated.Name;
        policy.RulesJson = updated.RulesJson;
        policy.Actions = updated.Actions;
        policy.IsActive = updated.IsActive;
        policy.BlockedAppsJson = updated.BlockedAppsJson;
        policy.BlockedWebsitesJson = updated.BlockedWebsitesJson;
        
        await _db.SaveChangesAsync();

        await _audit.LogAsync(policy.TenantId, "SuperAdmin", "Update Policy", $"Policy #{id}", $"Updated policy '{policy.Name}'");

        return Ok(policy);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePolicy(int id)
    {
        var policy = await _db.Policies.FindAsync(id);
        if (policy == null) return NotFound();

        _db.Policies.Remove(policy);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(policy.TenantId, "SuperAdmin", "Delete Policy", $"Policy #{id}", $"Deleted policy '{policy.Name}'");

        return NoContent();
    }
}
