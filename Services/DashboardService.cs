using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class DashboardService(
    ApplicationDbContext db,
    LibraryCalendar calendar,
    SettingsService settingsService)
{
    public async Task<AdminDashboardData> GetAdminAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetSettingsAsync(cancellationToken);
        var today = calendar.Today;

        var finishedFineBalance = await db.IssueRecords
            .Where(issue => (issue.Status == IssueStatus.Returned || issue.Status == IssueStatus.Lost) && issue.FineAmount > issue.FinePaidAmount)
            .SumAsync(issue => (decimal?)(issue.FineAmount - issue.FinePaidAmount), cancellationToken) ?? 0;

        var currentOverdueRecords = await db.IssueRecords.AsNoTracking()
            .Where(issue => issue.Status == IssueStatus.Active && issue.DueDateUtc < today)
            .Select(issue => new { issue.DueDateUtc, issue.FineRatePerDay })
            .ToListAsync(cancellationToken);

        var currentOverdueBalance = currentOverdueRecords.Sum(issue =>
            FineCalculator.CalculateFine(issue.DueDateUtc, today, issue.FineRatePerDay, settings.GracePeriodDays));

        var totalBooks = await db.Books.CountAsync(book => book.IsActive, cancellationToken);
        var totalMembers = await db.Members.CountAsync(member => member.IsActive, cancellationToken);
        var activeLoans = await db.IssueRecords.CountAsync(issue => issue.Status == IssueStatus.Active, cancellationToken);
        var availableCopies = await db.Books.Where(book => book.IsActive).SumAsync(book => (int?)book.AvailableQuantity, cancellationToken) ?? 0;
        var overdueLoans = await db.IssueRecords.CountAsync(issue => issue.Status == IssueStatus.Active && issue.DueDateUtc < today, cancellationToken);
        var totalLostBooks = await db.IssueRecords.CountAsync(issue => issue.Status == IssueStatus.Lost, cancellationToken);

        var recentIssues = await db.IssueRecords.AsNoTracking()
            .Include(issue => issue.Book)
            .Include(issue => issue.Member)
            .OrderByDescending(issue => issue.IssueDateUtc)
            .Take(7)
            .ToListAsync(cancellationToken);

        var recentReturns = await db.IssueRecords.AsNoTracking()
            .Include(issue => issue.Book)
            .Include(issue => issue.Member)
            .Where(issue => (issue.Status == IssueStatus.Returned || issue.Status == IssueStatus.Lost) && issue.ReturnDateUtc != null)
            .OrderByDescending(issue => issue.ReturnDateUtc)
            .Take(7)
            .ToListAsync(cancellationToken);

        var categoriesCount = await db.Categories.CountAsync(cancellationToken);

        var popularBooks = await GetPopularBooksQuery(db).ToListAsync(cancellationToken);

        var weekStart = today.AddDays(-6);
        var dailyIssues = await db.IssueRecords.AsNoTracking()
            .Where(issue => issue.IssueDateUtc >= weekStart && issue.IssueDateUtc <= today)
            .GroupBy(issue => issue.IssueDateUtc.Date)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var issueCountsByDay = dailyIssues.ToDictionary(item => item.Day, item => item.Count);
        var weeklyIssueActivity = Enumerable.Range(0, 7)
            .Select(offset => new DailyIssueData(weekStart.AddDays(offset), issueCountsByDay.GetValueOrDefault(weekStart.AddDays(offset))))
            .ToList();

        return new AdminDashboardData(
            totalBooks,
            totalMembers,
            activeLoans,
            availableCopies,
            overdueLoans,
            totalLostBooks,
            categoriesCount,
            finishedFineBalance + currentOverdueBalance,
            recentIssues,
            recentReturns,
            popularBooks,
            weeklyIssueActivity);
    }

    internal static IQueryable<PopularBookData> GetPopularBooksQuery(ApplicationDbContext db)
    {
        var countsByBook = db.IssueRecords.AsNoTracking()
            .GroupBy(issue => issue.BookId)
            .Select(group => new { BookId = group.Key, IssueCount = group.Count() });

        return (from book in db.Books.AsNoTracking()
                join count in countsByBook on book.Id equals count.BookId
                where book.IsActive
                orderby count.IssueCount descending, book.Title
                select new PopularBookData(book.Title, book.Author, book.Category, count.IssueCount))
            .Take(5);
    }

    public async Task<MemberDashboardData> GetMemberAsync(string userId, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetSettingsAsync(cancellationToken);
        var issues = await db.IssueRecords.AsNoTracking()
            .Include(issue => issue.Book)
            .Include(issue => issue.FinePayments)
            .Where(issue => issue.Member.UserId == userId)
            .OrderByDescending(issue => issue.IssueDateUtc)
            .ToListAsync(cancellationToken);

        var today = calendar.Today;
        var outstanding = issues.Sum(issue =>
            FineCalculator.CalculateOutstandingBalance(issue, today, settings.GracePeriodDays));

        return new MemberDashboardData(
            await db.Members.AsNoTracking().Include(member => member.User)
                .FirstOrDefaultAsync(member => member.UserId == userId, cancellationToken),
            issues.Where(issue => issue.Status == IssueStatus.Active).ToList(),
            issues,
            outstanding,
            today);
    }
}

public sealed record AdminDashboardData(
    int TotalBooks,
    int TotalMembers,
    int ActiveLoans,
    int AvailableCopies,
    int OverdueLoans,
    int TotalLostBooks,
    int CategoriesCount,
    decimal OutstandingFines,
    IReadOnlyList<IssueRecord> RecentIssues,
    IReadOnlyList<IssueRecord> RecentReturns,
    IReadOnlyList<PopularBookData> PopularBooks,
    IReadOnlyList<DailyIssueData> WeeklyIssueActivity);

public sealed record PopularBookData(string Title, string Author, string Category, int IssueCount);
public sealed record DailyIssueData(DateTime Date, int IssueCount);

public sealed record MemberDashboardData(
    Member? Profile,
    IReadOnlyList<IssueRecord> ActiveLoans,
    IReadOnlyList<IssueRecord> History,
    decimal OutstandingFines,
    DateTime Today);
