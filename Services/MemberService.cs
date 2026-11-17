using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
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
