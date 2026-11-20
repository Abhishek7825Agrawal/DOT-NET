using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryManagementSystem.Tests;

public static class ActivityAuditTests
{
    public static (int Passed, int Failed) Run()
    {
        var passed = 0;
        var failed = 0;
        void Assert(bool value, string name)
        {
            Console.WriteLine(value ? $"[PASS] {name}" : $"[FAIL] {name}");
            if (value) passed++; else failed++;
        }

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        using var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "phase7a-correlation-test";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, "Member")], "test"));
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var service = new ActivityLogService(db, accessor, NullLogger<ActivityLogService>.Instance);

        service.RecordAsync("member-a", "BookBorrowed", "Circulation", "You borrowed a title.",
            entityType: "Book", entityReference: "17", securityEvent: true).GetAwaiter().GetResult();
        service.RecordAsync("member-b", "BookmarkAdded", "DigitalReading", "You bookmarked page 25.")
            .GetAwaiter().GetResult();
        Assert(db.UserActivityLogs.Count() == 2 && db.SecurityAuditLogs.Count() == 1,
            "Activity and security events persist in separate database tables");
        Assert(db.UserActivityLogs.Where(log => log.UserId == "member-a").Count() == 1
            && db.UserActivityLogs.Where(log => log.UserId == "member-b").Count() == 1,
            "User activity queries can isolate each account by UserId");
        Assert(db.UserActivityLogs.Single(log => log.UserId == "member-a").CorrelationId == "phase7a-correlation-test",
            "Activity records preserve a correlation id");
        Assert(db.SecurityAuditLogs.Single().EntityReference == "17",
            "Security audit records preserve entity references");

        var immutable = db.UserActivityLogs.Single(log => log.UserId == "member-a");
        immutable.Description = "rewritten";
        var rejectedMutation = false;
        try { db.SaveChanges(); }
        catch (InvalidOperationException) { rejectedMutation = true; }
        Assert(rejectedMutation, "ApplicationDbContext rejects edits to historical activity records");
        db.Entry(immutable).State = EntityState.Unchanged;

        var oldRecord = db.SecurityAuditLogs.Single();
        db.Database.ExecuteSqlInterpolated($"UPDATE SecurityAuditLogs SET CreatedAtUtc = {DateTime.UtcNow.AddDays(-40)} WHERE Id = {oldRecord.Id}");
        db.Entry(oldRecord).Reload();
        db.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            EventType = "LoginFailed", Category = "Authentication", Severity = "Warning",
            Outcome = "Failure", Description = "A sign-in attempt failed.",
            CorrelationId = "retention-test", CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        var deleted = service.DeleteExpiredAsync(DateTime.UtcNow.AddDays(-30)).GetAwaiter().GetResult();
        Assert(deleted.SecurityLogsDeleted == 1 && db.SecurityAuditLogs.Count() == 1,
            "Retention removes only records older than the configured cutoff");

        return (passed, failed);
    }
}
