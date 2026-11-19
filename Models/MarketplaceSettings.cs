using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class MarketplaceSettings
{
    public int Id { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlatformCommissionRate { get; set; } = 15.00m; // Default 15%

    public bool RequireAdminApproval { get; set; } = true;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinimumListingPrice { get; set; } = 0.00m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MaximumListingPrice { get; set; } = 25000.00m;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(450)]
    public string? UpdatedByUserId { get; set; }
}
