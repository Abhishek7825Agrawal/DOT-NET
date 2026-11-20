using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin,Staff")]
public sealed class AdminCirculationController(
    ApplicationDbContext db,
    CirculationService circulation,
    LibraryCalendar calendar,
    ActivityLogService activityLogs) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    public async Task<IActionResult> Index(
        string? q,
        bool overdue = false,
        IssueStatus? status = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        var query = circulation.Search(q, overdue, status);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        ViewBag.Query = q;
        ViewBag.Overdue = overdue;
        ViewBag.Status = status;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Today = calendar.Today;

        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken));
    }

    public async Task<IActionResult> Overdue(string? q, int page = 1, CancellationToken cancellationToken = default)
    {
        return await Index(q, overdue: true, status: null, page, cancellationToken);
    }

    public async Task<IActionResult> Payments(string? q, CancellationToken cancellationToken = default)
    {
        ViewBag.Query = q;
        var history = await circulation.GetPaymentHistoryAsync(q, 100, cancellationToken);
        return View(history);
    }

    public async Task<IActionResult> Issue(int? bookId, CancellationToken cancellationToken)
    {
        await PopulateIssueListsAsync(cancellationToken);
        return View(new IssueBookViewModel { BookId = bookId ?? 0 });
    }

    [HttpPost]
    public async Task<IActionResult> Issue(IssueBookViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateIssueListsAsync(cancellationToken);
            return View(model);
        }
        var result = await circulation.IssueAsync(model.MemberId, model.BookId, cancellationToken);
        if (!result.Success)
        {
            await activityLogs.RecordSecurityAsync(CurrentUserId, "BookIssueFailed", "Circulation",
                "A book issue operation failed validation or persistence.", "Failure", "Warning", "Book", model.BookId.ToString(), cancellationToken);
            ModelState.AddModelError(string.Empty, result.Error!);
            await PopulateIssueListsAsync(cancellationToken);
            return View(model);
        }
        var issue = await db.IssueRecords.AsNoTracking().Include(item => item.Book).Include(item => item.Member)
            .OrderByDescending(item => item.Id).FirstAsync(item => item.MemberId == model.MemberId && item.BookId == model.BookId
                && item.Status == IssueStatus.Active, cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "BookIssued", "Circulation",
            $"You issued '{issue.Book.Title}' to {issue.Member.FullName}.", entityType: "Issue", entityReference: issue.Id.ToString(),
            securityEvent: true, cancellationToken: cancellationToken);
        await activityLogs.RecordAsync(issue.Member.UserId, "BookBorrowed", "Circulation",
            $"You borrowed '{issue.Book.Title}'.", entityType: "Book", entityReference: issue.BookId.ToString(),
            cancellationToken: cancellationToken, userRoleOverride: "Member");
        TempData["Success"] = "Book issued successfully and due date recorded.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Return(int id, string? remarks, CancellationToken cancellationToken)
    {
        var result = await circulation.ReturnAsync(id, remarks, cancellationToken);
        if (!result.Success)
        {
            await activityLogs.RecordSecurityAsync(CurrentUserId, "BookReturnFailed", "Circulation",
                "A book return operation failed validation or persistence.", "Failure", "Warning", "Issue", id.ToString(), cancellationToken);
            TempData["Error"] = result.Error;
        }
        else
        {
            var issue = await db.IssueRecords.AsNoTracking().Include(item => item.Book).Include(item => item.Member)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (issue is not null)
            {
                await activityLogs.RecordAsync(CurrentUserId, "BookReturned", "Circulation",
                    $"You recorded the return of '{issue.Book.Title}' from {issue.Member.FullName}.", entityType: "Issue", entityReference: id.ToString(),
                    securityEvent: true, cancellationToken: cancellationToken);
                await activityLogs.RecordAsync(issue.Member.UserId, "BookReturned", "Circulation",
                    $"You returned '{issue.Book.Title}'.", entityType: "Book", entityReference: issue.BookId.ToString(),
                    cancellationToken: cancellationToken, userRoleOverride: "Member");
            }
            TempData["Success"] = "Book returned and inventory updated. Any overdue fine has been recorded.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkLost(int id, string? remarks, CancellationToken cancellationToken)
    {
        var result = await circulation.MarkAsLostAsync(id, remarks, cancellationToken);
        if (!result.Success)
        {
            await activityLogs.RecordSecurityAsync(CurrentUserId, "BookLostUpdateFailed", "Circulation",
                "A lost-book operation failed validation or persistence.", "Failure", "Warning", "Issue", id.ToString(), cancellationToken);
            TempData["Error"] = result.Error;
        }
        else
        {
            var issue = await db.IssueRecords.AsNoTracking().Include(item => item.Book).Include(item => item.Member)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (issue is not null)
            {
                await activityLogs.RecordAsync(CurrentUserId, "BookMarkedLost", "Circulation",
                    $"You marked '{issue.Book.Title}' as lost for {issue.Member.FullName}.", entityType: "Issue", entityReference: id.ToString(),
                    securityEvent: true, severity: "Warning", cancellationToken: cancellationToken);
                await activityLogs.RecordAsync(issue.Member.UserId, "BookMarkedLost", "Circulation",
                    $"A borrowed book was recorded as lost.", entityType: "Book", entityReference: issue.BookId.ToString(),
                    cancellationToken: cancellationToken, userRoleOverride: "Member");
            }
            TempData["Success"] = "Book marked as Lost. Inventory and replacement penalty fee recorded.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> PayFine(
        int id,
        FinePaymentViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter a valid fine payment amount and method.";
            return RedirectToAction(nameof(Index));
        }
        var result = await circulation.RecordFinePaymentAsync(
            id, model.Amount, model.PaymentMethod, model.ReferenceNumber, cancellationToken);
        if (!result.Success)
        {
            await activityLogs.RecordSecurityAsync(CurrentUserId, "FinePaymentFailed", "Finance",
                "A fine payment operation failed validation or persistence.", "Failure", "Warning", "Issue", id.ToString(), cancellationToken);
            TempData["Error"] = result.Error;
        }
        else
        {
            var issue = await db.IssueRecords.AsNoTracking().Include(item => item.Member).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            await activityLogs.RecordAsync(CurrentUserId, "FinePaymentRecorded", "Finance",
                $"You recorded a fine payment of {model.Amount:C}.", entityType: "Issue", entityReference: id.ToString(),
                securityEvent: true, cancellationToken: cancellationToken);
            if (issue is not null)
                await activityLogs.RecordAsync(issue.Member.UserId, "FinePaid", "Finance",
                    $"A fine payment of {model.Amount:C} was recorded for your account.", entityType: "Issue", entityReference: id.ToString(),
                    cancellationToken: cancellationToken, userRoleOverride: "Member");
            TempData["Success"] = "Fine payment recorded successfully.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateIssueListsAsync(CancellationToken cancellationToken)
    {
        ViewBag.Members = new SelectList(await db.Members.AsNoTracking()
            .Where(member => member.IsActive).OrderBy(member => member.FullName)
            .Select(member => new { member.Id, Name = member.FullName + " (" + member.User.Email + ")" })
            .ToListAsync(cancellationToken), "Id", "Name");

        ViewBag.Books = new SelectList(await db.Books.AsNoTracking()
            .Where(book => book.IsActive && book.AvailableQuantity > 0).OrderBy(book => book.Title)
            .Select(book => new { book.Id, Name = book.Title + " — " + book.Author + " (" + book.AvailableQuantity + " available)" })
            .ToListAsync(cancellationToken), "Id", "Name");
    }
}
