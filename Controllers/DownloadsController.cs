using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/downloads")]
[Authorize] // Requires Login
public class DownloadsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;

    public DownloadsController(IWebHostEnvironment env, AppDbContext db)
    {
        _env = env;
        _db = db;
    }

    [HttpGet("agent")]
    [Authorize(Roles = "TenantAdmin")] // Strict RBAC: Only Tenant Admins
    public async Task<IActionResult> DownloadAgent([FromQuery] string os = "windows")
    {
        // 1. Identify Tenant
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;
        if (string.IsNullOrEmpty(tenantIdClaim)) return BadRequest("User has no TenantId");
        
        int tenantId = int.Parse(tenantIdClaim);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return Unauthorized("Tenant not found");

        // 2. Locate Template based on OS
        string templateFolder = os.ToLower() switch 
        {
            "linux" => "linux-x64",
            "mac" => "osx-x64",
            _ => "win-x64"
        };

        var templatePath = Path.Combine(_env.ContentRootPath, "Storage", "AgentTemplate", templateFolder);
        if (!Directory.Exists(templatePath)) return NotFound($"Agent Template for {os} not found on server.");

        // 3. Prepare Temp Directory
        var tempId = Guid.NewGuid().ToString();
        var tempPath = Path.Combine(Path.GetTempPath(), "WatchSec_Gen", tempId);
        var agentFolder = Path.Combine(tempPath, "watch-sec-agent");
        
        try 
        {
            Directory.CreateDirectory(agentFolder);

            // 4. Copy Files
            foreach (var file in Directory.GetFiles(templatePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(templatePath, file);
                var destPath = Path.Combine(agentFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                System.IO.File.Copy(file, destPath, true);
            }

            // 5. Inject Configuration (appsettings.json)
            var configPath = Path.Combine(agentFolder, "appsettings.json");
            if (System.IO.File.Exists(configPath))
            {
                var json = await System.IO.File.ReadAllTextAsync(configPath);
                var jsonObj = JsonNode.Parse(json);

                if (jsonObj != null)
                {
                    // Inject Tenant Key
                    jsonObj["TenantApiKey"] = tenant.ApiKey;
                    jsonObj["BackendUrl"] = $"{Request.Scheme}://{Request.Host}";

                    await System.IO.File.WriteAllTextAsync(configPath, jsonObj.ToString());
                }
            }

            // 5. Create Zip (Binaries Only)
            var zipPath = Path.Combine(tempPath, "payload.zip");
            ZipFile.CreateFromDirectory(agentFolder, zipPath);

            // 6. Serve File (SFX for Windows, Script for Linux/Mac)
            byte[] fileBytes;
            string fileName;
            string contentType;

            if (os.ToLower() == "windows")
            {
                // ... Windows SFX Logic (Existing) ...
                var stubPath = Path.Combine(_env.ContentRootPath, "Storage", "AgentTemplate", "Stub.exe");
                if (!System.IO.File.Exists(stubPath)) return StatusCode(500, "Installer Stub not found.");

                var stubBytes = await System.IO.File.ReadAllBytesAsync(stubPath);
                var zipBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
                var offsetBytes = BitConverter.GetBytes((long)stubBytes.Length);

                fileBytes = new byte[stubBytes.Length + zipBytes.Length + offsetBytes.Length];
                Buffer.BlockCopy(stubBytes, 0, fileBytes, 0, stubBytes.Length);
                Buffer.BlockCopy(zipBytes, 0, fileBytes, stubBytes.Length, zipBytes.Length);
                Buffer.BlockCopy(offsetBytes, 0, fileBytes, stubBytes.Length + zipBytes.Length, offsetBytes.Length);

                fileName = "watch-sec-setup.exe";
                contentType = "application/vnd.microsoft.portable-executable";
            }
            else
            {
                // Linux/Mac: Generate Self-Extracting Script
                var scriptPath = Path.Combine(_env.ContentRootPath, "Storage", "AgentTemplate", "install_template.sh");
                if (!System.IO.File.Exists(scriptPath)) return StatusCode(500, "Installer Template not found.");

                var scriptContent = await System.IO.File.ReadAllTextAsync(scriptPath);
                // Inject Tenant Key
                scriptContent = scriptContent.Replace("{{TENANT_KEY}}", tenant.ApiKey);
                
                // Convert Script to Bytes (ensure Unix line endings if possible, but Bash handles mixed usually)
                // Better to force LF
                scriptContent = scriptContent.Replace("\r\n", "\n");
                var scriptBytes = System.Text.Encoding.UTF8.GetBytes(scriptContent);
                
                var zipBytes = await System.IO.File.ReadAllBytesAsync(zipPath);

                // Merge: Script + Zip
                fileBytes = new byte[scriptBytes.Length + zipBytes.Length];
                Buffer.BlockCopy(scriptBytes, 0, fileBytes, 0, scriptBytes.Length);
                Buffer.BlockCopy(zipBytes, 0, fileBytes, scriptBytes.Length, zipBytes.Length);

                fileName = "watch-sec-install.sh";
                contentType = "application/x-sh";
            }

            // Cleanup
            try { Directory.Delete(tempPath, true); } catch { }

            return File(fileBytes, contentType, fileName);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
        }
    }
}
