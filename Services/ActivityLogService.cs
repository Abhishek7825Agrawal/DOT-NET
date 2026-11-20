using System.Security.Claims;
using System.Text.Json;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class ActivityLogService(
    ApplicationDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ActivityLogService> logger)
{
    public async Task RecordAsync(
        string? userId,
        string eventType,
        string category,
        string description,
        string outcome = "Success",
        string? entityType = null,
        string? entityReference = null,
        object? safeContext = null,
        bool securityEvent = false,
        string severity = "Information",
        CancellationToken cancellationToken = default,
        string? userRoleOverride = null)
    {
        var context = httpContextAccessor.HttpContext;
        var roles = context?.User.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var role = userRoleOverride ?? (roles.Length == 0 ? "Unknown" : string.Join(',', roles));
        var correlationId = context?.Items[CorrelationAuditMiddleware.CorrelationItemKey]?.ToString()
            ?? context?.TraceIdentifier
            ?? Guid.NewGuid().ToString("N");
        var requestPath = context?.Request.Path.Value;

        try
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                db.UserActivityLogs.Add(new UserActivityLog
                {
                    UserId = userId,
                    UserRole = role,
                    ActivityType = eventType,
                    Category = category,
                    Description = description,
                    Status = outcome,
                    EntityType = entityType,
                    EntityReference = entityReference,
                    ContextJson = safeContext is null ? null : JsonSerializer.Serialize(safeContext),
                    CorrelationId = correlationId,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            if (securityEvent)
            {
                db.SecurityAuditLogs.Add(new SecurityAuditLog
                {
                    UserId = userId,
                    UserRole = role,
                    EventType = eventType,
                    Category = category,
                    Severity = severity,
                    Outcome = outcome,
                    Description = description,
                    EntityType = entityType,
                    EntityReference = entityReference,
                    RequestPath = requestPath is { Length: > 300 } ? requestPath[..300] : requestPath,
                    CorrelationId = correlationId,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Never include request query strings, passwords, tokens, or payloads in logs.
            logger.LogError("Could not persist activity event {EventType}; exception type {ExceptionType}; correlation {CorrelationId}",
                eventType, exception.GetType().Name, correlationId);
            db.ChangeTracker.Clear();
        }
    }

    public Task RecordSecurityAsync(
        string? userId,
        string eventType,
        string category,
        string description,
        string outcome = "Success",
        string severity = "Information",
        string? entityType = null,
        string? entityReference = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(userId, eventType, category, description, outcome, entityType, entityReference,
            securityEvent: true, severity: severity, cancellationToken: cancellationToken);

    public async Task<(int UserLogsDeleted, int SecurityLogsDeleted)> DeleteExpiredAsync(
        DateTime retentionCutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var userLogsDeleted = await db.UserActivityLogs
            .Where(log => log.CreatedAtUtc < retentionCutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
        var securityLogsDeleted = await db.SecurityAuditLogs
            .Where(log => log.CreatedAtUtc < retentionCutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
        return (userLogsDeleted, securityLogsDeleted);
    }
}
