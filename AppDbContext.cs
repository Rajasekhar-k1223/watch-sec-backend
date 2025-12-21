using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace watch_sec_backend;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AgentReportEntity> AgentReports { get; set; }
    public DbSet<Tenant> Tenants { get; set; }

    public DbSet<User> Users { get; set; }
    public DbSet<Policy> Policies { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Seed Tenants
        modelBuilder.Entity<Tenant>().HasData(
            new Tenant { Id = 1, Name = "CyberCorp Inc", ApiKey = "default-tenant-key", Plan = "Enterprise" },
            new Tenant { Id = 2, Name = "RetailChain Ltd", ApiKey = "retail-tenant-key", Plan = "Starter" }
        );

        // Seed Users
        // Passwords: admin123, tenant123, retail123, analyst123
        modelBuilder.Entity<User>().HasData(
            // 1. Super Admin (Global)
            new User { Id = 1, Username = "admin", PasswordHash = "admin123", Role = "SuperAdmin", TenantId = null },
            
            // 2. CyberCorp Users
            new User { Id = 2, Username = "tenant_admin", PasswordHash = "tenant123", Role = "TenantAdmin", TenantId = 1 },
            new User { Id = 3, Username = "analyst_1", PasswordHash = "analyst123", Role = "Analyst", TenantId = 1 },

            // 3. RetailChain Users
            new User { Id = 4, Username = "retail_admin", PasswordHash = "retail123", Role = "TenantAdmin", TenantId = 2 },
            new User { Id = 5, Username = "analyst_2", PasswordHash = "analyst123", Role = "Analyst", TenantId = 2 }
        );
    }
}

public class Tenant 
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = ""; // Used by Agent to Auth
    public string Plan { get; set; } = "Starter";
}

public class User
{
    [Key]
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Analyst"; // SuperAdmin, TenantAdmin, Analyst
    public int? TenantId { get; set; } // Null for SuperAdmin/System users
}

public class AgentReportEntity
{
    [Key]
    public int Id { get; set; }
    public string AgentId { get; set; } = "";
    public int TenantId { get; set; } // FK
    public string Status { get; set; } = "";
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public DateTime Timestamp { get; set; }
}
