using Microsoft.EntityFrameworkCore;

namespace watch_sec_backend.Services;

public class DataSeeder
{
    private readonly AppDbContext _db;

    public DataSeeder(AppDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync()
    {
        // Only seed if we don't have the "Global Finance Ltd" tenant which implies our rich data set
        // (AppDbContext seeds "CyberCorp Inc" and "RetailChain Ltd" via HasData, but let's check for our specific demo one)
        if (await _db.Tenants.AnyAsync(t => t.Name == "Global Finance Ltd"))
        {
            return; // Already seeded
        }

        Console.WriteLine("[Seeder] Starting Rich Dummy Data Generation...");
        var rnd = new Random();

        // 1. Tenants
        var tenants = new List<Tenant>
        {
            new Tenant { Name = "Global Finance Ltd", ApiKey = "demo-finance-key", Plan = "Enterprise", AgentLimit = 500, NextBillingDate = DateTime.UtcNow.AddDays(20) },
            new Tenant { Name = "StartUp Inc", ApiKey = "demo-startup-key", Plan = "Pro", AgentLimit = 50, NextBillingDate = DateTime.UtcNow.AddDays(5) }
        };

        foreach (var t in tenants)
        {
            // Double check by name to start clean
            if (!await _db.Tenants.AnyAsync(x => x.Name == t.Name))
            {
                _db.Tenants.Add(t);
                await _db.SaveChangesAsync(); // Save to get ID

                // Create Admin
                _db.Users.Add(new User 
                { 
                    Username = $"admin_{t.Name.Split(' ')[0].ToLower()}", 
                    PasswordHash = "demo123", 
                    Role = "TenantAdmin", 
                    TenantId = t.Id 
                });
            }
        }
        await _db.SaveChangesAsync();

        // 2. Agents
        var targetTenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Name == "Global Finance Ltd");
        if (targetTenant != null)
        {
            var apps = new[] { "Chrome v80 (Vulnerable)", "Firefox", "Slack", "Zoom", "Tor Browser (Risk)" };
            var gateways = new[] { "192.168.1.1", "10.0.0.1", "172.16.0.1" };

            // Create 20 Dummy Agents
            for (int i = 1; i <= 20; i++)
            {
                var agentId = $"FIN-WKS-{i:000}";
                if (!await _db.Agents.AnyAsync(a => a.AgentId == agentId))
                {
                    var gw = gateways[rnd.Next(gateways.Length)];
                    var ip = gw.Substring(0, gw.LastIndexOf('.')) + "." + rnd.Next(2, 254);
                    
                    var agent = new Agent
                    {
                        AgentId = agentId,
                        TenantId = targetTenant.Id,
                        LastSeen = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 120)), // Recent
                        LocalIp = ip,
                        Gateway = gw,
                        Latitude = 40.7128 + (rnd.NextDouble() * 0.1),
                        Longitude = -74.0060 + (rnd.NextDouble() * 0.1),
                        Country = "United States",
                        InstalledSoftwareJson = System.Text.Json.JsonSerializer.Serialize(new[] 
                        { 
                            new { Name = "Google Chrome", Version = "80.0.1", Vulnerable = true },
                            new { Name = "Microsoft Word", Version = "16.0", Vulnerable = false }
                        })
                    };
                    _db.Agents.Add(agent);

                    // Add Metrics History
                    for(int j=0; j<10; j++)
                    {
                         _db.AgentReports.Add(new AgentReportEntity
                         {
                             AgentId = agentId,
                             TenantId = targetTenant.Id,
                             Status = "Online",
                             CpuUsage = rnd.Next(10, 90),
                             MemoryUsage = rnd.Next(200, 4000),
                             Timestamp = DateTime.UtcNow.AddMinutes(-j * 10)
                         });
                    }
                }
            }
            
            // 3. Audit Logs (Anomalies)
            _db.AuditLogs.Add(new AuditLog { TenantId = targetTenant.Id, Actor = "System", Action = "Anomaly Detected", Target = "FIN-WKS-005", Details = "Detected 'Tor Browser' running.", Timestamp = DateTime.UtcNow.AddMinutes(-15) });
            _db.AuditLogs.Add(new AuditLog { TenantId = targetTenant.Id, Actor = "System", Action = "USB Blocked", Target = "FIN-WKS-012", Details = "Blocked USB device 'StoreJet'.", Timestamp = DateTime.UtcNow.AddHours(-2) });

            await _db.SaveChangesAsync();
            Console.WriteLine("[Seeder] Rich Dummy Data Generation Complete.");
        }
    }
}
