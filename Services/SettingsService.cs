using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class SettingsService(ApplicationDbContext db)
{
    public async Task<LibrarySetting> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.LibrarySettings.OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new LibrarySetting
            {
                LoanPeriodDays = 14,
                FineRatePerDay = 5.00m,
                MaxBooksPerMember = 5,
                LostBookPenaltyFee = 250.00m,
                GracePeriodDays = 0,
                TimeZoneId = "Asia/Kolkata",
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.LibrarySettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
        }
        return settings;
    }

    public async Task<(bool Success, string? Error)> UpdateSettingsAsync(
        LibrarySetting input,
        CancellationToken cancellationToken = default)
    {
        var settings = await db.LibrarySettings.OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new LibrarySetting();
            db.LibrarySettings.Add(settings);
        }

        if (input.LoanPeriodDays is < 1 or > 365)
            return (false, "Loan period must be between 1 and 365 days.");
        if (input.FineRatePerDay is < 0 or > 10000)
            return (false, "Daily fine rate must be between 0 and 10,000.");
        if (input.MaxBooksPerMember is < 1 or > 50)
            return (false, "Max books per member must be between 1 and 50.");
        if (input.LostBookPenaltyFee is < 0 or > 50000)
            return (false, "Lost book penalty fee must be between 0 and 50,000.");
        if (input.GracePeriodDays is < 0 or > 30)
            return (false, "Grace period must be between 0 and 30 days.");

        settings.LoanPeriodDays = input.LoanPeriodDays;
        settings.FineRatePerDay = input.FineRatePerDay;
        settings.MaxBooksPerMember = input.MaxBooksPerMember;
        settings.LostBookPenaltyFee = input.LostBookPenaltyFee;
        settings.GracePeriodDays = input.GracePeriodDays;
        settings.TimeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? "Asia/Kolkata" : input.TimeZoneId.Trim();
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

