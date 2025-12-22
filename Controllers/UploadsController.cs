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
    public async Task<IActionResult> UploadAudio([FromForm] FileUploadDto dto)
    {
        if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

        // Validate Tenant
        var tenant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_db.Tenants, t => t.ApiKey == dto.TenantApiKey);
        if (tenant == null) return Unauthorized();

        try 
        {
            var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
            var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Audio", dto.AgentId, dateFolder);
            Directory.CreateDirectory(storagePath);

            var filename = $"{DateTime.UtcNow:HHmmss}_{dto.File.FileName}";
            var filePath = Path.Combine(storagePath, filename);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            return Ok(new { Path = filePath });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("shadow")]
    public async Task<IActionResult> UploadShadow([FromForm] FileUploadDto dto)
    {
        if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

        // Validate Tenant
        var tenant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_db.Tenants, t => t.ApiKey == dto.TenantApiKey);
        if (tenant == null) return Unauthorized();

        try 
        {
            var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
            var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Shadows", dto.AgentId, dateFolder);
            Directory.CreateDirectory(storagePath);

            var filename = $"{DateTime.UtcNow:HHmmss}_{dto.File.FileName}";
            var filePath = Path.Combine(storagePath, filename);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            return Ok(new { Path = filePath });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}

public class FileUploadDto
{
    public IFormFile File { get; set; }
    public string AgentId { get; set; }
    public string TenantApiKey { get; set; }
}
