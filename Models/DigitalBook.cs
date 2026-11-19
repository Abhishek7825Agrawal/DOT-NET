using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public sealed class DigitalBook
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    [Required, StringLength(120)]
    public string StorageFileName { get; set; } = string.Empty;

    [Required, StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [Range(1, 100000)]
    public int PageCount { get; set; }

    public int DocumentVersion { get; set; } = 1;

    public bool IsDigitalAvailable { get; set; } = true;

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(450)]
    public string? UploadedByUserId { get; set; }

    [StringLength(64)]
    public string? ChecksumSha256 { get; set; }

    public ICollection<BookAnnotation> Annotations { get; set; } = new List<BookAnnotation>();
    public ICollection<ReadingProgress> ReadingProgresses { get; set; } = new List<ReadingProgress>();
    public ICollection<BookBookmark> Bookmarks { get; set; } = new List<BookBookmark>();
}
