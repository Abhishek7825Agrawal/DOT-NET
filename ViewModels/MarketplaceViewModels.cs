using System.ComponentModel.DataAnnotations;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Http;

namespace LibraryManagementSystem.ViewModels;

public sealed class MarketplaceCatalogViewModel
{
    public List<PaperCardViewModel> Papers { get; set; } = [];
    public string? Query { get; set; }
    public string? Category { get; set; }
    public string? PriceFilter { get; set; } = "all"; // all, free, paid
    public string? Sort { get; set; } = "newest"; // newest, popular, price-asc, price-desc
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<string> Categories { get; set; } = [];
}

public sealed class PaperCardViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string Affiliation { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public int PublicationYear { get; set; }
    public bool IsPaid { get; set; }
    public decimal Price { get; set; }
    public int PageCount { get; set; }
    public int DownloadCount { get; set; }
    public int PurchaseCount { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}

public sealed class PaperDetailsViewModel
{
    public ResearchPaper Paper { get; set; } = null!;
    public bool CanAccess { get; set; }
    public bool IsOwned { get; set; }
    public bool IsAuthor { get; set; }
    public bool CanPurchase { get; set; }
    public string? PurchaseBlockedReason { get; set; }
    public List<PaperVersion> VersionHistory { get; set; } = [];
}

public sealed class PaperSubmitViewModel
{
    [Required(ErrorMessage = "Manuscript title is required."), StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Academic abstract is required."), MinLength(50, ErrorMessage = "Abstract should be at least 50 characters.")]
    public string Abstract { get; set; } = string.Empty;

    [Required(ErrorMessage = "Author(s) required."), StringLength(250)]
    public string Authors { get; set; } = string.Empty;

    [Required(ErrorMessage = "Academic affiliation / institution required."), StringLength(250)]
    public string Affiliation { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category / Domain is required."), StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required(ErrorMessage = "Keywords are required for indexing."), StringLength(300)]
    public string Keywords { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Doi { get; set; }

    [Range(1900, 2100)]
    public int PublicationYear { get; set; } = DateTime.UtcNow.Year;

    public bool IsPaid { get; set; }

    [Range(0, 25000, ErrorMessage = "Listing price must be between ₹0 and ₹25,000.")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "Please attach a valid PDF manuscript file.")]
    public IFormFile? PdfFile { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the copyright warranty and distribution declaration.")]
    public bool CopyrightAccepted { get; set; }
}

public sealed class PaperEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MinLength(50)]
    public string Abstract { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Authors { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Affiliation { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Keywords { get; set; } = string.Empty;

    public int CurrentVersion { get; set; }
    public string? RejectionReason { get; set; }
    public string? EditorialFeedback { get; set; }

    public IFormFile? NewPdfFile { get; set; }

    [StringLength(500)]
    public string? ChangeSummary { get; set; }
}

public sealed class AuthorDeskViewModel
{
    public Services.AuthorFinancialMetrics Metrics { get; set; } = new();
    public List<ResearchPaper> MyPapers { get; set; } = [];
}

public sealed class SalesLedgerViewModel
{
    public List<SalesLedgerItemViewModel> Transactions { get; set; } = [];
    public decimal TotalGross { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalNet { get; set; }
}

public sealed class SalesLedgerItemViewModel
{
    public string OrderNumber { get; set; } = string.Empty;
    public int PaperId { get; set; }
    public string PaperTitle { get; set; } = string.Empty;
    public DateTime OrderDateUtc { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetEarnings { get; set; }
    public PaperPaymentStatus PaymentStatus { get; set; }
}

public sealed class OrderCheckoutViewModel
{
    public int PaperId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal PlatformCommissionRate { get; set; }
    public bool IsFree => Price <= 0m;
    public bool CanPurchase { get; set; }
    public string? BlockedReason { get; set; }
}

public sealed class MyPurchasesViewModel
{
    public List<PurchasedPaperItemViewModel> Purchases { get; set; } = [];
}

public sealed class PurchasedPaperItemViewModel
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int PaperId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal PricePaid { get; set; }
    public DateTime OrderDateUtc { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public int PageCount { get; set; }
}

public sealed class CopyrightReportInputModel
{
    public int PaperId { get; set; }
    public string PaperTitle { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string ReporterName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string ReporterEmail { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string InfringedWorkTitle { get; set; } = string.Empty;

    [Required, MinLength(20, ErrorMessage = "Please describe the nature of infringement in detail.")]
    public string InfringementDescription { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm this declaration in good faith.")]
    public bool GoodFaithDeclaration { get; set; }
}

public sealed class AdminModerationViewModel
{
    public List<ResearchPaper> PendingPapers { get; set; } = [];
    public List<ResearchPaper> ApprovedPapers { get; set; } = [];
    public List<PaperCopyrightReport> CopyrightReports { get; set; } = [];
    public MarketplaceSettings Settings { get; set; } = null!;
}

public sealed class PaperReviewViewModel
{
    public ResearchPaper Paper { get; set; } = null!;
    public List<PaperVersion> Versions { get; set; } = [];

    [Required]
    public string Decision { get; set; } = "Approve"; // Approve, Reject, RequestCorrection

    public string? Remarks { get; set; }
}
