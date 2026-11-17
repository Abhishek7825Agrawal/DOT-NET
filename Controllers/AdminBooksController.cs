using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminBooksController(BookService books, CategoryService categories) : Controller
{
    public async Task<IActionResult> Index(string? q, string? category, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 15;
        var query = books.Search(q, category, includeInactive: true);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        ViewBag.Query = q;
        ViewBag.Category = category;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Categories = (await categories.GetAllAsync(cancellationToken)).Select(c => c.Name).ToList();

        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken));
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulateCategoriesAsync(cancellationToken);
        return View("Form", new BookFormViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(BookFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(cancellationToken);
            return View("Form", model);
        }

        var result = await books.CreateAsync(Map(model), cancellationToken);
        if (!result.Success)
        {
            var key = result.Error!.Contains("ISBN", StringComparison.OrdinalIgnoreCase) ? nameof(model.Isbn) : string.Empty;
            ModelState.AddModelError(key, result.Error!);
            await PopulateCategoriesAsync(cancellationToken);
            return View("Form", model);
        }

        TempData["Success"] = "Book added successfully to the catalogue.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var book = await books.GetAsync(id, cancellationToken);
        if (book is null) return NotFound();

        await PopulateCategoriesAsync(cancellationToken);
        return View("Form", new BookFormViewModel
        {
            Id = book.Id,
            Title = book.Title,
            Author = book.Author,
            Isbn = book.Isbn,
            Category = book.Category,
            Publisher = book.Publisher,
            PublicationYear = book.PublicationYear,
            Description = book.Description,
            TotalQuantity = book.TotalQuantity,
            IsActive = book.IsActive,
            RowVersion = Convert.ToBase64String(book.RowVersion)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(BookFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(cancellationToken);
            return View("Form", model);
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(model.RowVersion ?? string.Empty);
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "This edit form is invalid or expired. Reload the book and try again.");
            await PopulateCategoriesAsync(cancellationToken);
            return View("Form", model);
        }
        if (rowVersion.Length is not (8 or 16))
        {
            ModelState.AddModelError(string.Empty, "This edit form is invalid. Reload the book and try again.");
            await PopulateCategoriesAsync(cancellationToken);
            return View("Form", model);
        }

        var input = Map(model);
        input.RowVersion = rowVersion;
        var result = await books.UpdateAsync(input, cancellationToken);
        if (!result.Success)
        {
            var key = result.Error!.Contains("ISBN", StringComparison.OrdinalIgnoreCase) ? nameof(model.Isbn) : string.Empty;
            ModelState.AddModelError(key, result.Error!);
            await PopulateCategoriesAsync(cancellationToken);
            return View("Form", model);
        }

        TempData["Success"] = "Book details updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await books.DeactivateAsync(id, cancellationToken);
        if (!result.Success)
        {
            if (result.Error == "Book not found.") return NotFound();
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "Book archived or permanently removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Restore(int id, CancellationToken cancellationToken)
    {
        var result = await books.RestoreAsync(id, cancellationToken);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Success"] = "Book restored and made active in the catalogue.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateCategoriesAsync(CancellationToken cancellationToken)
    {
        var list = await categories.GetAllAsync(cancellationToken);
        ViewBag.CategoryOptions = list.Select(c => new SelectListItem { Value = c.Name, Text = c.Name }).ToList();
    }

    private static Book Map(BookFormViewModel model) => new()
    {
        Id = model.Id,
        Title = model.Title,
        Author = model.Author,
        Isbn = model.Isbn,
        Category = model.Category,
        Publisher = model.Publisher,
        PublicationYear = model.PublicationYear,
        Description = model.Description,
        TotalQuantity = model.TotalQuantity,
        IsActive = model.IsActive
    };
}
