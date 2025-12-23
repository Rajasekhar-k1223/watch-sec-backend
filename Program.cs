using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using watch_sec_backend;
using watch_sec_backend.Services;
using Microsoft.AspNetCore.SignalR; // Required for SendAsync extension

var builder = WebApplication.CreateBuilder(args);

// 1. MySQL (Railway)
// 1. MySQL (Railway)
// 1. Database Configuration
var useLocal = builder.Configuration.GetValue<bool>("DatabaseConfig:UseLocalDB");
var syncToLocal = builder.Configuration.GetValue<bool>("DatabaseConfig:SyncToLocalOnStartup");

Console.WriteLine($"[Init] Database Mode: {(useLocal ? "LOCAL" : "REMOTE")}");

var mysqlConn = useLocal 
    ? builder.Configuration.GetConnectionString("LocalMySQL") 
    : builder.Configuration.GetConnectionString("RemoteMySQL");

var mongoConn = useLocal 
    ? builder.Configuration.GetConnectionString("LocalMongo") 
    : builder.Configuration.GetConnectionString("RemoteMongo");

if (string.IsNullOrEmpty(mysqlConn) || string.IsNullOrEmpty(mongoConn))
    throw new Exception("CRITICAL: Missing ConnectionStrings in appsettings.json");

// 1. MySQL Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
{
    try 
    {
        options.UseMySql(mysqlConn, Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(mysqlConn),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null));
    }
    catch
    {
        // Fallback if AutoDetect fails due to timeout (Use a safe default version like 8.0.21)
        options.UseMySql(mysqlConn, new MySqlServerVersion(new Version(8, 0, 21)),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure());
    }
});

// 2. MongoDB
builder.Services.AddSingleton<IMongoClient>(sp => new MongoClient(mongoConn));

builder.Services.AddOpenApi();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Secret"] ?? "super-secret-key-that-should-be-in-env-vars-and-very-long";
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.ASCII.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHostedService<MailListenerService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddHostedService<IcapService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    options.EnableDetailedErrors = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder
        .SetIsOriginAllowed(_ => true) // Allow any origin
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()); 
});

