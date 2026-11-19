using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class BookAnnotation
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int DigitalBookId { get; set; }
    public DigitalBook DigitalBook { get; set; } = null!;

    public int DocumentVersion { get; set; } = 1;

    [Range(1, 100000)]
    public int PageNumber { get; set; }

    [Required, StringLength(2000)]
    public string SelectedText { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string HighlightColor { get; set; } = "yellow";

    [Required]
    public string TextAnchorJson { get; set; } = "{}";

    [Required]
    public string PositionJson { get; set; } = "{}";

    public int? LinkedNoteId { get; set; }
    public UserNote? LinkedNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
}
