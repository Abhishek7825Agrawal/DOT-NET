using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.Tests;

public static class BusinessLogicTests
{
    public static bool Run()
    {
        var passed = 0;
        var failed = 0;

        void Assert(bool condition, string testName)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                Console.ResetColor();
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName}");
                Console.ResetColor();
                failed++;
            }
        }

        Console.WriteLine("==================================================");
        Console.WriteLine("  SHELFWISE BUSINESS LOGIC & UNIT VERIFICATION    ");
        Console.WriteLine("==================================================");

        // 1. ISBN-10 Tests
        Assert(IsbnRules.IsValid("0-306-40615-2"), "ISBN-10 Valid formatted string (0-306-40615-2)");
        Assert(IsbnRules.IsValid("0306406152"), "ISBN-10 Valid unformatted string (0306406152)");
        Assert(IsbnRules.IsValid("0-8044-2957-X"), "ISBN-10 Valid with check digit 'X' (0-8044-2957-X)");
        Assert(!IsbnRules.IsValid("0-306-40615-3"), "ISBN-10 Invalid check digit rejected (0-306-40615-3)");
        Assert(!IsbnRules.IsValid("030640615"), "ISBN-10 Too short rejected");
        Assert(!IsbnRules.IsValid("030640615299"), "ISBN-10 Too long rejected");

        // 2. ISBN-13 Tests
        Assert(IsbnRules.IsValid("978-0-306-40615-7"), "ISBN-13 Valid formatted string (978-0-306-40615-7)");
        Assert(IsbnRules.IsValid("9780306406157"), "ISBN-13 Valid unformatted string (9780306406157)");
        Assert(IsbnRules.IsValid("978-3-16-148410-0"), "ISBN-13 Valid with check digit 0 (978-3-16-148410-0)");
        Assert(!IsbnRules.IsValid("978-0-306-40615-8"), "ISBN-13 Invalid check digit rejected (978-0-306-40615-8)");
        Assert(!IsbnRules.IsValid("978030640615"), "ISBN-13 Wrong length rejected");

        // 3. ISBN Normalization & Nullability
        Assert(IsbnRules.IsValid(null), "Null ISBN is valid (optional field)");
        Assert(IsbnRules.IsValid(""), "Empty ISBN is valid (optional field)");
        Assert(IsbnRules.Normalize("  ISBN: 978-0-306-40615-7  ") == "9780306406157", "ISBN prefix 'ISBN:' correctly stripped");
        Assert(IsbnRules.Normalize("0-8044-2957-x") == "080442957X", "ISBN check digit normalized to uppercase 'X'");

        // 4. Overdue Day Calculations
        var baseDate = new DateTime(2026, 11, 1);
        Assert(FineCalculator.CalculateOverdueDays(baseDate, baseDate) == 0, "Overdue days on exact due date is 0");
        Assert(FineCalculator.CalculateOverdueDays(baseDate, baseDate.AddDays(-2)) == 0, "Overdue days before due date is 0");
        Assert(FineCalculator.CalculateOverdueDays(baseDate, baseDate.AddDays(5)) == 5, "Overdue days 5 days late equals 5");

        // 5. Grace Period Tests
        Assert(FineCalculator.CalculateOverdueDays(baseDate, baseDate.AddDays(3), gracePeriodDays: 3) == 0, "Overdue days within 3-day grace period is 0");
        Assert(FineCalculator.CalculateOverdueDays(baseDate, baseDate.AddDays(5), gracePeriodDays: 3) == 5, "Overdue days beyond grace period charges full overdue days");

        // 6. Fine Amount Calculations
        Assert(FineCalculator.CalculateFine(baseDate, baseDate.AddDays(4), 5.00m) == 20.00m, "4 overdue days at ₹5.00/day equals ₹20.00");
        Assert(FineCalculator.CalculateFine(baseDate, baseDate.AddDays(1), 7.50m) == 7.50m, "1 overdue day at ₹7.50/day equals ₹7.50");
        Assert(FineCalculator.CalculateFine(baseDate, baseDate, 5.00m) == 0.00m, "0 overdue days equals ₹0.00 fine");

        // 7. Accrued Fine & Outstanding Balance for IssueRecord
        var issueActive = new IssueRecord
        {
            Id = 1,
            DueDateUtc = baseDate,
            FineRatePerDay = 10.00m,
            FineAmount = 0m,
            FinePaidAmount = 0m,
            Status = IssueStatus.Active
        };
        var activeFine = FineCalculator.CalculateAccruedFine(issueActive, baseDate.AddDays(3));
        Assert(activeFine == 30.00m, "Active loan accrued fine calculates correctly (3 days * ₹10 = ₹30)");

        var issueReturned = new IssueRecord
        {
            Id = 2,
            DueDateUtc = baseDate,
            ReturnDateUtc = baseDate.AddDays(5),
            FineRatePerDay = 10.00m,
            FineAmount = 50.00m,
            FinePaidAmount = 20.00m,
            Status = IssueStatus.Returned
        };
        var returnedBalance = FineCalculator.CalculateOutstandingBalance(issueReturned, baseDate.AddDays(10));
        Assert(returnedBalance == 30.00m, "Returned loan balance equals FineAmount - FinePaidAmount (₹50 - ₹20 = ₹30)");

        var issueLost = new IssueRecord
        {
            Id = 3,
            DueDateUtc = baseDate,
            ReturnDateUtc = baseDate.AddDays(2),
            FineRatePerDay = 5.00m,
            FineAmount = 260.00m, // 10 fine + 250 penalty
            FinePaidAmount = 260.00m,
            Status = IssueStatus.Lost
        };
        var lostBalance = FineCalculator.CalculateOutstandingBalance(issueLost, baseDate.AddDays(10));
        Assert(lostBalance == 0.00m, "Fully paid lost book balance is ₹0.00");

        Assert(SqlServerQueryTranslationTests.Run(), "Affected dashboard and reporting LINQ queries translate with SQL Server EF Core");

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"Total Tests: {passed + failed} | Passed: {passed} | Failed: {failed}");
        Console.WriteLine("==================================================");

        return failed == 0;
    }
}

