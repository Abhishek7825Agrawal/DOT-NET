namespace LibraryManagementSystem.Models;

/// <summary>Append-only, user-visible history of completed application actions.</summary>
public sealed class UserActivityLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Success";
    public string? EntityType { get; set; }
    public string? EntityReference { get; set; }
    public string? ContextJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
