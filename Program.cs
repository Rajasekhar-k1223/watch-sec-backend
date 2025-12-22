using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using watch_sec_backend;
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
builder.Services.AddHostedService<IcapService>();
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

    // AUTO-MIGRATION: Fix missing columns
    try { db.Database.ExecuteSqlRaw("ALTER TABLE AgentReports ADD COLUMN TenantId INT NOT NULL DEFAULT 1;"); } catch {}
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

// API: Save Report (PostgreSQL)
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

    // 2. ALWAYS INSERT (History Mode)
    // We no longer update existing records. We keep a history of every heartbeat.
    db.AgentReports.Add(new AgentReportEntity 
    { 
        AgentId = dto.AgentId,
        TenantId = tenant.Id,
        Status = dto.Status,
        CpuUsage = dto.CpuUsage,
        MemoryUsage = dto.MemoryUsage,
        Timestamp = dto.Timestamp
    });
    
    Console.WriteLine($"[API] Inserted History Record for {dto.AgentId}");
    
    await db.SaveChangesAsync();

    // 3. BROADCAST TO FRONTEND (Real-Time Update)
    // This allows the "View Logs" table to update instantly without reload.
    // We reuse the standard "ReceiveEvent" message so the frontend doesn't need new logic.
    var details = $"Status: {dto.Status} | CPU: {dto.CpuUsage:F1}% | MEM: {dto.MemoryUsage:F1}MB";
    await hub.Clients.All.SendAsync("ReceiveEvent", dto.AgentId, "System Heartbeat", details, dto.Timestamp);

    return Results.Ok(new { TenantId = tenant.Id });
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

public record AgentReportDto(string AgentId, string Status, double CpuUsage, double MemoryUsage, DateTime Timestamp, string TenantApiKey);
