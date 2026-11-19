using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class BookBookmark
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int DigitalBookId { get; set; }
    public DigitalBook DigitalBook { get; set; } = null!;

    [Range(1, 100000)]
    public int PageNumber { get; set; }

    [StringLength(100)]
    public string? Label { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
