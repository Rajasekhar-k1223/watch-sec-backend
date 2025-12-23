using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MongoDB.Driver.IMongoClient _mongo;

    public ReportsController(AppDbContext db, MongoDB.Driver.IMongoClient mongo)
    {
        _db = db;
        _mongo = mongo;
    }

    [HttpGet]
    public IActionResult GetReports()
    {
        // For now, listing "Past Reports" is still mocked or we could fetch file metadata if we stored them.
        // Let's keep the mock list for UI demo purposes, but the GENERATE action will be real.
        var reports = new[]
        {
            new { Id = 1, Name = "Weekly Activity Summary", Type = "Activity", Date = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"), Size = "1.2 MB" },
            new { Id = 2, Name = "DLP Violation Report", Type = "Security", Date = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"), Size = "850 KB" }
        };
        return Ok(reports);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateReport([FromBody] GenerateReportRequest request)
    {
        var csv = new System.Text.StringBuilder();
        var filename = $"Report_{request.Type}_{DateTime.Now:yyyyMMdd}.csv";
        var cutoff = request.Range == "24h" ? DateTime.UtcNow.AddHours(-24) : 
                     request.Range == "7d" ? DateTime.UtcNow.AddDays(-7) : 
                     DateTime.UtcNow.AddDays(-30);

        if (request.Type == "audit")
        {
            csv.AppendLine("Time,Actor,Action,Target,Details");
            var logs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                _db.AuditLogs.Where(x => x.Timestamp >= cutoff).OrderByDescending(x => x.Timestamp)
            );
            
            foreach (var log in logs)
            {
                csv.AppendLine($"{log.Timestamp},{log.Actor},{log.Action},{log.Target},\"{log.Details.Replace("\"", "\"\"")}\"");
            }
        }
        else if (request.Type == "activity" || request.Type == "productivity")
        {
            csv.AppendLine("Time,AgentId,Process,Window,DurationSec");
            var db = _mongo.GetDatabase("WatchSecDB");
            var coll = db.GetCollection<ActivityLog>("ActivityLogs");
            var filter = MongoDB.Driver.Builders<ActivityLog>.Filter.Gte(x => x.Timestamp, cutoff);
            
            var logs = await MongoDB.Driver.IAsyncCursorSourceExtensions.ToListAsync(coll.Find(filter));

            foreach (var log in logs)
            {
                csv.AppendLine($"{log.Timestamp},{log.AgentId},{log.ProcessName},\"{log.WindowTitle.Replace("\"", "\"\"")}\",{log.DurationSeconds}");
            }
        }
        else
        {
            csv.AppendLine("Error,Report Type Not Supported Yet");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", filename);
    }

    public record GenerateReportRequest(string Type, string Range);
}
