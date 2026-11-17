using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class LibrarySetting
{
    public int Id { get; set; }

    [Range(1, 365)]
    public int LoanPeriodDays { get; set; } = 14;

    [Range(0, 10000)]
    public decimal FineRatePerDay { get; set; } = 5.00m;

    [Range(1, 50)]
    public int MaxBooksPerMember { get; set; } = 5;

    [Range(0, 50000)]
    public decimal LostBookPenaltyFee { get; set; } = 250.00m;

    [Range(0, 30)]
    public int GracePeriodDays { get; set; } = 0;

    [Required, StringLength(80)]
    public string TimeZoneId { get; set; } = "Asia/Kolkata";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

