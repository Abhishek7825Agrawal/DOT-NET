using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize]
public sealed class AuthorMarketplaceController(
    ApplicationDbContext db,
    MarketplaceService marketplace,
    PaperStorageService storage,
    ActivityLogService activityLogs) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// Author Research Desk: Submissions, publication statuses, sales & revenue metrics.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AuthorDesk(CancellationToken cancellationToken)
    {
        var metrics = await marketplace.GetAuthorMetricsAsync(CurrentUserId, cancellationToken);

        var papers = await db.ResearchPapers.AsNoTracking()
            .Where(p => p.SubmitterUserId == CurrentUserId)
            .OrderByDescending(p => p.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        var vm = new AuthorDeskViewModel
        {
            Metrics = metrics,
            MyPapers = papers
        };

        return View(vm);
    }

    /// <summary>
    /// Submit New Research Paper Manuscript.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Submit(CancellationToken cancellationToken)
    {
        var settings = await marketplace.GetSettingsAsync(cancellationToken);
        ViewBag.CommissionRate = settings.PlatformCommissionRate;
        return View(new PaperSubmitViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(PaperSubmitViewModel model, CancellationToken cancellationToken)
    {
        var settings = await marketplace.GetSettingsAsync(cancellationToken);
        ViewBag.CommissionRate = settings.PlatformCommissionRate;

        if (!model.CopyrightAccepted)
        {
            ModelState.AddModelError(nameof(model.CopyrightAccepted), "You must warrant copyright ownership and accept the distribution terms.");
        }

        if (model.IsPaid && model.Price <= 0m)
        {
            ModelState.AddModelError(nameof(model.Price), "Paid listings must have a price greater than ₹0.00.");
        }

        if (model.PdfFile is null || model.PdfFile.Length == 0)
        {
            ModelState.AddModelError(nameof(model.PdfFile), "A valid research manuscript PDF file is required.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var pdfMeta = await storage.SavePaperAsync(model.PdfFile!, cancellationToken);

            var paper = new ResearchPaper
            {
                Title = model.Title.Trim(),
                Abstract = model.Abstract.Trim(),
                Authors = model.Authors.Trim(),
                Affiliation = model.Affiliation.Trim(),
                Category = model.Category.Trim(),
                Keywords = model.Keywords.Trim(),
                Doi = string.IsNullOrWhiteSpace(model.Doi) ? null : model.Doi.Trim(),
                PublicationYear = model.PublicationYear,
                IsPaid = model.IsPaid,
                Price = model.IsPaid ? model.Price : 0.00m,
                CopyrightAccepted = true,
                CopyrightAcceptedAtUtc = DateTime.UtcNow,
                SubmitterUserId = CurrentUserId
            };

            await marketplace.SubmitPaperAsync(paper, pdfMeta, cancellationToken);
            await activityLogs.RecordAsync(CurrentUserId, "ResearchPaperSubmitted", "Marketplace",
                $"You submitted '{paper.Title}' for review.", entityType: "ResearchPaper",
                entityReference: paper.Id.ToString(), securityEvent: true, cancellationToken: cancellationToken);

            TempData["Success"] = $"Manuscript '{paper.Title}' submitted successfully for editorial peer review.";
            return RedirectToAction(nameof(AuthorDesk));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Failed to process manuscript submission: {ex.Message}");
            return View(model);
        }
    }

    /// <summary>
    /// Author Correction / Re-submission form when manuscript is in CorrectionRequested or Draft status.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.SubmitterUserId == CurrentUserId, cancellationToken);

        if (paper is null)
        {
            return NotFound("Manuscript not found or permission denied.");
        }

        if (paper.Status != PaperStatus.CorrectionRequested && paper.Status != PaperStatus.Draft)
        {
            TempData["Error"] = "Only manuscripts in 'Correction Requested' or 'Draft' status can be edited.";
            return RedirectToAction(nameof(AuthorDesk));
        }

        var vm = new PaperEditViewModel
        {
            Id = paper.Id,
            Title = paper.Title,
            Abstract = paper.Abstract,
            Authors = paper.Authors,
            Affiliation = paper.Affiliation,
            Category = paper.Category,
            Keywords = paper.Keywords,
            CurrentVersion = paper.CurrentVersion,
            RejectionReason = paper.RejectionReason,
            EditorialFeedback = paper.EditorialFeedback
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PaperEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            PaperDocumentMetadata? newMeta = null;
            if (model.NewPdfFile is not null && model.NewPdfFile.Length > 0)
            {
                newMeta = await storage.SavePaperAsync(model.NewPdfFile, cancellationToken);
            }

            await marketplace.SubmitCorrectionAsync(
                model.Id,
                CurrentUserId,
                newMeta,
                model.Title,
                model.Abstract,
                model.Authors,
                model.Affiliation,
                model.Category,
                model.Keywords,
                model.ChangeSummary,
                cancellationToken);
            await activityLogs.RecordAsync(CurrentUserId, "ResearchPaperResubmitted", "Marketplace",
                $"You submitted revisions for '{model.Title}'.", entityType: "ResearchPaper",
                entityReference: model.Id.ToString(), securityEvent: true, cancellationToken: cancellationToken);

            TempData["Success"] = $"Revisions for '{model.Title}' submitted. Manuscript is back in editorial review.";
            return RedirectToAction(nameof(AuthorDesk));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Failed to submit revisions: {ex.Message}");
            return View(model);
        }
    }

    /// <summary>
    /// Itemized Sales Ledger for Author showing every individual transaction, commission deducted, and net credited.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SalesLedger(CancellationToken cancellationToken)
    {
        var authorPaperIds = await db.ResearchPapers.AsNoTracking()
            .Where(p => p.SubmitterUserId == CurrentUserId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var orders = await db.PaperOrders.AsNoTracking()
            .Include(o => o.Paper)
            .Include(o => o.BuyerUser)
            .Where(o => authorPaperIds.Contains(o.PaperId) && o.PaymentStatus == PaperPaymentStatus.Completed)
            .OrderByDescending(o => o.OrderDateUtc)
            .Select(o => new SalesLedgerItemViewModel
            {
                OrderNumber = o.OrderNumber,
                PaperId = o.PaperId,
                PaperTitle = o.Paper.Title,
                OrderDateUtc = o.OrderDateUtc,
                BuyerEmail = o.BuyerUser.Email ?? "Cardholder",
                GrossAmount = o.Amount,
                CommissionRate = o.PlatformCommissionRate,
                CommissionAmount = o.PlatformCommissionAmount,
                NetEarnings = o.AuthorEarningsAmount,
                PaymentStatus = o.PaymentStatus
            })
            .ToListAsync(cancellationToken);

        var vm = new SalesLedgerViewModel
        {
            Transactions = orders,
            TotalGross = orders.Sum(t => t.GrossAmount),
            TotalCommission = orders.Sum(t => t.CommissionAmount),
            TotalNet = orders.Sum(t => t.NetEarnings)
        };

        return View(vm);
    }
}
