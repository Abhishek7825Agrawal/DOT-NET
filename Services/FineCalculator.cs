using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.Services;

public static class FineCalculator
{
    public static int CalculateOverdueDays(DateTime dueDate, DateTime asOfDate, int gracePeriodDays = 0)
    {
        var calendarDays = (asOfDate.Date - dueDate.Date).Days;
        return calendarDays > gracePeriodDays ? calendarDays : 0;
    }

    public static decimal CalculateFine(DateTime dueDate, DateTime asOfDate, decimal fineRatePerDay, int gracePeriodDays = 0)
    {
        var overdueDays = CalculateOverdueDays(dueDate, asOfDate, gracePeriodDays);
        return decimal.Round(overdueDays * fineRatePerDay, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateAccruedFine(IssueRecord issue, DateTime asOfDate, int gracePeriodDays = 0)
    {
        if (issue.Status == IssueStatus.Returned || issue.Status == IssueStatus.Lost)
        {
            return issue.FineAmount;
        }

        return CalculateFine(issue.DueDateUtc, asOfDate, issue.FineRatePerDay, gracePeriodDays);
    }

    public static decimal CalculateOutstandingBalance(IssueRecord issue, DateTime asOfDate, int gracePeriodDays = 0)
    {
        var totalFine = CalculateAccruedFine(issue, asOfDate, gracePeriodDays);
        return Math.Max(0m, totalFine - issue.FinePaidAmount);
    }
}

