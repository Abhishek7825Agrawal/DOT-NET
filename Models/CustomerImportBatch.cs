using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public enum ImportRowStatus
{
    Valid = 0,
    Invalid = 1,
    Duplicate = 2,
    Imported = 3,
    Failed = 4
}

public sealed class CustomerImportBatch
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string BatchReference { get; set; } = string.Empty;

    [Required]
    public string StaffUserId { get; set; } = string.Empty;
    public ApplicationUser StaffUser { get; set; } = null!;

    [Required, StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int TotalRows { get; set; }
    public int ValidRowsCount { get; set; }
    public int InvalidRowsCount { get; set; }
    public int DuplicateRowsCount { get; set; }
    public int SuccessImportedCount { get; set; }

    public bool IsCommitted { get; set; }
    public DateTime? CommittedAtUtc { get; set; }

    public ICollection<CustomerImportRow> Rows { get; set; } = new List<CustomerImportRow>();
}

public sealed class CustomerImportRow
{
    public int Id { get; set; }

    public int BatchId { get; set; }
    public CustomerImportBatch Batch { get; set; } = null!;

    public int RowNumber { get; set; }

    [StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    public ImportRowStatus Status { get; set; }

    [StringLength(500)]
    public string? ValidationMessage { get; set; }

    public string? ActivationToken { get; set; }

    public string? CreatedUserId { get; set; }
}
