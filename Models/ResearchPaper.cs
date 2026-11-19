using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class ResearchPaper
{
    public int Id { get; set; }

    [Required, StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Abstract { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Authors { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Affiliation { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Keywords { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Doi { get; set; }

    public int PublicationYear { get; set; } = DateTime.UtcNow.Year;

    public bool IsPaid { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    public PaperStatus Status { get; set; } = PaperStatus.PendingReview;

    public string? RejectionReason { get; set; }

    public string? EditorialFeedback { get; set; }

    // Active manuscript file details (points to current active version)
    [Required, StringLength(120)]
    public string StorageFileName { get; set; } = string.Empty;

    [Required, StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public int PageCount { get; set; }

    [Required, StringLength(64)]
    public string ChecksumSha256 { get; set; } = string.Empty;

    public int CurrentVersion { get; set; } = 1;

    // Copyright & intellectual property declaration
    public bool CopyrightAccepted { get; set; }

    public DateTime CopyrightAcceptedAtUtc { get; set; } = DateTime.UtcNow;

    // Submitter
    [Required, StringLength(450)]
    public string SubmitterUserId { get; set; } = string.Empty;
    public ApplicationUser SubmitterUser { get; set; } = null!;

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    // Review audit trail
    public DateTime? ReviewedAtUtc { get; set; }

    [StringLength(450)]
    public string? ReviewedByUserId { get; set; }

    // Engagement metrics
    public int DownloadCount { get; set; }

    public int PurchaseCount { get; set; }

    // Navigations
    public ICollection<PaperVersion> Versions { get; set; } = new List<PaperVersion>();
    public ICollection<PaperOrder> Orders { get; set; } = new List<PaperOrder>();
    public ICollection<PaperCopyrightReport> CopyrightReports { get; set; } = new List<PaperCopyrightReport>();
}
