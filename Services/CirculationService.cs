using System.Data;
using System.Data.Common;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class CirculationService(
    ApplicationDbContext db,
    LibraryCalendar calendar,
    SettingsService settingsService)
{
    public async Task<(bool Success, string? Error)> IssueAsync(
        int memberId,
        int bookId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            var member = await db.Members.FirstOrDefaultAsync(item => item.Id == memberId, cancellationToken);
            if (member is null || !member.IsActive)
                return (false, "Member does not exist or is inactive.");

            var settings = await settingsService.GetSettingsAsync(cancellationToken);

            // Rule: Check member borrowing limits
            var activeLoansCount = await db.IssueRecords.CountAsync(
                i => i.MemberId == memberId && i.Status == IssueStatus.Active, cancellationToken);
            if (activeLoansCount >= settings.MaxBooksPerMember)
                return (false, $"Member has reached the maximum allowed limit of {settings.MaxBooksPerMember} active loans.");

            // Rule: Check duplicate active loan of same title
            var alreadyHoldingCopy = await db.IssueRecords.AnyAsync(
                i => i.MemberId == memberId && i.BookId == bookId && i.Status == IssueStatus.Active, cancellationToken);
            if (alreadyHoldingCopy)
                return (false, "This member already has an active loan for this book. Multiple loans of the same title are not permitted.");

            // Rule: Check for overdue loans on the member
            var today = calendar.Today;
            var hasSevereOverdue = await db.IssueRecords.AnyAsync(
                i => i.MemberId == memberId && i.Status == IssueStatus.Active && i.DueDateUtc < today, cancellationToken);
            if (hasSevereOverdue)
                return (false, "Member has one or more overdue loans. Overdue books must be returned before new loans can be issued.");

            var book = await db.Books.FirstOrDefaultAsync(
                item => item.Id == bookId && item.IsActive, cancellationToken);
            if (book is null || book.AvailableQuantity < 1)
                return (false, "Book is unavailable.");

            var loanDays = Math.Clamp(settings.LoanPeriodDays, 1, 365);
            var fineRate = decimal.Round(
                Math.Clamp(settings.FineRatePerDay, 0m, 1_000_000m),
                2,
                MidpointRounding.AwayFromZero);

            book.AvailableQuantity--;
            db.IssueRecords.Add(new IssueRecord
            {
                MemberId = memberId,
                BookId = bookId,
                IssueDateUtc = today,
                DueDateUtc = today.AddDays(loanDays),
                FineRatePerDay = fineRate,
                Status = IssueStatus.Active
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "Inventory changed while issuing this book. Refresh the list and try again.");
        }
        catch (DbUpdateException)
        {
            return (false, "The issue could not be saved because a database rule rejected it. Refresh and try again.");
        }
        catch (DbException)
        {
            return (false, "The database could not confirm the issue transaction. Check circulation history before retrying.");
        }
    }

    public async Task<(bool Success, string? Error)> ReturnAsync(
        int issueId,
        string? remarks = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            var issue = await db.IssueRecords.Include(item => item.Book)
                .FirstOrDefaultAsync(item => item.Id == issueId, cancellationToken);
            if (issue is null) return (false, "Issue record not found.");
            if (issue.Status != IssueStatus.Active)
                return (false, "This book is not currently on active loan.");

            var settings = await settingsService.GetSettingsAsync(cancellationToken);
            var returned = calendar.Today;
            var fine = FineCalculator.CalculateFine(issue.DueDateUtc, returned, issue.FineRatePerDay, settings.GracePeriodDays);

            issue.ReturnDateUtc = returned;
            issue.Status = IssueStatus.Returned;
            issue.FineAmount = fine;
            if (!string.IsNullOrWhiteSpace(remarks))
                issue.Remarks = remarks.Trim();

            issue.Book.AvailableQuantity = Math.Min(issue.Book.TotalQuantity, issue.Book.AvailableQuantity + 1);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "This loan or inventory was modified by another transaction. Refresh and confirm status.");
        }
        catch (DbUpdateException)
        {
            return (false, "The return could not be saved because a database rule rejected it.");
        }
        catch (DbException)
        {
            return (false, "The database could not confirm the return transaction. Check the issue status before retrying.");
        }
    }

    public async Task<(bool Success, string? Error)> MarkAsLostAsync(
        int issueId,
        string? remarks = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            var issue = await db.IssueRecords.Include(item => item.Book)
                .FirstOrDefaultAsync(item => item.Id == issueId, cancellationToken);
            if (issue is null) return (false, "Issue record not found.");
            if (issue.Status != IssueStatus.Active)
                return (false, "Only actively issued books can be marked as lost.");

            var settings = await settingsService.GetSettingsAsync(cancellationToken);
            var today = calendar.Today;
            var overdueFine = FineCalculator.CalculateFine(issue.DueDateUtc, today, issue.FineRatePerDay, settings.GracePeriodDays);
            var totalFine = overdueFine + settings.LostBookPenaltyFee;

            issue.ReturnDateUtc = today;
            issue.Status = IssueStatus.Lost;
            issue.FineAmount = totalFine;
            issue.Remarks = string.IsNullOrWhiteSpace(remarks)
                ? $"Marked as Lost on {today:yyyy-MM-dd}. Overdue fine: {overdueFine:C}, Replacement penalty: {settings.LostBookPenaltyFee:C}."
                : remarks.Trim();

            // Total quantity decreases by 1 since book is lost permanently
            issue.Book.TotalQuantity = Math.Max(0, issue.Book.TotalQuantity - 1);
            if (issue.Book.AvailableQuantity > issue.Book.TotalQuantity)
                issue.Book.AvailableQuantity = issue.Book.TotalQuantity;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "This loan or inventory changed while processing. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return (false, "Database rule rejected marking book as lost. Verify current inventory.");
        }
        catch (DbException)
        {
            return (false, "Database communication error while updating lost book status.");
        }
    }

    public IQueryable<IssueRecord> Search(string? term, bool overdueOnly = false, IssueStatus? status = null)
    {
        IQueryable<IssueRecord> query = db.IssueRecords.AsNoTracking().Include(issue => issue.Book)
            .Include(issue => issue.Member).ThenInclude(member => member.User)
            .Include(issue => issue.FinePayments);

        var today = calendar.Today;
        if (overdueOnly)
            query = query.Where(issue => issue.Status == IssueStatus.Active && issue.DueDateUtc < today);
        else if (status.HasValue)
            query = query.Where(issue => issue.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var value = term.Trim();
            query = query.Where(issue => issue.Book.Title.Contains(value) || issue.Member.FullName.Contains(value)
                || issue.Member.User.Email!.Contains(value) || (issue.Book.Isbn != null && issue.Book.Isbn.Contains(value)));
        }
        return query.OrderByDescending(issue => issue.IssueDateUtc);
    }

    public IQueryable<IssueRecord> ForMember(string userId) =>
        db.IssueRecords.AsNoTracking().Include(issue => issue.Book).Include(issue => issue.Member)
            .Include(issue => issue.FinePayments)
            .Where(issue => issue.Member.UserId == userId).OrderByDescending(issue => issue.IssueDateUtc);

    public async Task<(bool Success, string? Error)> RecordFinePaymentAsync(
        int issueId,
        decimal amount,
        FinePaymentMethod method,
        string? referenceNumber,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return (false, "Payment must be greater than zero.");
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            return (false, "Payment amount must have no more than two decimal places.");
        if (!Enum.IsDefined(typeof(FinePaymentMethod), method))
            return (false, "Select a valid payment method.");

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            var issue = await db.IssueRecords.FirstOrDefaultAsync(
                item => item.Id == issueId, cancellationToken);
            if (issue is null) return (false, "Issue record not found.");
            if (issue.Status == IssueStatus.Active)
                return (false, "Fine payments can only be recorded after the book is returned or settled as lost.");

            var remaining = issue.FineAmount - issue.FinePaidAmount;
            if (amount > remaining)
                return (false, $"Payment of {amount:C} exceeds the outstanding fine of {remaining:C}.");

            issue.FinePaidAmount += amount;
            db.FinePayments.Add(new FinePayment
            {
                IssueRecordId = issue.Id,
                Amount = amount,
                PaymentDateUtc = DateTimeOffset.UtcNow,
                PaymentMethod = method,
                ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim()
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "The fine balance was updated by another session. Refresh and verify the outstanding balance.");
        }
        catch (DbUpdateException)
        {
            return (false, "The fine payment could not be saved. Database rule rejected the payment.");
        }
        catch (DbException)
        {
            return (false, "The database could not confirm the payment transaction. Check payment history before retrying.");
        }
    }

    public async Task<List<FinePayment>> GetPaymentHistoryAsync(string? q = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var query = db.FinePayments.AsNoTracking()
            .Include(p => p.IssueRecord).ThenInclude(i => i.Book)
            .Include(p => p.IssueRecord).ThenInclude(i => i.Member).ThenInclude(m => m.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.IssueRecord.Member.FullName.Contains(term)
                || p.IssueRecord.Member.User.Email!.Contains(term)
                || p.IssueRecord.Book.Title.Contains(term)
                || (p.ReferenceNumber != null && p.ReferenceNumber.Contains(term)));
        }

        return await query.OrderByDescending(p => p.PaymentDateUtc).Take(limit).ToListAsync(cancellationToken);
    }
}
