using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[AllowAnonymous]
public sealed class BooksController(
    ApplicationDbContext db,
    BookService books,
    CategoryService categories) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        string? category,
        string? availability,
        string? sort,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 12;

        var totalCatalogueCount = await db.Books.AsNoTracking().CountAsync(b => b.IsActive, cancellationToken);

        var query = db.Books.AsNoTracking().Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(b => b.Title.Contains(term)
                || b.Author.Contains(term)
                || (b.Isbn != null && b.Isbn.Contains(term))
                || b.Category.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(b => b.Category == category);
        }

        if (string.Equals(availability, "available", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(b => b.AvailableQuantity > 0);
        }
        else if (string.Equals(availability, "issued", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(b => b.AvailableQuantity == 0);
        }

        var normalizedSort = sort?.ToLowerInvariant() switch
        {
            "newest" => "newest",
            "popular" => "popular",
            _ => "title"
        };

        query = normalizedSort switch
        {
            "newest" => query.OrderByDescending(b => b.AddedAtUtc).ThenBy(b => b.Title),
            "popular" => query.OrderByDescending(b => b.IssueRecords.Count).ThenBy(b => b.Title),
            _ => query.OrderBy(b => b.Title)
        };

        var filteredCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var pagedBooks = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        // Featured Books section: Show top 4 popular books if real data supports it, on page 1 with no search/category filter
        List<Book> featuredBooks = [];
        if (page == 1 && string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(availability))
        {
            featuredBooks = await db.Books.AsNoTracking()
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.IssueRecords.Count)
                .ThenByDescending(b => b.AddedAtUtc)
                .Take(4)
                .ToListAsync(cancellationToken);
        }

        ViewBag.Query = q;
        ViewBag.Category = category;
        ViewBag.Availability = availability;
        ViewBag.Sort = normalizedSort;
        ViewBag.TotalCatalogueCount = totalCatalogueCount;
        ViewBag.FilteredCount = filteredCount;
        ViewBag.FeaturedBooks = featuredBooks;
        ViewBag.Categories = (await categories.GetAllAsync(cancellationToken)).Select(c => c.Name).ToList();
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;

        return View(pagedBooks);
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var book = await books.GetAsync(id, cancellationToken);
        return book is null || !book.IsActive ? NotFound() : View(book);
    }
}
