using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/productivity")]
public class ProductivityController : ControllerBase
{
    private readonly IMongoClient _mongo;
    
    public ProductivityController(IMongoClient mongo)
    {
        _mongo = mongo;
    }

    private string Classify(string processName, string windowTitle)
    {
        return Helpers.AppClassifier.Classify(processName, windowTitle);
    }

    [HttpGet("summary/{agentId}")]
    public async Task<IActionResult> GetSummary(string agentId)
    {
        var db = _mongo.GetDatabase("WatchSecDB");
        var coll = db.GetCollection<ActivityLog>("ActivityLogs");

        // Filter: Last 24 Hours
        var filter = Builders<ActivityLog>.Filter.Eq(x => x.AgentId, agentId) &
                     Builders<ActivityLog>.Filter.Gte(x => x.Timestamp, DateTime.UtcNow.AddHours(-24));

        var logs = await coll.Find(filter).ToListAsync();

        if (logs.Count == 0) return Ok(new { Score = 0, TotalTime = 0 });

        double totalSec = 0;
        double productiveSec = 0;
        double unproductiveSec = 0;
        double neutralSec = 0;

        // Group by App for Top Apps List
        var appStats = new Dictionary<string, (double Duration, string Category)>();

        foreach (var log in logs)
        {
            totalSec += log.DurationSeconds;
            
            var name = string.IsNullOrEmpty(log.ProcessName) ? "Unknown" : log.ProcessName;
            var cat = Classify(name, log.WindowTitle);

            if (cat == "Productive") productiveSec += log.DurationSeconds;
            else if (cat == "Unproductive") unproductiveSec += log.DurationSeconds;
            else neutralSec += log.DurationSeconds;

            if (!appStats.ContainsKey(name)) appStats[name] = (0, cat);
            appStats[name] = (appStats[name].Duration + log.DurationSeconds, cat);
        }

        // Calculate Score (Ignore Neutral? Or Count Neutral as 50%? Let's imply Score = Productive / (Prod+Unprod))
        // Or simple: Score = Productive / Total Active
        // Let's go with Productive / Total * 100 for now.
        double score = totalSec > 0 ? (productiveSec / totalSec) * 100 : 0;

        var topApps = appStats.Select(x => new 
        { 
            Name = x.Key, 
            Duration = x.Value.Duration, 
            Category = x.Value.Category 
        })
        .OrderByDescending(x => x.Duration)
        .Take(10)
        .ToList();

        return Ok(new 
        {
            Score = Math.Round(score, 1),
            TotalSeconds = Math.Round(totalSec, 1),
            Breakdown = new 
            {
                Productive = Math.Round(productiveSec, 1),
                Unproductive = Math.Round(unproductiveSec, 1),
                Neutral = Math.Round(neutralSec, 1)
            },
            TopApps = topApps
        });
    }


    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> GetMyStats([FromServices] AppDbContext sqlDb)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Unauthorized();

        // 1. Resolve User -> Agent(s)
        // Heuristic: If User is TenantAdmin, show Aggregated? 
        // If User is simple Employee (Analyst role?), show their machine.
        // For now, let's assume AgentId matches Username OR we find an Agent assigned to this user.
        // For simplicity in this mock: AgentId = Username.
        
        return await GetSummary(username); 
    }
}
