using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class Member
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public ICollection<IssueRecord> IssueRecords { get; set; } = new List<IssueRecord>();
}
