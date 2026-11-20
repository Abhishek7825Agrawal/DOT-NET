using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin,Staff")]
public sealed class AdminMarketplaceController(
    ApplicationDbContext db,
    MarketplaceService marketplace,
    PaperStorageService storage,
    ActivityLogService activityLogs) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// Editorial Moderation Queue: Pending reviews, approved papers, copyright reports, and commission settings.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? tab = "pending", CancellationToken cancellationToken = default)
    {
        ViewBag.ActiveTab = tab;

        var pending = await db.ResearchPapers.AsNoTracking()
            .Include(p => p.SubmitterUser)
            .Where(p => p.Status == PaperStatus.PendingReview)
            .OrderByDescending(p => p.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        var approved = await db.ResearchPapers.AsNoTracking()
            .Include(p => p.SubmitterUser)
            .Where(p => p.Status == PaperStatus.Approved)
            .OrderByDescending(p => p.SubmittedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var copyrightReports = await db.PaperCopyrightReports.AsNoTracking()
            .Include(r => r.Paper)
            .OrderByDescending(r => r.ReportedAtUtc)
            .ToListAsync(cancellationToken);

        var settings = await marketplace.GetSettingsAsync(cancellationToken);

        var vm = new AdminModerationViewModel
        {
            PendingPapers = pending,
            ApprovedPapers = approved,
            CopyrightReports = copyrightReports,
            Settings = settings
        };

        return View(vm);
    }

    /// <summary>
    /// Detailed Manuscript Inspection View for Editorial Review.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Review(int id, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers
            .Include(p => p.SubmitterUser)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (paper is null) return NotFound("Manuscript not found.");

        var versions = await db.PaperVersions.AsNoTracking()
            .Where(v => v.PaperId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        var vm = new PaperReviewViewModel
        {
            Paper = paper,
            Versions = versions
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        paper.Status = PaperStatus.Approved;
        paper.ReviewedAtUtc = DateTime.UtcNow;
        paper.ReviewedByUserId = CurrentUserId;
        paper.RejectionReason = null;

        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "ResearchPaperApproved", "MarketplaceModeration",
            "You approved a research paper submission.", entityType: "ResearchPaper", entityReference: id.ToString(),
            securityEvent: true, cancellationToken: cancellationToken);
        TempData["Success"] = $"Manuscript '{paper.Title}' approved and published to the Research Paper Marketplace.";
        return RedirectToAction(nameof(Index), new { tab = "pending" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "An editorial reason is required when rejecting a manuscript.";
            return RedirectToAction(nameof(Review), new { id });
        }

        var paper = await db.ResearchPapers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        paper.Status = PaperStatus.Rejected;
        paper.RejectionReason = reason.Trim();
        paper.ReviewedAtUtc = DateTime.UtcNow;
        paper.ReviewedByUserId = CurrentUserId;

        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "ResearchPaperRejected", "MarketplaceModeration",
            "You rejected a research paper submission.", entityType: "ResearchPaper", entityReference: id.ToString(),
            securityEvent: true, severity: "Warning", cancellationToken: cancellationToken);
        TempData["Success"] = $"Manuscript '{paper.Title}' rejected with editorial feedback.";
        return RedirectToAction(nameof(Index), new { tab = "pending" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestCorrections(int id, string feedback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            TempData["Error"] = "Constructive feedback is required when requesting revisions.";
            return RedirectToAction(nameof(Review), new { id });
        }

        var paper = await db.ResearchPapers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        paper.Status = PaperStatus.CorrectionRequested;
        paper.EditorialFeedback = feedback.Trim();
        paper.ReviewedAtUtc = DateTime.UtcNow;
        paper.ReviewedByUserId = CurrentUserId;

        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "ResearchPaperCorrectionsRequested", "MarketplaceModeration",
            "You requested revisions for a research paper.", entityType: "ResearchPaper", entityReference: id.ToString(),
            securityEvent: true, cancellationToken: cancellationToken);
        TempData["Success"] = $"Revisions requested for '{paper.Title}'. Author has been notified.";
        return RedirectToAction(nameof(Index), new { tab = "pending" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(int id, string? reason, CancellationToken cancellationToken)
    {
        var paper = await db.ResearchPapers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper is null) return NotFound();

        paper.Status = PaperStatus.Suspended;
        paper.EditorialFeedback = string.IsNullOrWhiteSpace(reason) ? "Suspended by administration." : reason.Trim();
        paper.ReviewedAtUtc = DateTime.UtcNow;
        paper.ReviewedByUserId = CurrentUserId;

        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "ResearchPaperSuspended", "MarketplaceModeration",
            "You suspended a research paper from the public catalogue.", entityType: "ResearchPaper", entityReference: id.ToString(),
            securityEvent: true, severity: "Warning", cancellationToken: cancellationToken);
        TempData["Success"] = $"Manuscript '{paper.Title}' has been suspended from the public catalogue.";
        return RedirectToAction(nameof(Index), new { tab = "approved" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveCopyrightReport(int reportId, string decision, string? remarks, CancellationToken cancellationToken)
    {
        var report = await db.PaperCopyrightReports
            .Include(r => r.Paper)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null) return NotFound();

        report.AdminRemarks = remarks?.Trim();
        report.ResolvedAtUtc = DateTime.UtcNow;
        report.ResolvedByUserId = CurrentUserId;

        if (string.Equals(decision, "uphold", StringComparison.OrdinalIgnoreCase))
        {
            report.Status = CopyrightReportStatus.UpheldSuspended;
            report.Paper.Status = PaperStatus.Suspended;
            report.Paper.EditorialFeedback = $"Suspended due to upheld copyright claim #{report.Id}: {report.InfringedWorkTitle}";
            TempData["Success"] = $"Copyright claim #{report.Id} upheld. Manuscript suspended from marketplace.";
        }
        else
        {
            report.Status = CopyrightReportStatus.Dismissed;
            TempData["Success"] = $"Copyright claim #{report.Id} dismissed.";
        }

        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "CopyrightReportResolved", "MarketplaceModeration",
            "You resolved a copyright report.", entityType: "CopyrightReport", entityReference: report.Id.ToString(),
            securityEvent: true, severity: string.Equals(decision, "uphold", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Information",
            cancellationToken: cancellationToken);
        return RedirectToAction(nameof(Index), new { tab = "reports" });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSettings(MarketplaceSettings model, CancellationToken cancellationToken)
    {
        if (model.PlatformCommissionRate < 0m || model.PlatformCommissionRate > 50m)
        {
            TempData["Error"] = "Commission rate must be between 0% and 50%.";
            return RedirectToAction(nameof(Index), new { tab = "settings" });
        }

        var settings = await marketplace.GetSettingsAsync(cancellationToken);
        settings.PlatformCommissionRate = model.PlatformCommissionRate;
        settings.RequireAdminApproval = model.RequireAdminApproval;
        settings.MinimumListingPrice = Math.Max(0m, model.MinimumListingPrice);
        settings.MaximumListingPrice = Math.Max(settings.MinimumListingPrice, model.MaximumListingPrice);
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = CurrentUserId;

        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "MarketplaceSettingsUpdated", "Configuration",
            "You updated marketplace approval, pricing or commission settings.", entityType: "MarketplaceSettings",
            securityEvent: true, cancellationToken: cancellationToken);
        TempData["Success"] = "Marketplace policies and platform commission updated.";
        return RedirectToAction(nameof(Index), new { tab = "settings" });
    }
}
