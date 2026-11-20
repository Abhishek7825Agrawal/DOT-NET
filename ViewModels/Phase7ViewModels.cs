using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.ViewModels;

public class UserActivityPageViewModel
{
    public IReadOnlyList<ActivityLogRowViewModel> Items { get; set; } = [];
    public string? Query { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

public sealed class ActivityLogRowViewModel
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityReference { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime LocalTimestamp { get; set; }
}

public sealed class AdminActivityPageViewModel : UserActivityPageViewModel
{
    public string? UserFilter { get; set; }
    public string? RoleFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public string? ActivityTypeFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? EntityFilter { get; set; }
}

public sealed class SecurityAuditPageViewModel
{
    public IReadOnlyList<SecurityAuditRowViewModel> Items { get; set; } = [];
    public string? Query { get; set; }
    public string? Severity { get; set; }
    public string? Outcome { get; set; }
    public string? Role { get; set; }
    public string? Category { get; set; }
    public string? Entity { get; set; }
    public string? Correlation { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

public sealed class SecurityAuditRowViewModel
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserRole { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityReference { get; set; }
    public string? RequestPath { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime LocalTimestamp { get; set; }
}

public sealed class AdminUserListViewModel
{
    public IReadOnlyList<AdminUserRowViewModel> Users { get; set; } = [];
    public string? Query { get; set; }
    public string? Role { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

public sealed class AdminUserRowViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? LastResetStatus { get; set; }
}

public sealed class AdminUserDetailsViewModel
{
    public AdminUserRowViewModel Account { get; set; } = new();
    public IReadOnlyList<SecurityAuditRowViewModel> PasswordHistory { get; set; } = [];
}

public sealed class PasswordManagementViewModel
{
    public IReadOnlyList<SecurityAuditRowViewModel> Items { get; set; } = [];
    public string? Query { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}
