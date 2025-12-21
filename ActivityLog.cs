using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace watch_sec_backend;

public class ActivityLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string AgentId { get; set; } = string.Empty;
    public int TenantId { get; set; } // Added for Multi-Tenancy
    
    public string ActivityType { get; set; } = "App"; // "App" or "Web"
    
    public string WindowTitle { get; set; } = string.Empty;
    
    public string ProcessName { get; set; } = string.Empty; // For Apps
    
    public string Url { get; set; } = string.Empty; // For Web
    
    public double DurationSeconds { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // AI Fields
    public double RiskScore { get; set; }
    public string RiskLevel { get; set; } = "LOW";
}
