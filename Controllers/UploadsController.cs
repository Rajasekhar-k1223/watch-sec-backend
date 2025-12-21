using Microsoft.AspNetCore.Mvc;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/uploads")]
public class UploadsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;

    public UploadsController(IWebHostEnvironment env, AppDbContext db)
    {
        _env = env;
        _db = db;
    }

    [HttpPost("audio")]
    public async Task<IActionResult> UploadAudio([FromForm] IFormFile file, [FromForm] string agentId, [FromForm] string tenantApiKey)
    {
        if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

        // Validate Tenant
        var tenant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_db.Tenants, t => t.ApiKey == tenantApiKey);
        if (tenant == null) return Unauthorized();

        try 
        {
            var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
            var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Audio", agentId, dateFolder);
            Directory.CreateDirectory(storagePath);

            var filename = $"{DateTime.UtcNow:HHmmss}_{file.FileName}";
            var filePath = Path.Combine(storagePath, filename);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { Path = filePath });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("shadow")]
    public async Task<IActionResult> UploadShadow([FromForm] IFormFile file, [FromForm] string agentId, [FromForm] string tenantApiKey)
    {
        if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

        // Validate Tenant
        var tenant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_db.Tenants, t => t.ApiKey == tenantApiKey);
        if (tenant == null) return Unauthorized();

        try 
        {
            var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
            var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Shadows", agentId, dateFolder);
            Directory.CreateDirectory(storagePath);

            var filename = $"{DateTime.UtcNow:HHmmss}_{file.FileName}";
            var filePath = Path.Combine(storagePath, filename);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { Path = filePath });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
