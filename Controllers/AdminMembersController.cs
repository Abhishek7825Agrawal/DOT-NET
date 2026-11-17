using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminMembersController(MemberService members) : Controller
{
    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;
        var query = members.Search(q);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.Query = q;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, bool active, CancellationToken cancellationToken)
    {
        if (!await members.SetActiveAsync(id, active, cancellationToken)) return NotFound();
        TempData["Success"] = active ? "Member account activated." : "Member account deactivated.";
        return RedirectToAction(nameof(Index));
    }
}
