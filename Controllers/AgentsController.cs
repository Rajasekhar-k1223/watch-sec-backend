using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using watch_sec_backend;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AgentsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("{agentId}/toggle-screenshots")]
    public async Task<IActionResult> ToggleScreenshots(string agentId, [FromQuery] bool enabled)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.AgentId == agentId);
        if (agent == null) return NotFound("Agent not found (must register first)");

        agent.ScreenshotsEnabled = enabled;
        await _db.SaveChangesAsync();

        return Ok(new { AgentId = agentId, ScreenshotsEnabled = agent.ScreenshotsEnabled });
    }

    [HttpGet("{agentId}")]
    public async Task<IActionResult> GetAgent(string agentId)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.AgentId == agentId);
        if (agent == null) return NotFound();
        return Ok(agent);
    }
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(int id)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent == null) return NotFound();

        _db.Agents.Remove(agent);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
