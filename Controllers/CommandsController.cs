using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
// using watch_sec_backend.Hubs; // StreamHub is in root namespace

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/commands")]
[Authorize(Roles = "TenantAdmin,SuperAdmin")] // Critical Security
public class CommandsController : ControllerBase
{
    private readonly IHubContext<StreamHub> _hub;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public CommandsController(IHubContext<StreamHub> hub, AppDbContext db, AuditService audit)
    {
        _hub = hub;
        _db = db;
        _audit = audit;
    }

    public class CommandRequest
    {
        public string Command { get; set; } = ""; // KillProcess, Isolate, Restart
        public string? Target { get; set; } // PID, ServiceName, etc.
    }

    [HttpPost("execute/{agentId}")]
    public async Task<IActionResult> ExecuteCommand(string agentId, [FromBody] CommandRequest req)
    {
        var username = User.Identity?.Name ?? "Unknown";
        
        // 1. Audit Log (Critical)
        await _audit.LogAsync(
            1, // Mock TenantId (ideally from Claims)
            username, 
            $"Execute Command: {req.Command}", 
            agentId, 
            $"Target: {req.Target}"
        );

        // 2. Send via SignalR
        // Agents should be in a specific Group or we use Clients.All (filtering on client side) 
        // Best practice: Clients.Group(agentId) if groups are managed, or Clients.All for broadcast
        // For now, we'll broadcast and Agent checks ID.
        await _hub.Clients.All.SendAsync("ReceiveCommand", agentId, req.Command, req.Target);

        return Ok(new { Status = "Sent", Message = $"Command '{req.Command}' sent to {agentId}" });
    }
}
