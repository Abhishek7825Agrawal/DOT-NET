using System.Security.Claims;
using System.Text.RegularExpressions;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize]
public sealed class MarketplaceController(
    ApplicationDbContext db,
    MarketplaceService marketplace,
    PaperStorageService storage) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsAdminOrStaff => User.IsInRole("Admin") || User.IsInRole("Staff");

    /// <summary>
    /// Public & Member Marketplace Catalogue: Lists published research papers.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string? q,
        string? category,
        string? priceFilter = "all",
        string? sort = "newest",
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 12;

        var query = db.ResearchPapers.AsNoTracking()
            .Where(p => p.Status == PaperStatus.Approved);

        // Search filter (title, abstract, authors, keywords)
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Title.Contains(term)
                                  || p.Abstract.Contains(term)
                                  || p.Authors.Contains(term)
                                  || p.Keywords.Contains(term));
        }

        // Category / Subject Domain filter
        if (!string.IsNullOrWhiteSpace(category) && category != "All")
        {
            query = query.Where(p => p.Category == category);
        }

        // Price filter (all, free, paid)
        if (string.Equals(priceFilter, "free", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => !p.IsPaid || p.Price <= 0m);
        }
        else if (string.Equals(priceFilter, "paid", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.IsPaid && p.Price > 0m);
        }

        // Sorting
        query = sort switch
        {
            "popular" => query.OrderByDescending(p => p.PurchaseCount + p.DownloadCount),
            "price-asc" => query.OrderBy(p => p.Price),
            "price-desc" => query.OrderByDescending(p => p.Price),
            _ => query.OrderByDescending(p => p.SubmittedAtUtc)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var papers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaperCardViewModel
            {
                Id = p.Id,
                Title = p.Title,
                Authors = p.Authors,
                Affiliation = p.Affiliation,
                Category = p.Category,
                Keywords = p.Keywords,
                PublicationYear = p.PublicationYear,
                IsPaid = p.IsPaid,
                Price = p.Price,
                PageCount = p.PageCount,
                DownloadCount = p.DownloadCount,
                PurchaseCount = p.PurchaseCount,
                SubmittedAtUtc = p.SubmittedAtUtc
            })
            .ToListAsync(cancellationToken);

        var categories = await db.ResearchPapers.AsNoTracking()
            .Where(p => p.Status == PaperStatus.Approved)
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        var vm = new MarketplaceCatalogViewModel
        {
            Papers = papers,
            Query = q,
            Category = category,
            PriceFilter = priceFilter,
            Sort = sort,
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
            Categories = categories
        };

        return View(vm);
    }

    /// <summary>
    /// Paper Details Page: Full academic abstract, author affiliation, DOI, pricing, and purchase/read CTA.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.AsNoTracking()
            .Include(p => p.SubmitterUser)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (paper is null)
        {
            return NotFound("Research paper not found.");
        }

        // Non-approved papers can only be viewed by author or admin/staff
        if (paper.Status != PaperStatus.Approved && paper.SubmitterUserId != CurrentUserId && !IsAdminOrStaff)
        {
            return Forbid();
        }

        var canAccess = await marketplace.CanUserAccessPaperAsync(CurrentUserId, id, IsAdminOrStaff, cancellationToken);
        var isAuthor = !string.IsNullOrEmpty(CurrentUserId) && paper.SubmitterUserId == CurrentUserId;

        var isOwned = false;
        if (!string.IsNullOrEmpty(CurrentUserId))
        {
            isOwned = await db.PaperOrders.AsNoTracking()
                .AnyAsync(o => o.PaperId == id
                            && o.BuyerUserId == CurrentUserId
                            && o.PaymentStatus == PaperPaymentStatus.Completed, cancellationToken);
        }

        var (canPurchase, blockedReason) = (!string.IsNullOrEmpty(CurrentUserId))
            ? await marketplace.CanUserPurchasePaperAsync(CurrentUserId, id, cancellationToken)
            : (false, "Please sign in to acquire or purchase this research manuscript.");

        var versions = await db.PaperVersions.AsNoTracking()
            .Where(v => v.PaperId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        var vm = new PaperDetailsViewModel
        {
            Paper = paper,
            CanAccess = canAccess,
            IsOwned = isOwned,
            IsAuthor = isAuthor,
            CanPurchase = canPurchase,
            PurchaseBlockedReason = blockedReason,
            VersionHistory = versions
        };

        return View(vm);
    }

    /// <summary>
    /// Checkout / Order Confirmation Page.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Checkout(int id, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (paper is null || paper.Status != PaperStatus.Approved)
        {
            return NotFound("Manuscript not available for acquisition.");
        }

        var (canPurchase, blockedReason) = await marketplace.CanUserPurchasePaperAsync(CurrentUserId, id, cancellationToken);
        var settings = await marketplace.GetSettingsAsync(cancellationToken);

        var vm = new OrderCheckoutViewModel
        {
            PaperId = paper.Id,
            Title = paper.Title,
            Authors = paper.Authors,
            Category = paper.Category,
            Price = paper.IsPaid ? paper.Price : 0m,
            PlatformCommissionRate = settings.PlatformCommissionRate,
            CanPurchase = canPurchase,
            BlockedReason = blockedReason
        };

        return View(vm);
    }

    /// <summary>
    /// Free Manuscript Instant Acquisition (Zero-cost completed order).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcquireFree(int paperId, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, cancellationToken);
        if (paper is null || paper.IsPaid && paper.Price > 0m)
        {
            TempData["Error"] = "This manuscript requires a paid order.";
            return RedirectToAction(nameof(Details), new { id = paperId });
        }

        var (success, order, error) = await marketplace.CreateOrderAsync(CurrentUserId, paperId, "FreeAccess", cancellationToken);
        if (!success)
        {
            TempData["Error"] = error ?? "Could not acquire manuscript.";
            return RedirectToAction(nameof(Details), new { id = paperId });
        }

        TempData["Success"] = $"Access to '{paper.Title}' granted. You can read online or download now.";
        return RedirectToAction(nameof(MyPurchases));
    }

    /// <summary>
    /// User's library of acquired research papers (both free and paid).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MyPurchases(CancellationToken cancellationToken)
    {
        var orders = await db.PaperOrders.AsNoTracking()
            .Include(o => o.Paper)
            .Where(o => o.BuyerUserId == CurrentUserId && o.PaymentStatus == PaperPaymentStatus.Completed)
            .OrderByDescending(o => o.OrderDateUtc)
            .Select(o => new PurchasedPaperItemViewModel
            {
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                PaperId = o.PaperId,
                Title = o.Paper.Title,
                Authors = o.Paper.Authors,
                Category = o.Paper.Category,
                PricePaid = o.Amount,
                OrderDateUtc = o.OrderDateUtc,
                PaymentMethod = o.PaymentMethod,
                PageCount = o.Paper.PageCount
            })
            .ToListAsync(cancellationToken);

        return View(new MyPurchasesViewModel { Purchases = orders });
    }

    /// <summary>
    /// Authorized PDF Streaming with HTTP 206 Partial Content Range Support.
    /// Strictly verifies user ownership / free access before streaming.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Stream(int id, CancellationToken cancellationToken)
    {
        var canAccess = await marketplace.CanUserAccessPaperAsync(CurrentUserId, id, IsAdminOrStaff, cancellationToken);
        if (!canAccess)
        {
            return Forbid();
        }

        var paper = await db.ResearchPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        var fullPath = storage.GetVerifiedPaperPath(paper.StorageFileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("Manuscript document file not found in storage.");
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{SanitizeFileName(paper.OriginalFileName)}\"";

        return PhysicalFile(fullPath, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>
    /// Authorized PDF Download.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var canAccess = await marketplace.CanUserAccessPaperAsync(CurrentUserId, id, IsAdminOrStaff, cancellationToken);
        if (!canAccess)
        {
            return Forbid();
        }

        var paper = await db.ResearchPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        var fullPath = storage.GetVerifiedPaperPath(paper.StorageFileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("Manuscript document file not found in storage.");
        }

        paper.DownloadCount++;
        await db.SaveChangesAsync(cancellationToken);

        return PhysicalFile(fullPath, "application/pdf", SanitizeFileName(paper.OriginalFileName));
    }

    /// <summary>
    /// Copyright Infringement Report Form (DMCA Notice).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ReportCopyright(int id, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        var vm = new CopyrightReportInputModel
        {
            PaperId = paper.Id,
            PaperTitle = paper.Title
        };

        return View(vm);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportCopyright(CopyrightReportInputModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var paper = await db.ResearchPapers.FirstOrDefaultAsync(p => p.Id == model.PaperId, cancellationToken);
        if (paper is null) return NotFound();

        var report = new PaperCopyrightReport
        {
            PaperId = model.PaperId,
            ReporterUserId = User.Identity?.IsAuthenticated == true ? CurrentUserId : null,
            ReporterName = model.ReporterName.Trim(),
            ReporterEmail = model.ReporterEmail.Trim(),
            InfringedWorkTitle = model.InfringedWorkTitle.Trim(),
            InfringementDescription = model.InfringementDescription.Trim(),
            GoodFaithDeclaration = model.GoodFaithDeclaration,
            Status = CopyrightReportStatus.UnderReview,
            ReportedAtUtc = DateTime.UtcNow
        };

        db.PaperCopyrightReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Your copyright infringement notification has been submitted to the editorial review board. Case reference #" + report.Id;
        return RedirectToAction(nameof(Details), new { id = model.PaperId });
    }

    private static string SanitizeFileName(string fileName)
    {
        var clean = Path.GetFileName(fileName);
        return Regex.Replace(clean, @"[^a-zA-Z0-9_\-\.]", "_");
    }
}
