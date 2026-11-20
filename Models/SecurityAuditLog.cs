namespace LibraryManagementSystem.Models;

/// <summary>Append-only server-side security and administrative audit event.</summary>
public sealed class SecurityAuditLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string? UserRole { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "Information";
    public string Outcome { get; set; } = "Success";
    public string Description { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityReference { get; set; }
    public string? RequestPath { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
