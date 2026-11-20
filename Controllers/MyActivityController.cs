using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Member,Staff")]
public sealed class MyActivityController(ApplicationDbContext db, LibraryCalendar calendar) : Controller
{
    public async Task<IActionResult> Index(string? q, DateTime? fromDate, DateTime? toDate, int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.UserActivityLogs.AsNoTracking().Where(log => log.UserId == userId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(log => log.Description.Contains(term) || log.ActivityType.Contains(term)
                || log.Category.Contains(term) || (log.EntityReference != null && log.EntityReference.Contains(term)));
        }
        if (fromDate.HasValue) query = query.Where(log => log.CreatedAtUtc >= calendar.ToUtc(fromDate.Value.Date));
        if (toDate.HasValue) query = query.Where(log => log.CreatedAtUtc < calendar.ToUtc(toDate.Value.Date.AddDays(1)));

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var rows = await query.OrderByDescending(log => log.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(log => new ActivityLogRowViewModel
            {
                Id = log.Id, UserId = log.UserId, UserRole = log.UserRole,
                ActivityType = log.ActivityType, Category = log.Category,
                Description = log.Description, Status = log.Status,
                EntityType = log.EntityType, EntityReference = log.EntityReference,
                CorrelationId = log.CorrelationId, LocalTimestamp = log.CreatedAtUtc
            }).ToListAsync(cancellationToken);
        foreach (var row in rows) row.LocalTimestamp = calendar.ToLocal(DateTime.SpecifyKind(row.LocalTimestamp, DateTimeKind.Utc)).DateTime;

        return View(new UserActivityPageViewModel
        {
            Items = rows, Query = q, FromDate = fromDate, ToDate = toDate,
            Page = page, TotalPages = totalPages
        });
    }
}
