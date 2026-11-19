using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class MemberService(ApplicationDbContext db)
{
    public IQueryable<Member> Search(string? term, bool includeInactive = true)
    {
        IQueryable<Member> query = db.Members.AsNoTracking().Include(member => member.User);
        if (!includeInactive) query = query.Where(member => member.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var value = term.Trim();
            query = query.Where(member => member.FullName.Contains(value)
                || member.User.Email!.Contains(value) || (member.PhoneNumber != null && member.PhoneNumber.Contains(value)));
        }
        return query.OrderBy(member => member.FullName);
    }

    public Task<Member?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        db.Members.AsNoTracking().FirstOrDefaultAsync(member => member.UserId == userId, cancellationToken);

    public async Task<MemberDetailsViewModel?> GetDetailsAsync(
        int id,
        DateTime today,
        CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking()
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (member is null) return null;

        var settings = await db.LibrarySettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
        var graceDays = settings?.GracePeriodDays ?? 0;

        var issues = await db.IssueRecords.AsNoTracking()
            .Include(i => i.Book)
            .Include(i => i.FinePayments)
            .Where(i => i.MemberId == id)
            .OrderByDescending(i => i.IssueDateUtc)
            .ToListAsync(cancellationToken);

        var payments = await db.FinePayments.AsNoTracking()
            .Include(p => p.IssueRecord).ThenInclude(i => i.Book)
            .Where(p => p.IssueRecord.MemberId == id)
            .OrderByDescending(p => p.PaymentDateUtc)
            .ToListAsync(cancellationToken);

        var totalLoans = issues.Count;
        var activeLoans = issues.Count(i => i.Status == IssueStatus.Active);
        var returnedLoans = issues.Count(i => i.Status == IssueStatus.Returned);
        var overdueLoans = issues.Count(i => i.Status == IssueStatus.Active && i.DueDateUtc.Date < today);
        var outstandingFines = issues.Sum(i => FineCalculator.CalculateOutstandingBalance(i, today, graceDays));
        var totalFinesPaid = issues.Sum(i => i.FinePaidAmount);

        return new MemberDetailsViewModel
        {
            Member = member,
            TotalLoans = totalLoans,
            ActiveLoansCount = activeLoans,
            ReturnedBooksCount = returnedLoans,
            OverdueLoansCount = overdueLoans,
            OutstandingFines = outstandingFines,
            TotalFinesPaid = totalFinesPaid,
            CirculationHistory = issues,
            PaymentHistory = payments,
            Today = today
        };
    }

    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (member is null) return false;
        member.IsActive = active;
        member.User.LockoutEnabled = true;
        member.User.LockoutEnd = active ? null : DateTimeOffset.MaxValue;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
