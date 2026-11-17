using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class CategoryService(ApplicationDbContext db)
{
    public Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<Category?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<List<CategoryBookCountDto>> GetCategoriesWithCountsAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var bookCounts = await db.Books.AsNoTracking()
            .GroupBy(b => b.Category)
            .Select(g => new { Category = g.Key, Count = g.Count(), ActiveCount = g.Count(b => b.IsActive) })
            .ToListAsync(cancellationToken);

        var lookup = bookCounts.ToDictionary(item => item.Category, StringComparer.OrdinalIgnoreCase);

        return categories.Select(c => new CategoryBookCountDto(
            c.Id,
            c.Name,
            c.Description,
            lookup.TryGetValue(c.Name, out var info) ? info.Count : 0,
            lookup.TryGetValue(c.Name, out var activeInfo) ? activeInfo.ActiveCount : 0
        )).ToList();
    }

    public async Task<(bool Success, string? Error)> CreateAsync(Category category, CancellationToken cancellationToken = default)
    {
        var name = category.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Category name is required.");

        if (await db.Categories.AnyAsync(c => c.Name == name, cancellationToken))
            return (false, "A category with this name already exists.");

        category.Name = name;
        category.Description = string.IsNullOrWhiteSpace(category.Description) ? null : category.Description.Trim();
        category.CreatedAtUtc = DateTime.UtcNow;

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(Category input, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == input.Id, cancellationToken);
        if (category is null) return (false, "Category not found.");

        var name = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Category name is required.");

        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != input.Id, cancellationToken))
            return (false, "Another category with this name already exists.");

        // If category name is changing, sync with active book records
        if (!string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            var oldName = category.Name;
            var affectedBooks = await db.Books.Where(b => b.Category == oldName).ToListAsync(cancellationToken);
            foreach (var b in affectedBooks)
            {
                b.Category = name;
            }
        }

        category.Name = name;
        category.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null) return (false, "Category not found.");

        var hasBooks = await db.Books.AnyAsync(b => b.Category == category.Name, cancellationToken);
        if (hasBooks)
            return (false, "Cannot delete category because books are currently assigned to it. Reassign or remove books first.");

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public sealed record CategoryBookCountDto(int Id, string Name, string? Description, int TotalBooks, int ActiveBooks);