// Enable Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure DB Created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    // Ensure Active DB Created
    try { db.Database.EnsureCreated(); } catch (Exception ex) { Console.WriteLine($"[Warn] DB Init: {ex.Message}"); }

    // --- SYNC LOGIC (REMOTE -> LOCAL) ---
    if (syncToLocal && !useLocal)
    {
        Console.WriteLine("[Sync] Starting Data Synchronization from REMOTE to LOCAL...");
        try 
        {
            // 1. Fetch Remote Data (we are currently connected to Remote)
            var remoteTenants = db.Tenants.ToList();
            var remoteUsers = db.Users.ToList();
            Console.WriteLine($"[Sync] Fetched {remoteTenants.Count} Tenants, {remoteUsers.Count} Users from Remote.");

            // 2. Connect to Local DB manually
            var localStr = builder.Configuration.GetConnectionString("LocalMySQL");
            if (!string.IsNullOrEmpty(localStr))
            {
                var localOpts = new DbContextOptionsBuilder<AppDbContext>()
                    .UseMySql(localStr, new MySqlServerVersion(new Version(8, 0, 21)))
                    .Options;
                
                using var localDb = new AppDbContext(localOpts);
                localDb.Database.EnsureCreated();
                
                // 3. Upsert Tenants
                foreach (var t in remoteTenants)
                {
                    if (!localDb.Tenants.Any(x => x.Id == t.Id))
                    {
                        localDb.Tenants.Add(t);
                    }
                }
                
                // 4. Upsert Users
                foreach (var u in remoteUsers)
                {
                    if (!localDb.Users.Any(x => x.Id == u.Id))
                    {
                        localDb.Users.Add(u);
                    }
                }
                
                localDb.SaveChanges();
                Console.WriteLine("[Sync] SUCCESS: Synchronized data to Local Database.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sync] FAILED: {ex.Message}");
        }
    }
    // --- END SYNC LOGIC ---

    // AUTO-MIGRATION: Fix missing columns safely
    try { 
        // 1. Ensure AgentReports has TenantId
        try { 
            db.Database.ExecuteSqlRaw("ALTER TABLE AgentReports ADD COLUMN TenantId INT NOT NULL DEFAULT 1;"); 
        } catch { /* Column likely exists */ }
        
        // 2. Tenants - New Billing Columns
        try {
            db.Database.ExecuteSqlRaw("ALTER TABLE Tenants ADD COLUMN AgentLimit INT NOT NULL DEFAULT 5;");
            db.Database.ExecuteSqlRaw("ALTER TABLE Tenants ADD COLUMN NextBillingDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;");
        } catch { /* Column likely exists */ }

        // 3. Ensure Agents Table exists (PRIORITY: Create before Alter)
        try {
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS Agents (
                    Id INT AUTO_INCREMENT PRIMARY KEY,
                    AgentId VARCHAR(255) NOT NULL,
                    TenantId INT NOT NULL,
                    ScreenshotsEnabled TINYINT(1) NOT NULL DEFAULT 1,
                    LastSeen DATETIME NOT NULL,
                    Latitude DOUBLE NOT NULL DEFAULT 0,
                    Longitude DOUBLE NOT NULL DEFAULT 0,
                    Country TEXT,
                    InstalledSoftwareJson TEXT,
                    LocalIp VARCHAR(50),
                    Gateway VARCHAR(50),
                    INDEX (AgentId)
                );
            ");
        } catch (Exception ex) { Console.WriteLine($"[Migrate] Agents Table Create Failed: {ex.Message}"); }

        // 4. Agents - New Columns (If table existed but old schema)
        try {
            db.Database.ExecuteSqlRaw("ALTER TABLE Agents ADD COLUMN Latitude DOUBLE NOT NULL DEFAULT 0;");
            db.Database.ExecuteSqlRaw("ALTER TABLE Agents ADD COLUMN Longitude DOUBLE NOT NULL DEFAULT 0;");
            db.Database.ExecuteSqlRaw("ALTER TABLE Agents ADD COLUMN Country TEXT;");
        } catch { /* Column likely exists */ }

        try {
            db.Database.ExecuteSqlRaw("ALTER TABLE Agents ADD COLUMN InstalledSoftwareJson TEXT;");
            db.Database.ExecuteSqlRaw("ALTER TABLE Agents ADD COLUMN LocalIp VARCHAR(50) DEFAULT '0.0.0.0';");
            db.Database.ExecuteSqlRaw("ALTER TABLE Agents ADD COLUMN Gateway VARCHAR(50) DEFAULT 'Unknown';");
        } catch { /* Column likely exists */ }


        // 5. Policies - New Blocking Columns
        try {
            db.Database.ExecuteSqlRaw("ALTER TABLE Policies ADD COLUMN BlockedAppsJson TEXT;");
            db.Database.ExecuteSqlRaw("ALTER TABLE Policies ADD COLUMN BlockedWebsitesJson TEXT;");
        } catch { /* Column likely exists */ }

        // 6. Ensure AuditLogs Table exists
        try {
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS AuditLogs (
                    Id INT AUTO_INCREMENT PRIMARY KEY,
                    TenantId INT NOT NULL,
                    Actor VARCHAR(255) NOT NULL,
                    Action VARCHAR(255) NOT NULL,
                    Target VARCHAR(255) NOT NULL,
                    Details TEXT,
                    Timestamp DATETIME NOT NULL,
                    INDEX (TenantId),
                    INDEX (Timestamp)
                );
            ");
        } catch { }

    } catch (Exception ex) { Console.WriteLine($"[Migrate] Fatal Error: {ex.Message}"); }

    // --- AUTO SEEDING (NEW) ---
    try 
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync();
    } 
    catch(Exception ex) 
    { 
        Console.WriteLine($"[Seeder] Error: {ex.Message}"); 
    }
}

// Enable Swagger UI (Always, for public testing)
app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.MapControllers(); // Map AuthController endpoints

