using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Tests;

public static class SqlServerQueryTranslationTests
{
    public static bool Run()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ShelfwiseTranslationOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var db = new ApplicationDbContext(options);
        var start = new DateTime(2026, 11, 1);
        var end = start.AddDays(7);
        var queries = new (string Name, IQueryable Query)[]
        {
            ("Dashboard popular-book aggregate", DashboardService.GetPopularBooksQuery(db)),
            ("Report top-books aggregate", ReportingService.GetTopBorrowedBooksQuery(db)),
            ("Report top-borrowers aggregate", ReportingService.GetTopBorrowersQuery(db)),
            ("Report daily issue counts", ReportingService.GetIssueCountsByDayQuery(db, start, end)),
            ("Report daily return counts", ReportingService.GetReturnCountsByDayQuery(db, start, end))
        };

        var passed = true;
        foreach (var (name, query) in queries)
        {
            try
            {
                var sql = query.ToQueryString();
                var translated = sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
                    && sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase);
                if (name.Contains("borrowed-book", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("top-books", StringComparison.OrdinalIgnoreCase))
                    translated &= sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase)
                        && sql.Contains("Books", StringComparison.OrdinalIgnoreCase);
                if (name.Contains("borrowers", StringComparison.OrdinalIgnoreCase))
                    translated &= sql.Contains("AspNetUsers", StringComparison.OrdinalIgnoreCase);

                Console.WriteLine($"[{(translated ? "PASS" : "FAIL")}] SQL Server translation: {name}");
                passed &= translated;
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine($"[FAIL] SQL Server translation: {name} ({exception.GetType().Name})");
                passed = false;
            }
        }

        return passed;
    }
}
