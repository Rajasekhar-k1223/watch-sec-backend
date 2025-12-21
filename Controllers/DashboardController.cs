using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/dashboard")]
[Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMongoClient _mongo;

    public DashboardController(AppDbContext db, IMongoClient mongo)
    {
        _db = db;
        _mongo = mongo;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        // 1. Agent Stats (MySQL) - Get LATEST only
        var allReports = await _db.AgentReports.ToListAsync();
        var reports = allReports
            .GroupBy(r => r.AgentId)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .ToList();

        var totalAgents = reports.Count;
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-2);
        var onlineAgents = reports.Count(r => r.Timestamp >= onlineThreshold);
        var offlineAgents = totalAgents - onlineAgents;
        var avgCpu = totalAgents > 0 ? reports.Average(r => r.CpuUsage) : 0;
        var avgMem = totalAgents > 0 ? reports.Average(r => r.MemoryUsage) : 0;

        // 2. Event Stats (MongoDB)
        var collection = _mongo.GetDatabase("watchsec").GetCollection<SecurityEventLog>("events");
        var last24h = DateTime.UtcNow.AddHours(-24);
        var eventFilter = Builders<SecurityEventLog>.Filter.Gte(x => x.Timestamp, last24h);
        
        var events = await collection.Find(eventFilter).ToListAsync();
        var totalEvents = events.Count;
        
        // Group by Type
        var eventsByType = events.GroupBy(e => e.Type)
                                 .Select(g => new { Type = g.Key, Count = g.Count() })
                                 .ToList();

        // Group by Hour (for Chart)
        var eventsByHour = events.GroupBy(e => e.Timestamp.Hour)
                                 .Select(g => new { Hour = g.Key, Count = g.Count() })
                                 .OrderBy(x => x.Hour)
                                 .ToList();

        return Ok(new 
        {
            Agents = new { Total = totalAgents, Online = onlineAgents, Offline = offlineAgents },
            Resources = new { AvgCpu = avgCpu, AvgMem = avgMem },
            Threats = new { Total24h = totalEvents, ByType = eventsByType, Trend = eventsByHour }
        });
    }
}
