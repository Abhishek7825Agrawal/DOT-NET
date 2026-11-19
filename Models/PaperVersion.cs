using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class PaperVersion
{
    public int Id { get; set; }

    [Required]
    public int PaperId { get; set; }
    public ResearchPaper Paper { get; set; } = null!;

    [Required]
    public int VersionNumber { get; set; } = 1;

    [Required, StringLength(120)]
    public string StorageFileName { get; set; } = string.Empty;

    [Required, StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public int PageCount { get; set; }

    [Required, StringLength(64)]
    public string ChecksumSha256 { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ChangeSummary { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, StringLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;
}
