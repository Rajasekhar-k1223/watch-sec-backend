using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace watch_sec_backend;

public class SecurityEventLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string AgentId { get; set; } = "";
    public int TenantId { get; set; }
    public string Type { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
