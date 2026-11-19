using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class PaperOrder
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string OrderNumber { get; set; } = string.Empty;

    [Required]
    public int PaperId { get; set; }
    public ResearchPaper Paper { get; set; } = null!;

    [Required, StringLength(450)]
    public string BuyerUserId { get; set; } = string.Empty;
    public ApplicationUser BuyerUser { get; set; } = null!;

    // Financial breakdown snapshots (immutable at order time)
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlatformCommissionRate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PlatformCommissionAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AuthorEarningsAmount { get; set; }

    public PaperPaymentStatus PaymentStatus { get; set; } = PaperPaymentStatus.Pending;

    [Required, StringLength(50)]
    public string PaymentMethod { get; set; } = "FreeAccess";

    [StringLength(100)]
    public string? PaymentReference { get; set; }

    public DateTime OrderDateUtc { get; set; } = DateTime.UtcNow;
}
