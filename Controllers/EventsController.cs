using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/events")]
[Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
public class EventsController : ControllerBase
{
    private readonly IMongoClient _mongo;
    private readonly AppDbContext _sqlDb; // Inject SQL DB for Tenant Lookup

    public EventsController(IMongoClient mongo, AppDbContext sqlDb)
    {
        _mongo = mongo;
        _sqlDb = sqlDb;
    }

    [HttpGet("{agentId}")]
    public async Task<IActionResult> GetEvents(string agentId)
    {
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<SecurityEventLog>("events");
        
        var filter = Builders<SecurityEventLog>.Filter.Eq(x => x.AgentId, agentId);
        var sort = Builders<SecurityEventLog>.Sort.Descending(x => x.Timestamp);
        
        var events = await collection.Find(filter).Sort(sort).Limit(100).ToListAsync();
        
        return Ok(events);
    }

    [HttpPost("activity")]
    public async Task<IActionResult> LogActivity([FromBody] ActivityLogDto dto)
    {
        // 1. Validate Tenant
        var tenant = await _sqlDb.Tenants.FirstOrDefaultAsync(t => t.ApiKey == dto.TenantApiKey);
        if (tenant == null) return Unauthorized();

        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<ActivityLog>("activity");

        var log = new ActivityLog
        {
            AgentId = dto.AgentId,
            TenantId = tenant.Id, // Link to Tenant
            ActivityType = dto.ActivityType,
            WindowTitle = dto.WindowTitle,
            ProcessName = dto.ProcessName,
            Url = dto.Url,
            DurationSeconds = dto.DurationSeconds,
            Timestamp = dto.Timestamp
        };

        // AI ANALYSIS (Phase 8)
        // Analyze "ProcessName" or "WindowTitle" for sentiment/threats
        var textToAnalyze = (log.ProcessName ?? "") + " " + (log.WindowTitle ?? "");
        var analysis = SentimentEngine.AnalyzeText(textToAnalyze);
        
        if (analysis.Score > 0)
        {
            log.RiskScore = analysis.Score;
            log.RiskLevel = analysis.RiskLevel;
        }

        await collection.InsertOneAsync(log);
        return Ok();
    }

    public record ActivityLogDto(string AgentId, string TenantApiKey, string ActivityType, string WindowTitle, string ProcessName, string Url, double DurationSeconds, DateTime Timestamp);

    [HttpGet("activity/{agentId}")]
    public async Task<IActionResult> GetActivity(string agentId)
    {
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<ActivityLog>("activity");
        
        var filter = Builders<ActivityLog>.Filter.Eq(x => x.AgentId, agentId);
        var sort = Builders<ActivityLog>.Sort.Descending(x => x.Timestamp);
        
        // Get last 24 hours
        var events = await collection.Find(filter).Sort(sort).Limit(500).ToListAsync();
        
        return Ok(events);
    }
    [HttpPost("simulate/{agentId}")]
    public async Task<IActionResult> SimulateEvent(string agentId)
    {
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<SecurityEventLog>("events");

        var evt = new SecurityEventLog
        {
            AgentId = agentId,
            Type = "Simulated Threat",
            Details = "This is a test event triggered from the Dashboard.",
            Timestamp = DateTime.UtcNow
        };

        await collection.InsertOneAsync(evt);
        return Ok(new { message = "Event Simulated" });
    }
}
