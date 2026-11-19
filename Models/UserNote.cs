using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class UserNote
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    [Required, StringLength(200)]
    public string Title { get; set; } = "Reading Notes";

    [Required]
    public string ContentDeltaJson { get; set; } = "{\"ops\":[]}";

    public string? SanitizedContentHtml { get; set; }

    [StringLength(500)]
    public string? PlainTextExcerpt { get; set; }

    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
}
