using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminUsersController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IShelfwiseEmailSender emailSender,
    ActivityLogService activityLogs,
    IWebHostEnvironment environment,
    IConfiguration configuration) : Controller
{
    [HttpGet]
    public async Task<IActionResult> PasswordManagement(string? q, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 30;
        var query = db.SecurityAuditLogs.AsNoTracking().Where(log => log.Category == "PasswordRecovery");
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var userIds = db.Users.Where(user => user.Email != null && user.Email.Contains(term)).Select(user => user.Id);
            query = query.Where(log => log.EventType.Contains(term) || log.Description.Contains(term)
                || (log.UserId != null && userIds.Contains(log.UserId))
                || (log.EntityReference != null && userIds.Contains(log.EntityReference)));
        }
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var logs = await query.OrderByDescending(log => log.CreatedAtUtc).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(cancellationToken);
        var ids = logs.SelectMany(log => new[] { log.UserId, log.EntityReference }).Where(id => id is not null)
            .Distinct().ToArray();
        var emails = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);
        return View(new PasswordManagementViewModel
        {
            Query = q, Page = page, TotalPages = totalPages,
            Items = logs.Select(log => new SecurityAuditRowViewModel
            {
                Id = log.Id, UserId = log.UserId, UserEmail = emails.GetValueOrDefault(log.EntityReference ?? log.UserId ?? string.Empty),
                UserRole = log.UserRole, EventType = log.EventType, Category = log.Category,
                Severity = log.Severity, Outcome = log.Outcome, Description = log.Description,
                EntityType = log.EntityType, EntityReference = log.EntityReference,
                CorrelationId = log.CorrelationId, LocalTimestamp = log.CreatedAtUtc
            }).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? role, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        var query = db.Users.AsNoTracking()
            .Where(user => db.UserRoles.Any(userRole => userRole.UserId == user.Id
                && db.Roles.Any(identityRole => identityRole.Id == userRole.RoleId
                    && (identityRole.Name == "Member" || identityRole.Name == "Staff"))));
        if (string.Equals(role, "Member", StringComparison.OrdinalIgnoreCase))
            query = query.Where(user => db.UserRoles.Any(userRole => userRole.UserId == user.Id
                && db.Roles.Any(identityRole => identityRole.Id == userRole.RoleId && identityRole.Name == "Member")));
        else if (string.Equals(role, "Staff", StringComparison.OrdinalIgnoreCase))
            query = query.Where(user => db.UserRoles.Any(userRole => userRole.UserId == user.Id
                && db.Roles.Any(identityRole => identityRole.Id == userRole.RoleId && identityRole.Name == "Staff")));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(user => (user.Email != null && user.Email.Contains(term))
                || (user.UserName != null && user.UserName.Contains(term))
                || (user.MemberProfile != null && user.MemberProfile.FullName.Contains(term)));
        }

        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var rows = await query.OrderBy(user => user.Email).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(user => new AdminUserRowViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.MemberProfile != null ? user.MemberProfile.FullName : user.UserName ?? "Account",
                IsActive = user.IsActive
            }).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var isStaff = await (from userRole in db.UserRoles
                                 join identityRole in db.Roles on userRole.RoleId equals identityRole.Id
                                 where userRole.UserId == row.Id && identityRole.Name == "Staff"
                                 select userRole).AnyAsync(cancellationToken);
            row.Role = isStaff ? "Staff" : "Member";
            var lastReset = await db.SecurityAuditLogs.AsNoTracking()
                .Where(log => log.EntityReference == row.Id && log.Category == "PasswordRecovery")
                .OrderByDescending(log => log.CreatedAtUtc)
                .Select(log => log.Outcome)
                .FirstOrDefaultAsync(cancellationToken);
            row.LastResetStatus = lastReset;
        }
        return View(new AdminUserListViewModel { Users = rows, Query = q, Role = role, Page = page, TotalPages = totalPages });
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        var account = await db.Users.AsNoTracking().Where(item => item.Id == id)
            .Select(user => new AdminUserRowViewModel
            {
                Id = user.Id, Email = user.Email ?? string.Empty,
                DisplayName = user.MemberProfile != null ? user.MemberProfile.FullName : user.UserName ?? "Account",
                IsActive = user.IsActive
            }).FirstOrDefaultAsync(cancellationToken);
        if (account is null) return NotFound();
        var roles = await (from userRole in db.UserRoles
                           join identityRole in db.Roles on userRole.RoleId equals identityRole.Id
                           where userRole.UserId == id
                           select identityRole.Name!).ToListAsync(cancellationToken);
        if (!roles.Contains("Member") && !roles.Contains("Staff")) return NotFound();
        var history = await db.SecurityAuditLogs.AsNoTracking()
            .Where(log => log.EntityReference == id && log.Category == "PasswordRecovery")
            .OrderByDescending(log => log.CreatedAtUtc).Take(50).ToListAsync(cancellationToken);
        return View(new AdminUserDetailsViewModel
        {
            Account = new AdminUserRowViewModel
            {
                Id = account.Id, Email = account.Email, DisplayName = account.DisplayName,
                IsActive = account.IsActive, Role = roles.Contains("Staff") ? "Staff" : "Member",
                LastResetStatus = history.FirstOrDefault()?.Outcome
            },
            PasswordHistory = history.Select(log => new SecurityAuditRowViewModel
            {
                Id = log.Id, UserId = log.UserId, EventType = log.EventType, Category = log.Category,
                Severity = log.Severity, Outcome = log.Outcome, Description = log.Description,
                EntityType = log.EntityType, EntityReference = log.EntityReference,
                CorrelationId = log.CorrelationId, LocalTimestamp = DateTime.SpecifyKind(log.CreatedAtUtc, DateTimeKind.Utc)
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> SendPasswordReset(string id, CancellationToken cancellationToken)
    {
        var target = await userManager.FindByIdAsync(id);
        if (target is null || string.IsNullOrWhiteSpace(target.Email)) return NotFound();
        var roles = await userManager.GetRolesAsync(target);
        if (!roles.Contains("Member") && !roles.Contains("Staff")) return NotFound();

        var publicBaseUrl = ResolvePublicBaseUrl();
        EmailSendResult result;
        if (publicBaseUrl is null)
            result = new EmailSendResult(false, "Secure public base URL is not configured.");
        else
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(target);
            var actionPath = Url.Action("ResetPassword", "Account", new { email = target.Email }) ?? "/Account/ResetPassword";
            result = await emailSender.SendPasswordResetLinkAsync(target.Email,
                target.MemberProfile?.FullName ?? target.UserName ?? "Shelfwise user",
                new Uri(publicBaseUrl, actionPath).ToString() + "#token=" + Uri.EscapeDataString(token), cancellationToken);
        }

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var outcome = result.AcceptedByProvider ? "AcceptedByProvider" : "DeliveryUnavailable";
        await activityLogs.RecordSecurityAsync(actorId, "AdminPasswordResetInitiated", "PasswordRecovery",
            "An administrator initiated a password reset email.", outcome,
            result.AcceptedByProvider ? "Information" : "Warning", "User", target.Id, cancellationToken);
        await activityLogs.RecordAsync(actorId, "AdminPasswordResetInitiated", "PasswordRecovery",
            result.AcceptedByProvider
                ? $"You requested a password reset email for {target.Email}."
                : $"Your password reset email request for {target.Email} could not be delivered.",
            outcome, "User", target.Id, cancellationToken: cancellationToken);
        TempData[result.AcceptedByProvider ? "Success" : "Error"] = result.AcceptedByProvider
            ? "The reset email was accepted by the configured SMTP provider. Delivery to the inbox is not guaranteed."
            : "The reset email was not sent. Configure SMTP and the secure public base URL, then retry.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(string id, bool active, CancellationToken cancellationToken)
    {
        var target = await userManager.Users.Include(user => user.MemberProfile)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (target is null) return NotFound();
        var roles = await userManager.GetRolesAsync(target);
        if (!roles.Contains("Member") && !roles.Contains("Staff")) return NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        target.IsActive = active;
        var update = await userManager.UpdateAsync(target);
        if (!update.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            TempData["Error"] = "Account status could not be updated.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (target.MemberProfile is not null)
            target.MemberProfile.IsActive = active;
        if (target.MemberProfile is not null)
            await db.SaveChangesAsync(cancellationToken);
        await userManager.UpdateSecurityStampAsync(target);
        await transaction.CommitAsync(cancellationToken);

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await activityLogs.RecordSecurityAsync(actorId, active ? "AccountReactivated" : "AccountDeactivated",
            "AccountManagement", $"An administrator {(active ? "reactivated" : "deactivated")} a {roles[0]} account.",
            "Success", active ? "Information" : "Warning", "User", target.Id, cancellationToken);
        await activityLogs.RecordAsync(actorId, active ? "AccountReactivated" : "AccountDeactivated",
            "AccountManagement", $"You {(active ? "reactivated" : "deactivated")} the account for {target.Email}.",
            entityType: "User", entityReference: target.Id, cancellationToken: cancellationToken);
        TempData["Success"] = active ? "Account reactivated." : "Account deactivated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Uri? ResolvePublicBaseUrl()
    {
        if (Uri.TryCreate(configuration["Email:PublicBaseUrl"], UriKind.Absolute, out var configured))
            return environment.IsDevelopment() || configured.Scheme == Uri.UriSchemeHttps ? configured : null;
        if (environment.IsDevelopment() && Request.Host.HasValue
            && (Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || System.Net.IPAddress.TryParse(Request.Host.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip)))
            return new Uri($"{Request.Scheme}://{Request.Host}");
        return null;
    }
}
