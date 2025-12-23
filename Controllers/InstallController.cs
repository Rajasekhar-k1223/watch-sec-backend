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
    // Simple in-memory cache for OTPs (In production, use Redis or DB)
    private static readonly Dictionary<string, (string Code, DateTime Expiry)> _otpCache = new();

    public InstallController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public class ValidateRequest
    {
        public string MachineName { get; set; } = "";
        public string Domain { get; set; } = "";
        public string IP { get; set; } = "";
    }

    [HttpPost("validate")]
    public IActionResult ValidateDevice([FromBody] ValidateRequest req)
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
