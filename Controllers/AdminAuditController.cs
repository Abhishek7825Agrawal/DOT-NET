using System.Text;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminAuditController(ApplicationDbContext db, LibraryCalendar calendar) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? role, string? category, string? severity,
        string? outcome, string? entity, string? correlation, DateTime? fromDate, DateTime? toDate,
        int page = 1, CancellationToken cancellationToken = default)
    {
        var vm = await BuildPageAsync(q, role, category, severity, outcome, entity, correlation,
            fromDate, toDate, page, cancellationToken);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? q, string? role, string? category, string? severity,
        string? outcome, string? entity, string? correlation, DateTime? fromDate, DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        var logs = await Filter(q, role, category, severity, outcome, entity, correlation, fromDate, toDate)
            .OrderByDescending(log => log.CreatedAtUtc).Take(10000).ToListAsync(cancellationToken);
        var ids = logs.Where(log => log.UserId != null).Select(log => log.UserId!).Distinct().ToArray();
        var emails = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);
        var csv = new StringBuilder("Timestamp,User,Role,Severity,Category,Event,Outcome,Description,Entity,Reference,Path,CorrelationId\r\n");
        foreach (var log in logs)
        {
            emails.TryGetValue(log.UserId ?? string.Empty, out var email);
            AppendCsv(csv, calendar.ToLocal(DateTime.SpecifyKind(log.CreatedAtUtc, DateTimeKind.Utc)).ToString("yyyy-MM-dd HH:mm:ss zzz"),
                email, log.UserRole, log.Severity, log.Category, log.EventType, log.Outcome,
                log.Description, log.EntityType, log.EntityReference, log.RequestPath, log.CorrelationId);
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv", $"shelfwise-security-audit-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<SecurityAuditPageViewModel> BuildPageAsync(string? q, string? role, string? category,
        string? severity, string? outcome, string? entity, string? correlation, DateTime? fromDate,
        DateTime? toDate, int page, CancellationToken cancellationToken)
    {
        const int pageSize = 30;
        var query = Filter(q, role, category, severity, outcome, entity, correlation, fromDate, toDate);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var logs = await query.OrderByDescending(log => log.CreatedAtUtc).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(cancellationToken);
        var ids = logs.Where(log => log.UserId != null).Select(log => log.UserId!).Distinct().ToArray();
        var emails = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);
        var rows = logs.Select(log => new SecurityAuditRowViewModel
        {
            Id = log.Id, UserId = log.UserId, UserEmail = emails.GetValueOrDefault(log.UserId ?? string.Empty),
            UserRole = log.UserRole, EventType = log.EventType, Category = log.Category,
            Severity = log.Severity, Outcome = log.Outcome, Description = log.Description,
            EntityType = log.EntityType, EntityReference = log.EntityReference, RequestPath = log.RequestPath,
            CorrelationId = log.CorrelationId,
            LocalTimestamp = calendar.ToLocal(DateTime.SpecifyKind(log.CreatedAtUtc, DateTimeKind.Utc)).DateTime
        }).ToList();
        return new SecurityAuditPageViewModel
        {
            Items = rows, Query = q, Role = role, Category = category, Severity = severity,
            Outcome = outcome, Entity = entity, Correlation = correlation, FromDate = fromDate,
            ToDate = toDate, Page = page, TotalPages = totalPages
        };
    }

    private IQueryable<LibraryManagementSystem.Models.SecurityAuditLog> Filter(string? q, string? role, string? category,
        string? severity, string? outcome, string? entity, string? correlation,
        DateTime? fromDate, DateTime? toDate)
    {
        var query = db.SecurityAuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var ids = db.Users.Where(user => user.Email != null && user.Email.Contains(term)).Select(user => user.Id);
            query = query.Where(log => log.Description.Contains(term) || log.EventType.Contains(term)
                || log.CorrelationId.Contains(term) || (log.UserId != null && (log.UserId.Contains(term) || ids.Contains(log.UserId))));
        }
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(log => log.UserRole != null && log.UserRole.Contains(role));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(log => log.Category == category);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(log => log.Severity == severity);
        if (!string.IsNullOrWhiteSpace(outcome)) query = query.Where(log => log.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(log => log.EntityType == entity || log.EntityReference == entity);
        if (!string.IsNullOrWhiteSpace(correlation)) query = query.Where(log => log.CorrelationId.Contains(correlation));
        if (fromDate.HasValue) query = query.Where(log => log.CreatedAtUtc >= calendar.ToUtc(fromDate.Value.Date));
        if (toDate.HasValue) query = query.Where(log => log.CreatedAtUtc < calendar.ToUtc(toDate.Value.Date.AddDays(1)));
        return query;
    }

    private static void AppendCsv(StringBuilder builder, params string?[] values) => builder.AppendLine(string.Join(',', values.Select(value =>
    {
        var safe = value ?? string.Empty;
        if (safe.StartsWith('=') || safe.StartsWith('+') || safe.StartsWith('-') || safe.StartsWith('@')) safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    })));
}
