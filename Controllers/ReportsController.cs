using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetReports()
    {
        // In a real scenario, this would scan the "Reports" directory or DB
        // For now, we return the structured data to replace the frontend mock
        var reports = new[]
        {
            new { Id = 1, Name = "Weekly Activity Summary", Type = "Activity", Date = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"), Size = "1.2 MB" },
            new { Id = 2, Name = "DLP Violation Report", Type = "Security", Date = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"), Size = "850 KB" },
            new { Id = 3, Name = "User Productivity Analysis", Type = "Productivity", Date = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd"), Size = "2.4 MB" },
            new { Id = 4, Name = "Blocked Websites Log", Type = "Web", Date = DateTime.UtcNow.AddDays(-4).ToString("yyyy-MM-dd"), Size = "450 KB" },
            new { Id = 5, Name = "Incident Response Audit", Type = "Audit", Date = DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd"), Size = "3.1 MB" }
        };

        return Ok(reports);
    }

    [HttpPost("generate")]
    public IActionResult GenerateReport([FromBody] GenerateReportRequest request)
    {
        // Mock Generation
        return Ok(new { message = $"Report '{request.Type}' for range '{request.Range}' generated successfully." });
    }

    public record GenerateReportRequest(string Type, string Range);
}
