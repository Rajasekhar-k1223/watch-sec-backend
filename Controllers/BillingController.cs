using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using watch_sec_backend;

[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public BillingController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetBillingInfo([FromQuery] int tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound("Tenant not found");

        var agentCount = await _db.Agents.CountAsync(a => a.TenantId == tenantId);

        return Ok(new 
        {
            tenant.Id,
            tenant.Plan,
            tenant.AgentLimit,
            agentCount,
            tenant.NextBillingDate,
            DueAmount = tenant.Plan == "Enterprise" ? 499.00 : tenant.Plan == "Starter" ? 0.00 : 99.00
        });
    }

    [HttpPost("upgrade")]
    public async Task<IActionResult> UpgradePlan([FromQuery] int tenantId, [FromQuery] string newPlan)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound();

        var oldPlan = tenant.Plan;
        
        // Mock Logic
        tenant.Plan = newPlan;
        tenant.AgentLimit = newPlan == "Enterprise" ? 1000 : newPlan == "Pro" ? 50 : 5;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(tenantId, "TenantAdmin", "Upgrade Plan", $"Billing", $"Upgraded plan from {oldPlan} to {newPlan}");

        return Ok(new { Message = $"Successfully upgraded to {newPlan}", NewLimit = tenant.AgentLimit });
    }
}
