using System.Text;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class ReportingService(
    ApplicationDbContext db,
    LibraryCalendar calendar,
    SettingsService settingsService)
{
    public async Task<ReportsDashboardDto> GetComprehensiveReportAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetSettingsAsync(cancellationToken);
        var today = calendar.Today;

        var totalBooks = await db.Books.CountAsync(cancellationToken);
        var activeBooks = await db.Books.CountAsync(b => b.IsActive, cancellationToken);
        var archivedBooks = totalBooks - activeBooks;
        var totalCopies = await db.Books.Where(b => b.IsActive).SumAsync(b => (int?)b.TotalQuantity, cancellationToken) ?? 0;
        var availableCopies = await db.Books.Where(b => b.IsActive).SumAsync(b => (int?)b.AvailableQuantity, cancellationToken) ?? 0;
        var checkedOutCopies = totalCopies - availableCopies;

        var totalMembers = await db.Members.CountAsync(cancellationToken);
        var activeMembers = await db.Members.CountAsync(m => m.IsActive, cancellationToken);
        var inactiveMembers = totalMembers - activeMembers;

        var activeLoans = await db.IssueRecords.CountAsync(i => i.Status == IssueStatus.Active, cancellationToken);
        var overdueLoans = await db.IssueRecords.CountAsync(i => i.Status == IssueStatus.Active && i.DueDateUtc < today, cancellationToken);
        var returnedLoans = await db.IssueRecords.CountAsync(i => i.Status == IssueStatus.Returned, cancellationToken);
        var lostLoans = await db.IssueRecords.CountAsync(i => i.Status == IssueStatus.Lost, cancellationToken);

        var totalFinesLevied = await db.IssueRecords.SumAsync(i => (decimal?)i.FineAmount, cancellationToken) ?? 0;
        var totalFinesCollected = await db.FinePayments.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;

        // Current accrued active fines
        var activeOverdues = await db.IssueRecords.AsNoTracking()
            .Where(i => i.Status == IssueStatus.Active && i.DueDateUtc < today)
            .Select(i => new { i.DueDateUtc, i.FineRatePerDay })
            .ToListAsync(cancellationToken);
        var accruedUnreturnedFine = activeOverdues.Sum(i =>
            FineCalculator.CalculateFine(i.DueDateUtc, today, i.FineRatePerDay, settings.GracePeriodDays));

        // Outstanding on completed (returned or lost)
        var finishedBalance = await db.IssueRecords.AsNoTracking()
            .Where(i => (i.Status == IssueStatus.Returned || i.Status == IssueStatus.Lost) && i.FineAmount > i.FinePaidAmount)
            .SumAsync(i => (decimal?)(i.FineAmount - i.FinePaidAmount), cancellationToken) ?? 0;

        var totalOutstandingBalance = finishedBalance + accruedUnreturnedFine;

        // Top 5 most borrowed books
        var topBooks = await GetTopBorrowedBooksQuery(db).ToListAsync(cancellationToken);

        // Top 5 active borrowers
        var topBorrowers = await GetTopBorrowersQuery(db).ToListAsync(cancellationToken);

        // Last 7 days activity trend
        var sevenDaysAgo = today.AddDays(-6);
        var tomorrow = today.AddDays(1);
        var issuesByDay = await GetIssueCountsByDayQuery(db, sevenDaysAgo, tomorrow)
            .ToDictionaryAsync(item => item.Day, item => item.Count, cancellationToken);
        var returnsByDay = await GetReturnCountsByDayQuery(db, sevenDaysAgo, tomorrow)
            .ToDictionaryAsync(item => item.Day, item => item.Count, cancellationToken);

        var trendDays = new List<DailyActivityDto>();
        for (var day = sevenDaysAgo; day <= today; day = day.AddDays(1))
        {
            var dayDate = day.Date;
            trendDays.Add(new DailyActivityDto(
                day.ToString("dd MMM"),
                issuesByDay.GetValueOrDefault(dayDate),
                returnsByDay.GetValueOrDefault(dayDate)
            ));
        }

        return new ReportsDashboardDto(
            totalBooks, activeBooks, archivedBooks,
            totalCopies, availableCopies, checkedOutCopies,
            totalMembers, activeMembers, inactiveMembers,
            activeLoans, overdueLoans, returnedLoans, lostLoans,
            totalFinesLevied, totalFinesCollected, totalOutstandingBalance,
            topBooks, topBorrowers, trendDays
        );
    }

    internal static IQueryable<TopBorrowedBookDto> GetTopBorrowedBooksQuery(ApplicationDbContext db)
    {
        var countsByBook = db.IssueRecords.AsNoTracking()
            .GroupBy(issue => issue.BookId)
            .Select(group => new { BookId = group.Key, BorrowCount = group.Count() });

        return (from book in db.Books.AsNoTracking()
                join count in countsByBook on book.Id equals count.BookId
                orderby count.BorrowCount descending
                select new TopBorrowedBookDto(book.Title, book.Author, book.Category, count.BorrowCount))
            .Take(5);
    }

    internal static IQueryable<TopBorrowerDto> GetTopBorrowersQuery(ApplicationDbContext db)
    {
        var countsByMember = db.IssueRecords.AsNoTracking()
            .GroupBy(issue => issue.MemberId)
            .Select(group => new { MemberId = group.Key, TotalLoans = group.Count() });

        return (from member in db.Members.AsNoTracking()
                join count in countsByMember on member.Id equals count.MemberId
                join user in db.Users.AsNoTracking() on member.UserId equals user.Id
                orderby count.TotalLoans descending
                select new TopBorrowerDto(member.FullName, user.Email ?? "—", count.TotalLoans))
            .Take(5);
    }

    internal static IQueryable<DailyActivityCountDto> GetIssueCountsByDayQuery(
        ApplicationDbContext db, DateTime startInclusive, DateTime endExclusive) =>
        db.IssueRecords.AsNoTracking()
            .Where(issue => issue.IssueDateUtc >= startInclusive && issue.IssueDateUtc < endExclusive)
            .GroupBy(issue => issue.IssueDateUtc.Date)
            .Select(group => new DailyActivityCountDto(group.Key, group.Count()));

    internal static IQueryable<DailyActivityCountDto> GetReturnCountsByDayQuery(
        ApplicationDbContext db, DateTime startInclusive, DateTime endExclusive) =>
        db.IssueRecords.AsNoTracking()
            .Where(issue => issue.ReturnDateUtc >= startInclusive && issue.ReturnDateUtc < endExclusive)
            .GroupBy(issue => issue.ReturnDateUtc!.Value.Date)
            .Select(group => new DailyActivityCountDto(group.Key, group.Count()));

    public async Task<byte[]> ExportBooksCsvAsync(CancellationToken cancellationToken = default)
    {
        var books = await db.Books.AsNoTracking().OrderBy(b => b.Title).ToListAsync(cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("ID,Title,Author,ISBN,Category,Publisher,PublicationYear,TotalCopies,AvailableCopies,Status");
        foreach (var b in books)
        {
            sb.AppendLine($"\"{b.Id}\",\"{Escape(b.Title)}\",\"{Escape(b.Author)}\",\"{b.Isbn ?? ""}\",\"{Escape(b.Category)}\",\"{Escape(b.Publisher)}\",\"{b.PublicationYear}\",\"{b.TotalQuantity}\",\"{b.AvailableQuantity}\",\"{(b.IsActive ? "Active" : "Archived")}\"");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> ExportCirculationCsvAsync(CancellationToken cancellationToken = default)
    {
        var issues = await db.IssueRecords.AsNoTracking()
            .Include(i => i.Book)
            .Include(i => i.Member).ThenInclude(m => m.User)
            .OrderByDescending(i => i.IssueDateUtc)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("LoanID,BookTitle,BookISBN,MemberName,MemberEmail,IssueDate,DueDate,ReturnDate,Status,FineAmount,FinePaidAmount,Remarks");
        foreach (var i in issues)
        {
            sb.AppendLine($"\"{i.Id}\",\"{Escape(i.Book.Title)}\",\"{i.Book.Isbn ?? ""}\",\"{Escape(i.Member.FullName)}\",\"{Escape(i.Member.User.Email)}\",\"{i.IssueDateUtc:yyyy-MM-dd}\",\"{i.DueDateUtc:yyyy-MM-dd}\",\"{(i.ReturnDateUtc.HasValue ? i.ReturnDateUtc.Value.ToString("yyyy-MM-dd") : "")}\",\"{i.Status}\",\"{i.FineAmount:F2}\",\"{i.FinePaidAmount:F2}\",\"{Escape(i.Remarks)}\"");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> ExportFinePaymentsCsvAsync(CancellationToken cancellationToken = default)
    {
        var payments = await db.FinePayments.AsNoTracking()
            .Include(p => p.IssueRecord).ThenInclude(i => i.Book)
            .Include(p => p.IssueRecord).ThenInclude(i => i.Member)
            .OrderByDescending(p => p.PaymentDateUtc)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("PaymentID,LoanID,MemberName,BookTitle,Amount,PaymentMethod,ReferenceNumber,PaymentDate");
        foreach (var p in payments)
        {
            var dateStr = calendar.ToLocal(p.PaymentDateUtc).ToString("yyyy-MM-dd HH:mm");
            sb.AppendLine($"\"{p.Id}\",\"{p.IssueRecordId}\",\"{Escape(p.IssueRecord.Member.FullName)}\",\"{Escape(p.IssueRecord.Book.Title)}\",\"{p.Amount:F2}\",\"{p.PaymentMethod}\",\"{Escape(p.ReferenceNumber)}\",\"{dateStr}\"");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Replace("\"", "\"\"");
}

public sealed record ReportsDashboardDto(
    int TotalBooks, int ActiveBooks, int ArchivedBooks,
    int TotalCopies, int AvailableCopies, int CheckedOutCopies,
    int TotalMembers, int ActiveMembers, int InactiveMembers,
    int ActiveLoans, int OverdueLoans, int ReturnedLoans, int LostLoans,
    decimal TotalFinesLevied, decimal TotalFinesCollected, decimal OutstandingBalance,
    IReadOnlyList<TopBorrowedBookDto> TopBooks,
    IReadOnlyList<TopBorrowerDto> TopBorrowers,
    IReadOnlyList<DailyActivityDto> TrendDays
);

public sealed record TopBorrowedBookDto(string Title, string Author, string Category, int BorrowCount);
public sealed record TopBorrowerDto(string MemberName, string Email, int TotalLoans);
internal sealed record DailyActivityCountDto(DateTime Day, int Count);
public sealed record DailyActivityDto(string Label, int Issues, int Returns);

