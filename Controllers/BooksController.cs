using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[AllowAnonymous]
public sealed class BooksController(BookService books, CategoryService categories) : Controller
{
    public async Task<IActionResult> Index(string? q, string? category, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 12;
        var query = books.Search(q, category);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        ViewBag.Query = q;
        ViewBag.Category = category;
        ViewBag.Categories = (await categories.GetAllAsync(cancellationToken)).Select(c => c.Name).ToList();
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;

        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken));
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var book = await books.GetAsync(id, cancellationToken);
        return book is null || !book.IsActive ? NotFound() : View(book);
    }
}
