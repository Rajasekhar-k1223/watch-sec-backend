using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace watch_sec_backend;

public class StreamHub : Hub
{
    // Store latest screen buffer in memory (Smart Storage: Only keep last frame per agent)
    private static ConcurrentDictionary<string, string> _latestScreens = new(); 
    // Rate Limit Storage: Track last save time per agent
    private static ConcurrentDictionary<string, DateTime> _lastSaved = new(); 
    // Trigger Capture: Force save next frame if event occurred
    private static ConcurrentDictionary<string, bool> _forceCapture = new();
    
    private readonly IMongoClient _mongo;
    private readonly AppDbContext _db;

    public StreamHub(IMongoClient mongo, AppDbContext db)
    {
        _mongo = mongo;
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var agentId = httpContext?.Request.Query["agentId"].ToString();
        var tenantKey = httpContext?.Request.Headers["X-Tenant-Key"].ToString();

        // 1. Allow Authenticated Users (Frontend Dashboard)
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            await base.OnConnectedAsync();
            return;
        }

        // 2. Allow Agents with Valid Tenant Key
        if (!string.IsNullOrEmpty(tenantKey))
        {
            var tenant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_db.Tenants, t => t.ApiKey == tenantKey);
            if (tenant != null)
            {
                if (!string.IsNullOrEmpty(agentId))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, agentId);
                }
                await base.OnConnectedAsync();
                return;
            }
        }

        // 3. Reject Unknowns
        Console.WriteLine($"[StreamHub] REJECTED Connection from {agentId ?? "Unknown"}: Missing or Invalid Creds");
        Context.Abort();
    }

    public async Task SendScreen(string agentId, string base64Image)
    {
        // Debug Log to confirm reception
         Console.WriteLine($"[StreamHub] Rx Screen from {agentId}: {base64Image.Length} bytes");
        
        _latestScreens[agentId] = base64Image;

        // Smart Storage Logic:
        // 1. Time-based: Save every 30s
        // 2. Event-based: Save IMMEDIATELY if an event triggered
        var lastTime = _lastSaved.GetOrAdd(agentId, DateTime.MinValue);
        bool isEventTrigger = _forceCapture.TryRemove(agentId, out _);
        
        if (isEventTrigger || (DateTime.UtcNow - lastTime).TotalSeconds >= 30)
        {
             _lastSaved[agentId] = DateTime.UtcNow;

            // 1. Storage Path Logic
            // Structure: Storage/Screenshots/{AgentId}/{yyyyMMdd}/{HHmmss.webp}
            var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
            var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Screenshots", agentId, dateFolder);
            
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }

            // 2. Save to Disk (Async)
            _ = Task.Run(async () => 
            {
                try 
                {
                    var bytes = Convert.FromBase64String(base64Image);
                    // Add suffix if event triggered to distinguish
                    var suffix = isEventTrigger ? "_ALERT" : "";
                    var filename = $"{DateTime.UtcNow:HHmmss}{suffix}.webp";
                    var filePath = Path.Combine(storagePath, filename);
                    await File.WriteAllBytesAsync(filePath, bytes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save screenshot: {ex.Message}");
                }
            });
        }

        // 3. Broadcast (Always stream real-time)
        await Clients.All.SendAsync("ReceiveScreen", agentId, base64Image);
    }

    public async Task SendEvent(string agentId, string type, string details)
    {
        Console.WriteLine($"[StreamHub] Received Event: {type} from {agentId}");
        var timestamp = DateTime.UtcNow;

        // Trigger Evidence Capture
        _forceCapture[agentId] = true;

        try 
        {
            // Persist to MongoDB
            var db = _mongo.GetDatabase("watchsec");
            var collection = db.GetCollection<SecurityEventLog>("events");
            await collection.InsertOneAsync(new SecurityEventLog 
            {
                AgentId = agentId,
                Type = type,
                Details = details,
                Timestamp = timestamp
            });
            Console.WriteLine($"[StreamHub] Event saved to MongoDB ID: {agentId}");
        }
        catch (Exception ex)
        {
             Console.WriteLine($"[StreamHub] MongoDB Insert Failed: {ex.Message}");
        }

        // Broadcast security event to dashboard
        await Clients.All.SendAsync("ReceiveEvent", agentId, type, details, timestamp);
    }

    public async Task KillProcess(string targetAgentId, int pid)
    {
        // Send command ONLY to the specific agent group
        await Clients.Group(targetAgentId).SendAsync("KillProcess", pid);
    }
}
