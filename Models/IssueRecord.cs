using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public enum IssueStatus { Active = 0, Returned = 1, Lost = 2 }

public sealed class IssueRecord
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public Member Member { get; set; } = null!;
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;
    // These legacy-named columns contain library-calendar dates at midnight,
    // not UTC instants. Keep the column names and values stable for existing databases.
    public DateTime IssueDateUtc { get; set; }
    public DateTime DueDateUtc { get; set; }
    public DateTime? ReturnDateUtc { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.Active;

    [Range(0, 1000000)]
    public decimal FineRatePerDay { get; set; }

    [Range(0, 1000000)]
    public decimal FineAmount { get; set; }

    [Range(0, 1000000)]
    public decimal FinePaidAmount { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<FinePayment> FinePayments { get; set; } = new List<FinePayment>();
}
