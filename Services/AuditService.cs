using Microsoft.EntityFrameworkCore;
using watch_sec_backend;

public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(int tenantId, string actor, string action, string target, string details)
    {
        try 
        {
            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                Actor = actor,
                Action = action,
                Target = target,
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Audit] FAILED to log: {ex.Message}");
        }
    }
}
