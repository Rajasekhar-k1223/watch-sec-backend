using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using watch_sec_backend;

[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] int? tenantId, [FromQuery] int limit = 100)
    {
        var query = _db.AuditLogs.AsQueryable();
        
        if (tenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == tenantId.Value);
        }

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync();

        return Ok(logs);
    }
}
