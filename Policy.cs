using System.ComponentModel.DataAnnotations;

namespace watch_sec_backend;

public class Policy
{
    [Key]
    public int Id { get; set; }
    
    public string Name { get; set; } = string.Empty;
    
    // JSON string defining rules (e.g., [{"type": "process", "value": "notepad", "action": "kill"}])
    public string RulesJson { get; set; } = "[]"; 
    
    // Comma-separated list of actions (Block, Alert, Screenshot)
    public string Actions { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public int TenantId { get; set; }
}
