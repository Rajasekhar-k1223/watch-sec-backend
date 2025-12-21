using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace watch_sec_backend;

public class OCRLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string AgentId { get; set; } = string.Empty;
    public string ScreenshotId { get; set; } = string.Empty; // Link to the image
    public string ExtractedText { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> SensitiveKeywordsFound { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
