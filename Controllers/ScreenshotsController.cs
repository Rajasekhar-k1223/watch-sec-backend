using Microsoft.AspNetCore.Mvc;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/screenshots")]
public class ScreenshotsController : ControllerBase
{
    private readonly string _storageRoot;

    public ScreenshotsController(IWebHostEnvironment env, IConfiguration config)
    {
        var basePath = config["StoragePath"] ?? "Storage";
        if (!Path.IsPathRooted(basePath)) basePath = Path.Combine(env.ContentRootPath, basePath);
        
        _storageRoot = Path.Combine(basePath, "Screenshots");
    }

    [HttpGet("list/{agentId}")]
    public IActionResult ListScreenshots(string agentId)
    {
        var agentPath = Path.Combine(_storageRoot, agentId);
        if (!Directory.Exists(agentPath))
        {
            return Ok(new List<ScreenshotDto>());
        }

        var result = new List<ScreenshotDto>();

        // Iterate Date Folders
        foreach (var dateDir in Directory.GetDirectories(agentPath))
        {
            var dateStr = Path.GetFileName(dateDir); // yyyyMMdd
            
            // Iterate Files
            foreach (var file in Directory.GetFiles(dateDir, "*.webp"))
            {
                var fileName = Path.GetFileName(file);
                var isAlert = fileName.Contains("_ALERT");
                
                // Parse Time (HHmmss)
                var timePart = fileName.Split('_')[0].Replace(".webp", "");
                var dateTimeStr = $"{dateStr} {timePart}";
                
                if (DateTime.TryParseExact(dateTimeStr, "yyyyMMdd HHmmss", null, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    result.Add(new ScreenshotDto
                    {
                        Filename = fileName,
                        Date = dateStr,
                        Timestamp = dt,
                        IsAlert = isAlert,
                        Url = $"/api/screenshots/view/{agentId}/{dateStr}/{fileName}"
                    });
                }
            }
        }

        return Ok(result.OrderByDescending(x => x.Timestamp));
    }

    [HttpGet("view/{agentId}/{date}/{filename}")]
    public IActionResult GetImage(string agentId, string date, string filename)
    {
        var path = Path.Combine(_storageRoot, agentId, date, filename);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var bytes = System.IO.File.ReadAllBytes(path);
        return File(bytes, "image/webp");
    }
}

public class ScreenshotDto
{
    public string Filename { get; set; } = "";
    public string Date { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool IsAlert { get; set; }
    public string Url { get; set; } = "";
}
