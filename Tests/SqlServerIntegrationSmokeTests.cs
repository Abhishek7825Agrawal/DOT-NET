using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Read-only smoke checks against the configured SQL Server. This deliberately
/// never applies migrations or writes seed/application data.
/// </summary>
public static class SqlServerIntegrationSmokeTests
{
    public static async Task<bool> RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();

        if (!db.Database.IsSqlServer())
        {
            Console.WriteLine("[FAIL] SQL Server smoke checks require DatabaseProvider=SqlServer.");
            return false;
        }

        try
        {
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                Console.WriteLine($"[FAIL] SQL Server has {pendingMigrations.Count()} pending migration(s); none were applied.");
                return false;
            }
            Console.WriteLine("[PASS] SQL Server connection and migration history are current; no migrations were applied.");

            if (!await db.LibrarySettings.AsNoTracking().AnyAsync(cancellationToken))
            {
                Console.WriteLine("[FAIL] Library settings are missing; read-only smoke checks will not seed data.");
                return false;
            }

            var dashboardService = provider.GetRequiredService<DashboardService>();
            var dashboard = await dashboardService.GetAdminAsync(cancellationToken);
            Console.WriteLine($"[PASS] Admin dashboard queries: {dashboard.PopularBooks.Count} popular books, {dashboard.RecentIssues.Count} recent issues, {dashboard.WeeklyIssueActivity.Count} chart days.");

            var memberUserId = await db.Members.AsNoTracking()
                .OrderBy(member => member.Id)
                .Select(member => member.UserId)
                .FirstOrDefaultAsync(cancellationToken);
            if (memberUserId is not null)
            {
                var memberDashboard = await dashboardService.GetMemberAsync(memberUserId, cancellationToken);
                Console.WriteLine($"[PASS] Member dashboard and borrowing-history queries: {memberDashboard.History.Count} records.");
            }

            var reports = await provider.GetRequiredService<ReportingService>().GetComprehensiveReportAsync(cancellationToken);
            Console.WriteLine($"[PASS] Admin reports: {reports.TopBooks.Count} top books, {reports.TopBorrowers.Count} top members, {reports.TrendDays.Count} trend days; aggregates executed.");

            _ = await provider.GetRequiredService<CategoryService>().GetCategoriesWithCountsAsync(cancellationToken);
            _ = await provider.GetRequiredService<BookService>().Search(null, null, includeInactive: true)
                .Take(1).ToListAsync(cancellationToken);
            _ = await provider.GetRequiredService<MemberService>().Search(null)
                .Take(1).ToListAsync(cancellationToken);
            _ = await provider.GetRequiredService<CirculationService>().Search(null)
                .Take(1).ToListAsync(cancellationToken);
            _ = await provider.GetRequiredService<CirculationService>().GetPaymentHistoryAsync(limit: 1, cancellationToken: cancellationToken);
            _ = await db.Members.AsNoTracking()
                .Where(member => member.IsActive)
                .OrderBy(member => member.FullName)
                .Select(member => new { member.Id, Name = member.FullName + " (" + member.User.Email + ")" })
                .Take(1).ToListAsync(cancellationToken);
            _ = await db.Books.AsNoTracking()
                .Where(book => book.IsActive && book.AvailableQuantity > 0)
                .OrderBy(book => book.Title)
                .Select(book => new { book.Id, Name = book.Title + " — " + book.Author + " (" + book.AvailableQuantity + " available)" })
                .Take(1).ToListAsync(cancellationToken);
            Console.WriteLine("[PASS] Category counts, book/member searches, issue form lists, circulation and payment-history queries executed.");
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[FAIL] SQL Server smoke check stopped at {exception.GetType().Name}; details withheld to avoid logging connection information.");
            return false;
        }
    }
}
