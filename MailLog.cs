using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace watch_sec_backend;

public class MailLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public bool HasAttachments { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
