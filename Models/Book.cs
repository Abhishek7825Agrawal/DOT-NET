using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class Book
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Author { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Isbn { get; set; }

    [Required, StringLength(80)]
    public string Category { get; set; } = string.Empty;

    [StringLength(160)]
    public string? Publisher { get; set; }

    [Range(1450, 2100)]
    public int? PublicationYear { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, 100000)]
    public int TotalQuantity { get; set; }

    [Range(0, 100000)]
    public int AvailableQuantity { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<IssueRecord> IssueRecords { get; set; } = new List<IssueRecord>();
    public DigitalBook? DigitalEdition { get; set; }
    public ICollection<UserNote> Notes { get; set; } = new List<UserNote>();
}
