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
    public async Task<IActionResult> GetStats([FromQuery] int hours = 24)
    {
        // 1. Agent Stats (MySQL) - Filter by date for performance, but need "Latest" for status
        // Strategy: 
        // A. For "Current Status" (Online/Offline), we technically only need the very last report per agent.
        //    But finding "Last per Group" in EF Core can be tricky/slow without raw SQL or window functions.
        //    Since we also want "Resource Trends" over the 'hours', fetching reports in that window is actually desireable.
        
        var startTime = DateTime.UtcNow.AddHours(-Math.Abs(hours));

        // Fetch reports only within the window (plus a buffer for "Online" check if window is tiny, but 'hours' >= 1)
        // Optimization: Select only needed columns? For now, fetch all is fine if window isn't massive.
        var windowReports = await _db.AgentReports
            .Where(r => r.Timestamp >= startTime)
            .ToListAsync();

        // 1a. Current Fleet Status (Using latest report per agent within window)
        // Note: If an agent hasn't reported in 'hours', it's effectively "Offline" or not relevant to this specific view window.
        // However, for "Total Agents" count, we might want ALL agents ever? 
        // Let's stick to: "Agents Active/Seen in this Window".
        
        var latestPerAgent = windowReports
            .GroupBy(r => r.AgentId)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .ToList();

        var totalAgents = latestPerAgent.Count;
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-5);
        var onlineAgents = latestPerAgent.Count(r => r.Timestamp >= onlineThreshold);
        var offlineAgents = totalAgents - onlineAgents; 
        
        var avgCpu = totalAgents > 0 ? latestPerAgent.Average(r => r.CpuUsage) : 0;
        var avgMem = totalAgents > 0 ? latestPerAgent.Average(r => r.MemoryUsage) : 0;

        // 1b. Resource Trend (Group by Hour)
        var resourceTrend = windowReports
            .GroupBy(r => r.Timestamp.ToString("yyyy-MM-dd HH:00")) // Simple scaling
            .Select(g => new 
            { 
                Time = g.Key, 
                Cpu = Math.Round(g.Average(r => r.CpuUsage), 1),
                Mem = Math.Round(g.Average(r => r.MemoryUsage), 1)
            })
            .OrderBy(x => x.Time)
            .ToList();

        // 2. Event Stats (MongoDB)
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<SecurityEventLog>("events");
        
        var eventFilter = Builders<SecurityEventLog>.Filter.Gte(x => x.Timestamp, startTime);

        
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

        // 3. Recent Logs (Stream) - Limit to filter window + top 50
        var recentLogs = await collection.Find(eventFilter)
                                         .SortByDescending(x => x.Timestamp)
                                         .Limit(50)
                                         .ToListAsync();

        // 4. Network Stats (Mocked)
        var networkStats = new 
        {
            InboundMbps = new Random().NextRound(10, 500),
            OutboundMbps = new Random().NextRound(5, 200),
            ActiveConnections = new Random().Next(50, 2000)
        };

        // 5. Risky Assets (Top 5 Agents by Threats)
        var riskyAssets = events.GroupBy(e => e.AgentId)
                                .Select(g => new { AgentId = g.Key, ThreatCount = g.Count() })
                                .OrderByDescending(x => x.ThreatCount)
                                .Take(5)
                                .ToList();

        // 6. Global Productivity Score (Filter Window)
        var actCollection = _mongo.GetDatabase("WatchSecDB").GetCollection<ActivityLog>("ActivityLogs");
        var recentActs = await actCollection.Find(Builders<ActivityLog>.Filter.Gte(x => x.Timestamp, startTime)).ToListAsync();
        
        double globalScore = 0;
        if (recentActs.Count > 0)
        {
            double prod = 0, total = 0;
            foreach (var act in recentActs)
            {
                var cat = Helpers.AppClassifier.Classify(act.ProcessName, act.WindowTitle);
                if (cat == "Productive") prod += act.DurationSeconds;
                total += act.DurationSeconds;
            }
            if (total > 0) globalScore = Math.Round((prod / total) * 100, 1);
        }

        return Ok(new 
        {
            Agents = new { Total = totalAgents, Online = onlineAgents, Offline = offlineAgents },
            Resources = new { AvgCpu = Math.Round(avgCpu, 1), AvgMem = Math.Round(avgMem, 1), Trend = resourceTrend },
            Threats = new { Total24h = totalEvents, ByType = eventsByType, Trend = eventsByHour },
            RecentLogs = recentLogs,
            Network = networkStats,
            RiskyAssets = riskyAssets,
            Productivity = new { GlobalScore = globalScore }
        });
    }

    // Phase 16: Network Topology Data
    [HttpGet("topology")]
    public async Task<IActionResult> GetTopology()
    {
        // Fetch all agents (Config table)
        // We might want to filter by "Active recently" (e.g., last 7 days) to avoid ghosts
        var activeAgents = await _db.Agents
            .Where(a => a.LastSeen >= DateTime.UtcNow.AddDays(-7))
            .Select(a => new 
            {
                a.AgentId,
                a.LocalIp,
                a.Gateway,
                a.LastSeen,
                Status = a.LastSeen >= DateTime.UtcNow.AddMinutes(-5) ? "Online" : "Offline"
            })
            .ToListAsync();
            
        return Ok(activeAgents);
    }
}

public static class RandomExtensions
{
    public static double NextRound(this Random r, int min, int max)
    {
        return Math.Round(r.NextDouble() * (max - min) + min, 1);
    }
}
