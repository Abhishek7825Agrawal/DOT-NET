using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminCirculationController(
    ApplicationDbContext db,
    CirculationService circulation,
    LibraryCalendar calendar) : Controller
{
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

    public async Task<IActionResult> Issue(CancellationToken cancellationToken)
    {
        await PopulateIssueListsAsync(cancellationToken);
        return View(new IssueBookViewModel());
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
            ModelState.AddModelError(string.Empty, result.Error!);
            await PopulateIssueListsAsync(cancellationToken);
            return View(model);
        }
        TempData["Success"] = "Book issued successfully and due date recorded.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Return(int id, string? remarks, CancellationToken cancellationToken)
    {
        var result = await circulation.ReturnAsync(id, remarks, cancellationToken);
        if (!result.Success)
            TempData["Error"] = result.Error;
        else
            TempData["Success"] = "Book returned and inventory updated. Any overdue fine has been recorded.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkLost(int id, string? remarks, CancellationToken cancellationToken)
    {
        var result = await circulation.MarkAsLostAsync(id, remarks, cancellationToken);
        if (!result.Success)
            TempData["Error"] = result.Error;
        else
            TempData["Success"] = "Book marked as Lost. Inventory and replacement penalty fee recorded.";
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
            TempData["Error"] = result.Error;
        else
            TempData["Success"] = "Fine payment recorded successfully.";
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
