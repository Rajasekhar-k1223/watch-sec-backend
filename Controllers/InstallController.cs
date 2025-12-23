using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/install")]
public class InstallController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly Services.IEmailService _email;
    
    // Simple in-memory cache for OTPs (In production, use Redis or DB)
    private static readonly Dictionary<string, (string Code, DateTime Expiry)> _otpCache = new();

    public InstallController(AppDbContext db, IConfiguration config, Services.IEmailService email)
    {
        _db = db;
        _config = config;
        _email = email;
    }

    public class ValidateRequest
    {
        public string MachineName { get; set; } = "";
        public string Domain { get; set; } = "";
        public string IP { get; set; } = "";
        public string? TenantApiKey { get; set; } // Added for Notification Context
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateDevice([FromBody] ValidateRequest req)
    {
        var trustedDomains = _config.GetSection("SecuritySettings:TrustedDomains").Get<string[]>() ?? Array.Empty<string>();
        var trustedIPs = _config.GetSection("SecuritySettings:TrustedIPs").Get<string[]>() ?? Array.Empty<string>();

        bool isDomainTrusted = trustedDomains.Any(d => req.Domain.ToUpper().Contains(d.ToUpper()));
        bool isIpTrusted = trustedIPs.Contains(req.IP);

        Console.WriteLine($"[Install] Validating Device: {req.MachineName} | Domain: {req.Domain} | IP: {req.IP}");

        if (isDomainTrusted || isIpTrusted)
        {
            return Ok(new { Status = "Trusted", Message = "Device Authorized via Policy." });
        }

        // --- UNTRUSTED DEVICE LOGIC ---

        // 1. Notify Tenant Admin (if Key provided)
        if (!string.IsNullOrEmpty(req.TenantApiKey))
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.ApiKey == req.TenantApiKey);
            if (tenant != null)
            {
                var admin = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Role == "TenantAdmin");
                if (admin != null)
                {
                    // Construct Email
                     var emailTarget = admin.Username.Contains("@") ? admin.Username : $"{admin.Username}@example.com";
                     
                     // Fire and forget email (don't block response)
                     _ = _email.SendEmailAsync(
                         emailTarget,
                         "Installation Blocked: Authorization Required",
                         $@"
                            <h2>New Device Installation Attempt</h2>
                            <p>A device outside your trusted network attempted to install the agent.</p>
                            <ul>
                                <li><b>Machine:</b> {req.MachineName}</li>
                                <li><b>Domain:</b> {req.Domain}</li>
                                <li><b>IP Address:</b> {req.IP}</li>
                                <li><b>Timestamp:</b> {DateTime.UtcNow}</li>
                            </ul>
                            <p><b>Action Required:</b> If this is legitimate, please generate an OTP from your Dashboard and provide it to the user.</p>
                         ");
                }
            }
        }

        return Ok(new { Status = "Untrusted", Message = "Remote Device Detected. Validation PIN Required." });
    }

    [HttpPost("token")]
    // [Authorize(Roles = "TenantAdmin")] // Uncomment in prod
    public IActionResult GenerateToken()
    {
        // Generate 6-digit PIN
        var pin = new Random().Next(100000, 999999).ToString();
        
        // Store in Cache (Valid for 30 mins)
        // In a real multi-tenant app, key by TenantId
        _otpCache["GLOBAL"] = (pin, DateTime.UtcNow.AddMinutes(30));

        Console.WriteLine($"[Install] Generated OTP: {pin}");
        return Ok(new { Token = pin, ExpiresIn = "30 Minutes" });
    }

    public class VerifyTokenRequest
    {
        public string Token { get; set; } = "";
    }

    [HttpPost("verify-token")]
    public IActionResult VerifyToken([FromBody] VerifyTokenRequest req)
    {
        if (_otpCache.TryGetValue("GLOBAL", out var entry))
        {
            if (DateTime.UtcNow > entry.Expiry)
            {
                return BadRequest("Token Expired");
            }

            if (entry.Code == req.Token)
            {
                return Ok(new { Status = "Authorized" });
            }
        }

        return Unauthorized("Invalid Token");
    }
}