// API: Save Report (PostgreSQL) - Now Multi-Tenant Aware
app.MapPost("/api/report", async ([FromBody] AgentReportDto dto, AppDbContext db, Microsoft.AspNetCore.SignalR.IHubContext<StreamHub> hub) =>
{
    Console.WriteLine($"[API] Received Report from {dto.AgentId}");
    
    // 1. Authenticate Tenant
    var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.ApiKey == dto.TenantApiKey);
    if (tenant == null) 
    {
        Console.WriteLine($"[API] Unauthorized Tenant Key: {dto.TenantApiKey}");
        return Results.Unauthorized();
    }

    // 2. INSERT HISTORY (Reports)
    db.AgentReports.Add(new AgentReportEntity 
    { 
        AgentId = dto.AgentId,
        TenantId = tenant.Id,
        Status = dto.Status,
        CpuUsage = dto.CpuUsage,
        MemoryUsage = dto.MemoryUsage,
        Timestamp = dto.Timestamp
    });

    // 2b. SYNC AGENT Config (Persistent Entity)
    var agent = await db.Agents.FirstOrDefaultAsync(a => a.AgentId == dto.AgentId);
    if (agent == null)
    {
        // New Agent - Mock Geo
        var lat = (new Random().NextDouble() * 160) - 80; // Avoid poles
        var lon = (new Random().NextDouble() * 360) - 180;

        agent = new Agent 
        { 
            AgentId = dto.AgentId, 
            TenantId = tenant.Id, 
            ScreenshotsEnabled = true, // Default ON
            LastSeen = DateTime.UtcNow,
            Latitude = lat,
            Longitude = lon,
            Country = "Unknown", // TODO: Real lookup
            InstalledSoftwareJson = dto.InstalledSoftwareJson ?? "[]",
            LocalIp = dto.LocalIp ?? "0.0.0.0",
            Gateway = dto.Gateway ?? "Unknown"
        };
        db.Agents.Add(agent);
    }
    else
    {
        // Update Last Seen
        agent.LastSeen = DateTime.UtcNow;
        // Ensure TenantId matches (if moved?)
        agent.TenantId = tenant.Id;
        
        // Update Software List if provided
        if (!string.IsNullOrEmpty(dto.InstalledSoftwareJson))
        {
            agent.InstalledSoftwareJson = dto.InstalledSoftwareJson;
        }

        // Update Network Info if provided
        if (!string.IsNullOrEmpty(dto.LocalIp)) agent.LocalIp = dto.LocalIp;
        if (!string.IsNullOrEmpty(dto.Gateway)) agent.Gateway = dto.Gateway;
    }
    
    await db.SaveChangesAsync();

    // 3. BROADCAST
    var details = $"Status: {dto.Status} | CPU: {dto.CpuUsage:F1}% | MEM: {dto.MemoryUsage:F1}MB";
    await hub.Clients.All.SendAsync("ReceiveEvent", dto.AgentId, "System Heartbeat", details, dto.Timestamp);

    // Return Config to Agent
    return Results.Ok(new { TenantId = tenant.Id, ScreenshotsEnabled = agent.ScreenshotsEnabled });
});



// API: Get Status (Dashboard) - Supports Filtering
app.MapGet("/api/status", async (int? tenantId, AppDbContext db) =>
{
    // Filter by tenant if provided
    var query = db.AgentReports.AsQueryable();
    if (tenantId.HasValue)
    {
        query = query.Where(r => r.TenantId == tenantId.Value);
    }
    
    // Fetch all relevant records then GroupBy in memory to get LATEST
    // (Doing complex GroupBy in EF Core with MySQL can be tricky, this is safe for <100k records)
    var allReports = await query.ToListAsync();
    
    var latestReports = allReports
        .GroupBy(r => r.AgentId)
        .Select(g => g.OrderByDescending(r => r.Timestamp).First())
        .ToList();

    return latestReports;
});

// API: Get Agent Metrics History (MySQL)
app.MapGet("/api/history/{agentId}", async (string agentId, AppDbContext db) =>
{
    var history = await db.AgentReports
        .Where(r => r.AgentId == agentId)
        .OrderByDescending(r => r.Timestamp)
        .Take(50)
        .ToListAsync();
    return Results.Ok(history);
});

// API: Get Events (MongoDB) - Moved to EventsController.cs to avoid route conflict and support simulation
// app.MapGet("/api/events/{agentId}"... removed

// API: Dashboard Analytics (Aggregated) - Moved to DashboardController.cs

app.MapHub<StreamHub>("/streamHub"); 

app.Run();

public record AgentReportDto(string AgentId, string Status, double CpuUsage, double MemoryUsage, DateTime Timestamp, string TenantApiKey, string? InstalledSoftwareJson = null, string? LocalIp = null, string? Gateway = null);
