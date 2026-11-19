using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class ReadingProgress
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int DigitalBookId { get; set; }
    public DigitalBook DigitalBook { get; set; } = null!;

    [Range(1, 100000)]
    public int LastPageRead { get; set; } = 1;

    [Range(1, 100000)]
    public int TotalPages { get; set; } = 1;

    public decimal PercentComplete { get; set; }

    public DateTime LastReadAtUtc { get; set; } = DateTime.UtcNow;
}
