using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminActivityController(ApplicationDbContext db, LibraryCalendar calendar) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? role, string? category, string? activityType,
        string? status, string? entity, DateTime? fromDate, DateTime? toDate, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var vm = await BuildPageAsync(q, role, category, activityType, status, entity, fromDate, toDate, page, cancellationToken);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? q, string? role, string? category, string? activityType,
        string? status, string? entity, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var query = Filter(q, role, category, activityType, status, entity, fromDate, toDate);
        var logs = await query.OrderByDescending(log => log.CreatedAtUtc).Take(10000).ToListAsync(cancellationToken);
        var userIds = logs.Select(log => log.UserId).Distinct().ToArray();
        var emails = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email ?? string.Empty, cancellationToken);
        var csv = new StringBuilder("Timestamp,User,Role,Category,Activity,Status,Description,Entity,Reference,CorrelationId\r\n");
        foreach (var log in logs)
        {
            emails.TryGetValue(log.UserId, out var email);
            AppendCsv(csv, calendar.ToLocal(DateTime.SpecifyKind(log.CreatedAtUtc, DateTimeKind.Utc)).ToString("yyyy-MM-dd HH:mm:ss zzz"),
                email, log.UserRole, log.Category, log.ActivityType, log.Status, log.Description,
                log.EntityType, log.EntityReference, log.CorrelationId);
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv", $"shelfwise-activity-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<AdminActivityPageViewModel> BuildPageAsync(string? q, string? role, string? category,
        string? activityType, string? status, string? entity, DateTime? fromDate, DateTime? toDate, int page, CancellationToken cancellationToken)
    {
        const int pageSize = 30;
        var query = Filter(q, role, category, activityType, status, entity, fromDate, toDate);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var logs = await query.OrderByDescending(log => log.CreatedAtUtc).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(cancellationToken);
        var ids = logs.Select(log => log.UserId).Distinct().ToArray();
        var emails = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);
        var rows = logs.Select(log => new ActivityLogRowViewModel
        {
            Id = log.Id, UserId = log.UserId, UserEmail = emails.GetValueOrDefault(log.UserId),
            UserRole = log.UserRole, ActivityType = log.ActivityType, Category = log.Category,
            Description = log.Description, Status = log.Status, EntityType = log.EntityType,
            EntityReference = log.EntityReference, CorrelationId = log.CorrelationId,
            LocalTimestamp = calendar.ToLocal(DateTime.SpecifyKind(log.CreatedAtUtc, DateTimeKind.Utc)).DateTime
        }).ToList();
        return new AdminActivityPageViewModel
        {
            Items = rows, Query = q, RoleFilter = role, CategoryFilter = category,
            ActivityTypeFilter = activityType, StatusFilter = status, EntityFilter = entity, FromDate = fromDate,
            ToDate = toDate, Page = page, TotalPages = totalPages
        };
    }

    private IQueryable<LibraryManagementSystem.Models.UserActivityLog> Filter(string? q, string? role, string? category,
        string? activityType, string? status, string? entity, DateTime? fromDate, DateTime? toDate)
    {
        var query = db.UserActivityLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var userIds = db.Users.Where(user => user.Email != null && user.Email.Contains(term)).Select(user => user.Id);
            query = query.Where(log => log.Description.Contains(term) || log.ActivityType.Contains(term)
                || log.UserId.Contains(term) || userIds.Contains(log.UserId));
        }
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(log => log.UserRole.Contains(role));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(log => log.Category == category);
        if (!string.IsNullOrWhiteSpace(activityType)) query = query.Where(log => log.ActivityType == activityType);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(log => log.Status == status);
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(log => log.EntityType == entity || log.EntityReference == entity);
        if (fromDate.HasValue) query = query.Where(log => log.CreatedAtUtc >= calendar.ToUtc(fromDate.Value.Date));
        if (toDate.HasValue) query = query.Where(log => log.CreatedAtUtc < calendar.ToUtc(toDate.Value.Date.AddDays(1)));
        return query;
    }

    private static void AppendCsv(StringBuilder builder, params string?[] values)
    {
        builder.AppendLine(string.Join(',', values.Select(value =>
        {
            var safe = value ?? string.Empty;
            if (safe.StartsWith('=') || safe.StartsWith('+') || safe.StartsWith('-') || safe.StartsWith('@')) safe = "'" + safe;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        })));
    }
}
