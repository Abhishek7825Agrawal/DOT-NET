using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class PaperCopyrightReport
{
    public int Id { get; set; }

    [Required]
    public int PaperId { get; set; }
    public ResearchPaper Paper { get; set; } = null!;

    [StringLength(450)]
    public string? ReporterUserId { get; set; }
    public ApplicationUser? ReporterUser { get; set; }

    [Required, StringLength(150)]
    public string ReporterName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string ReporterEmail { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string InfringedWorkTitle { get; set; } = string.Empty;

    [Required]
    public string InfringementDescription { get; set; } = string.Empty;

    public bool GoodFaithDeclaration { get; set; }

    public CopyrightReportStatus Status { get; set; } = CopyrightReportStatus.UnderReview;

    public string? AdminRemarks { get; set; }

    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAtUtc { get; set; }

    [StringLength(450)]
    public string? ResolvedByUserId { get; set; }
}
