using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class AuthorFinancialMetrics
{
    public int TotalSubmissions { get; set; }
    public int ApprovedCount { get; set; }
    public int PendingCount { get; set; }
    public int TotalSalesCount { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal TotalCommissionDeducted { get; set; }
    public decimal NetEarnings { get; set; }
}

public sealed class MarketplaceService(
    ApplicationDbContext db,
    PaperStorageService? storage = null)
{
    public async Task<MarketplaceSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.MarketplaceSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new MarketplaceSettings
            {
                PlatformCommissionRate = 15.00m,
                RequireAdminApproval = true,
                MinimumListingPrice = 0.00m,
                MaximumListingPrice = 25000.00m,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.MarketplaceSettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
        }
        return settings;
    }

    /// <summary>
    /// Checks whether the user is authorized to read/stream/download a research paper.
    /// Strictly prevents pending or failed orders from granting access to paid PDFs.
    /// </summary>
    public async Task<bool> CanUserAccessPaperAsync(
        string? userId,
        int paperId,
        bool isAdminOrStaff,
        CancellationToken cancellationToken = default)
    {
        var paper = await db.ResearchPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, cancellationToken);

        if (paper is null) return false;

        // Submitter and admins always have access to their own manuscripts regardless of status
        if (!string.IsNullOrEmpty(userId) && (paper.SubmitterUserId == userId || isAdminOrStaff))
        {
            return true;
        }

        // Public/Readers can only access Approved papers
        if (paper.Status != PaperStatus.Approved)
        {
            return false;
        }

        // Free open-access papers are available to all authorized cardholders/users
        if (!paper.IsPaid || paper.Price <= 0m)
        {
            return true;
        }

        // Paid papers require an authenticated user with a COMPLETED order
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var hasCompletedOrder = await db.PaperOrders.AsNoTracking()
            .AnyAsync(o => o.PaperId == paperId
                        && o.BuyerUserId == userId
                        && o.PaymentStatus == PaperPaymentStatus.Completed, cancellationToken);

        return hasCompletedOrder;
    }

    /// <summary>
    /// Evaluates eligibility for purchase, enforcing self-purchase and duplicate purchase barriers.
    /// </summary>
    public async Task<(bool CanPurchase, string? Reason)> CanUserPurchasePaperAsync(
        string userId,
        int paperId,
        CancellationToken cancellationToken = default)
    {
        var paper = await db.ResearchPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, cancellationToken);

        if (paper is null)
        {
            return (false, "Research paper not found.");
        }

        if (paper.Status != PaperStatus.Approved)
        {
            return (false, "This manuscript is not currently available for acquisition.");
        }

        // Barrier 1: Self-purchase prevention
        if (paper.SubmitterUserId == userId)
        {
            return (false, "Authors cannot purchase their own research manuscripts.");
        }

        // Barrier 2: Duplicate purchase prevention
        var alreadyPurchased = await db.PaperOrders.AsNoTracking()
            .AnyAsync(o => o.PaperId == paperId
                        && o.BuyerUserId == userId
                        && o.PaymentStatus == PaperPaymentStatus.Completed, cancellationToken);

        if (alreadyPurchased)
        {
            return (false, "You already hold verified access rights to this research paper.");
        }

        return (true, null);
    }

    /// <summary>
    /// Executes order creation inside a database transaction, calculating fee snapshots and setting verified status.
    /// Free papers complete immediately; paid papers remain in Pending status until real gateway integration.
    /// </summary>
    public async Task<(bool Success, PaperOrder? Order, string? ErrorMessage)> CreateOrderAsync(
        string buyerUserId,
        int paperId,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        var (canPurchase, reason) = await CanUserPurchasePaperAsync(buyerUserId, paperId, cancellationToken);
        if (!canPurchase)
        {
            return (false, null, reason);
        }

        var paper = await db.ResearchPapers
            .FirstOrDefaultAsync(p => p.Id == paperId, cancellationToken);

        if (paper is null)
        {
            return (false, null, "Paper not found.");
        }

        var settings = await GetSettingsAsync(cancellationToken);
        var commissionRate = settings.PlatformCommissionRate;
        var price = paper.IsPaid ? paper.Price : 0.00m;

        var commissionAmount = Math.Round(price * (commissionRate / 100m), 2);
        var authorEarnings = price - commissionAmount;

        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        // Open transaction
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var isFree = price == 0m || !paper.IsPaid;

            var order = new PaperOrder
            {
                OrderNumber = orderNumber,
                PaperId = paperId,
                BuyerUserId = buyerUserId,
                Amount = price,
                PlatformCommissionRate = commissionRate,
                PlatformCommissionAmount = commissionAmount,
                AuthorEarningsAmount = authorEarnings,
                PaymentMethod = isFree ? "FreeAccess" : paymentMethod,
                PaymentStatus = isFree ? PaperPaymentStatus.Completed : PaperPaymentStatus.Pending,
                PaymentReference = isFree ? "FREE-GRANT" : null,
                OrderDateUtc = DateTime.UtcNow
            };

            db.PaperOrders.Add(order);

            if (isFree)
            {
                paper.DownloadCount++;
                paper.PurchaseCount++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (true, order, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (false, null, $"Failed to process order transaction: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates and persists a new author research paper submission with initial immutable Version 1.
    /// </summary>
    public async Task<ResearchPaper> SubmitPaperAsync(
        ResearchPaper paper,
        PaperDocumentMetadata pdfMeta,
        CancellationToken cancellationToken = default)
    {
        paper.StorageFileName = pdfMeta.StorageFileName;
        paper.OriginalFileName = pdfMeta.OriginalFileName;
        paper.FileSizeBytes = pdfMeta.FileSizeBytes;
        paper.PageCount = pdfMeta.PageCount;
        paper.ChecksumSha256 = pdfMeta.ChecksumSha256;
        paper.CurrentVersion = 1;
        paper.Status = PaperStatus.PendingReview;
        paper.SubmittedAtUtc = DateTime.UtcNow;

        var version1 = new PaperVersion
        {
            VersionNumber = 1,
            StorageFileName = pdfMeta.StorageFileName,
            OriginalFileName = pdfMeta.OriginalFileName,
            FileSizeBytes = pdfMeta.FileSizeBytes,
            PageCount = pdfMeta.PageCount,
            ChecksumSha256 = pdfMeta.ChecksumSha256,
            ChangeSummary = "Initial manuscript submission",
            CreatedAtUtc = DateTime.UtcNow,
            UploadedByUserId = paper.SubmitterUserId
        };

        paper.Versions.Add(version1);
        db.ResearchPapers.Add(paper);
        await db.SaveChangesAsync(cancellationToken);

        return paper;
    }

    /// <summary>
    /// Submits author corrections, incrementing version and generating an immutable PaperVersion entry.
    /// </summary>
    public async Task<ResearchPaper> SubmitCorrectionAsync(
        int paperId,
        string authorUserId,
        PaperDocumentMetadata? newPdfMeta,
        string? title,
        string? abstractText,
        string? authors,
        string? affiliation,
        string? category,
        string? keywords,
        string? changeSummary,
        CancellationToken cancellationToken = default)
    {
        var paper = await db.ResearchPapers
            .Include(p => p.Versions)
            .FirstOrDefaultAsync(p => p.Id == paperId && p.SubmitterUserId == authorUserId, cancellationToken);

        if (paper is null)
        {
            throw new KeyNotFoundException("Research manuscript not found or permission denied.");
        }

        if (paper.Status != PaperStatus.CorrectionRequested && paper.Status != PaperStatus.Draft)
        {
            throw new InvalidOperationException("Corrections can only be submitted for manuscripts flagged for revision.");
        }

        if (!string.IsNullOrWhiteSpace(title)) paper.Title = title.Trim();
        if (!string.IsNullOrWhiteSpace(abstractText)) paper.Abstract = abstractText.Trim();
        if (!string.IsNullOrWhiteSpace(authors)) paper.Authors = authors.Trim();
        if (!string.IsNullOrWhiteSpace(affiliation)) paper.Affiliation = affiliation.Trim();
        if (!string.IsNullOrWhiteSpace(category)) paper.Category = category.Trim();
        if (!string.IsNullOrWhiteSpace(keywords)) paper.Keywords = keywords.Trim();

        paper.CurrentVersion++;
        var storageFileName = newPdfMeta?.StorageFileName ?? paper.StorageFileName;
        var originalFileName = newPdfMeta?.OriginalFileName ?? paper.OriginalFileName;
        var fileSizeBytes = newPdfMeta?.FileSizeBytes ?? paper.FileSizeBytes;
        var pageCount = newPdfMeta?.PageCount ?? paper.PageCount;
        var checksumSha256 = newPdfMeta?.ChecksumSha256 ?? paper.ChecksumSha256;

        if (newPdfMeta is not null)
        {
            paper.StorageFileName = storageFileName;
            paper.OriginalFileName = originalFileName;
            paper.FileSizeBytes = fileSizeBytes;
            paper.PageCount = pageCount;
            paper.ChecksumSha256 = checksumSha256;
        }

        var versionLog = new PaperVersion
        {
            PaperId = paper.Id,
            VersionNumber = paper.CurrentVersion,
            StorageFileName = storageFileName,
            OriginalFileName = originalFileName,
            FileSizeBytes = fileSizeBytes,
            PageCount = pageCount,
            ChecksumSha256 = checksumSha256,
            ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? $"Revision {paper.CurrentVersion}" : changeSummary.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UploadedByUserId = authorUserId
        };

        db.PaperVersions.Add(versionLog);

        paper.Status = PaperStatus.PendingReview;
        paper.RejectionReason = null; // Clear previous rejection note upon correction
        await db.SaveChangesAsync(cancellationToken);

        return paper;
    }

    /// <summary>
    /// Computes author dashboard financial KPI counters.
    /// </summary>
    public async Task<AuthorFinancialMetrics> GetAuthorMetricsAsync(
        string authorUserId,
        CancellationToken cancellationToken = default)
    {
        var papers = await db.ResearchPapers.AsNoTracking()
            .Where(p => p.SubmitterUserId == authorUserId)
            .ToListAsync(cancellationToken);

        var paperIds = papers.Select(p => p.Id).ToList();

        var orders = await db.PaperOrders.AsNoTracking()
            .Where(o => paperIds.Contains(o.PaperId) && o.PaymentStatus == PaperPaymentStatus.Completed)
            .ToListAsync(cancellationToken);

        return new AuthorFinancialMetrics
        {
            TotalSubmissions = papers.Count,
            ApprovedCount = papers.Count(p => p.Status == PaperStatus.Approved),
            PendingCount = papers.Count(p => p.Status == PaperStatus.PendingReview || p.Status == PaperStatus.CorrectionRequested),
            TotalSalesCount = orders.Count,
            GrossRevenue = orders.Sum(o => o.Amount),
            TotalCommissionDeducted = orders.Sum(o => o.PlatformCommissionAmount),
            NetEarnings = orders.Sum(o => o.AuthorEarningsAmount)
        };
    }
}
