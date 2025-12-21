using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using watch_sec_backend;
using Microsoft.AspNetCore.SignalR; // Required for SendAsync extension

var builder = WebApplication.CreateBuilder(args);

// 1. MySQL (Railway)
// 1. MySQL (Railway)
var mysqlConn = "Server=trolley.proxy.rlwy.net;Port=34465;Database=railway;Uid=root;Pwd=nBhlxqOuzWwFQkraCcNrVIoDVFqFbWEA;Connect Timeout=60;";
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

// 2. MongoDB (For high-volume Event Logs)
var mongoConn = "mongodb://mongo:taOtHmJnOLgnMorrtJpDZLmozClPXmOq@crossover.proxy.rlwy.net:30926";
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
    db.Database.EnsureCreated();

    // AUTO-MIGRATION: Fix missing columns in existing tables
    try 
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE AgentReports ADD COLUMN TenantId INT NOT NULL DEFAULT 1;");
    } 
    catch { /* Ignore */ }

    // AUTO-MIGRATION: FORCE RECREATE TABLES (As requested)
    try 
    {
        // 1. Drop existing tables (Users first due to dependency)
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS Users;");
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS Tenants;");

        // 2. Create Tenants Table
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE Tenants (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                Name LONGTEXT NOT NULL,
                ApiKey LONGTEXT NOT NULL,
                Plan LONGTEXT NOT NULL
            );
        ");

        // 3. Seed Tenants
        db.Database.ExecuteSqlRaw("INSERT INTO Tenants (Id, Name, ApiKey, Plan) VALUES (1, 'CyberCorp Inc', 'default-tenant-key', 'Enterprise')");
        db.Database.ExecuteSqlRaw("INSERT INTO Tenants (Id, Name, ApiKey, Plan) VALUES (2, 'RetailChain Ltd', 'retail-tenant-key', 'Starter')");

        // 4. Create Users Table (Linked to Tenants)
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE Users (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                Username LONGTEXT NOT NULL,
                PasswordHash LONGTEXT NOT NULL,
                Role LONGTEXT NOT NULL,
                TenantId INT NULL
            );
        ");

        // 5. Seed Users
        db.Database.ExecuteSqlRaw("INSERT INTO Users (Id, Username, PasswordHash, Role, TenantId) VALUES (1, 'admin', 'admin123', 'SuperAdmin', NULL)");
        db.Database.ExecuteSqlRaw("INSERT INTO Users (Id, Username, PasswordHash, Role, TenantId) VALUES (2, 'tenant_admin', 'tenant123', 'TenantAdmin', 1)");
        db.Database.ExecuteSqlRaw("INSERT INTO Users (Id, Username, PasswordHash, Role, TenantId) VALUES (3, 'analyst_1', 'analyst123', 'Analyst', 1)");
        db.Database.ExecuteSqlRaw("INSERT INTO Users (Id, Username, PasswordHash, Role, TenantId) VALUES (4, 'retail_admin', 'retail123', 'TenantAdmin', 2)");
        db.Database.ExecuteSqlRaw("INSERT INTO Users (Id, Username, PasswordHash, Role, TenantId) VALUES (5, 'analyst_2', 'analyst123', 'Analyst', 2)");
        
        Console.WriteLine("SUCCESS: Database Tables Recreated and Seeded.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("CRITICAL DB INIT ERROR: " + ex.Message);
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

// API: List Tenants (Super Admin)
app.MapGet("/api/tenants", async (AppDbContext db) =>
{
    // In real app: Add [Authorize(Roles="SuperAdmin")]
    return await db.Tenants.ToListAsync();
});

// API: Create Tenant (Super Admin)
app.MapPost("/api/tenants", async ([FromBody] Tenant tenant, AppDbContext db) =>
{
    tenant.ApiKey = Guid.NewGuid().ToString(); // Generate Key
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync();
    return Results.Created($"/api/tenants/{tenant.Id}", tenant);
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
