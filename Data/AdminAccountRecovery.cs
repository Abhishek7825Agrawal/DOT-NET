using System.Data.Common;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

/// <summary>
/// Operator-invoked Admin diagnostics and recovery. Neither operation is an HTTP endpoint.
/// </summary>
public static class AdminAccountRecovery
{
    public const string AdminEmail = "admin@shelfwise.local";
    public const string RequiredConfirmation = "admin@shelfwise.local";

    public static async Task<bool> InspectAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminAccountRecovery");
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (!db.Database.IsSqlServer())
        {
            logger.LogError("Admin inspection requires SQL Server. No account was changed.");
            return false;
        }

        var connection = db.Database.GetDbConnection();
        logger.LogInformation("Admin inspection database target: provider=SqlServer; server={Server}; database={Database}",
            connection.DataSource, connection.Database);

        var user = await users.FindByEmailAsync(AdminEmail);
        if (user is null || !string.Equals(user.Email, AdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Admin account was not found by normalized email in the configured SQL Server database.");
            return false;
        }

        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(
            row => row.UserId == user.Id, cancellationToken);
        var isAdmin = await users.IsInRoleAsync(user, "Admin");
        var isLockedOut = await users.IsLockedOutAsync(user);
        logger.LogInformation(
            "Admin account found: isAdmin={IsAdmin}; usernameMatchesEmail={UsernameMatchesEmail}; emailConfirmed={EmailConfirmed}; hasPasswordHash={HasPasswordHash}; lockoutEnabled={LockoutEnabled}; isLockedOut={IsLockedOut}; lockoutEndIsSet={LockoutEndIsSet}; accessFailedCount={AccessFailedCount}; memberProfileExists={MemberProfileExists}; memberIsActive={MemberIsActive}",
            isAdmin,
            string.Equals(user.UserName, user.Email, StringComparison.OrdinalIgnoreCase),
            user.EmailConfirmed,
            !string.IsNullOrEmpty(user.PasswordHash),
            user.LockoutEnabled,
            isLockedOut,
            user.LockoutEnd.HasValue,
            user.AccessFailedCount,
            member is not null,
            member?.IsActive ?? true);

        return isAdmin;
    }

    public static async Task<bool> RecoverAsync(
        IServiceProvider services,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminAccountRecovery");
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (!db.Database.IsSqlServer())
        {
            logger.LogError("Admin recovery requires SQL Server. No account was changed.");
            return false;
        }
        if (string.IsNullOrEmpty(newPassword))
        {
            logger.LogError("Admin recovery password environment variable is missing. No account was changed.");
            return false;
        }

        var admin = await users.FindByEmailAsync(AdminEmail);
        if (admin is null || !string.Equals(admin.Email, AdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Admin recovery stopped: the target account was not found in the configured database.");
            return false;
        }
        if (!await users.IsInRoleAsync(admin, "Admin"))
        {
            logger.LogError("Admin recovery stopped: the target account does not have the Admin role.");
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var resetToken = await users.GeneratePasswordResetTokenAsync(admin);
        var passwordResult = await users.ResetPasswordAsync(admin, resetToken, newPassword);
        if (!passwordResult.Succeeded)
        {
            logger.LogError("Admin password was rejected by Identity policy. Error codes: {ErrorCodes}; no recovery was committed.",
                string.Join(",", passwordResult.Errors.Select(error => error.Code)));
            return false;
        }

        var lockoutResult = await users.SetLockoutEndDateAsync(admin, null);
        if (!lockoutResult.Succeeded)
        {
            logger.LogError("Admin recovery failed clearing lockout: {ErrorCodes}",
                string.Join(",", lockoutResult.Errors.Select(error => error.Code)));
            return false;
        }

        var failedCountResult = await users.ResetAccessFailedCountAsync(admin);
        if (!failedCountResult.Succeeded)
        {
            logger.LogError("Admin recovery failed resetting the failed-login counter: {ErrorCodes}",
                string.Join(",", failedCountResult.Errors.Select(error => error.Code)));
            return false;
        }

        var member = await db.Members.SingleOrDefaultAsync(row => row.UserId == admin.Id, cancellationToken);
        if (member is { IsActive: false })
        {
            member.IsActive = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var updatedAdmin = await users.FindByIdAsync(admin.Id);
        var verifiedMember = await db.Members.AsNoTracking().SingleOrDefaultAsync(
            row => row.UserId == admin.Id, cancellationToken);
        var isActive = verifiedMember?.IsActive ?? true;
        var passwordMatches = updatedAdmin is not null
            && await users.CheckPasswordAsync(updatedAdmin, newPassword);
        var verified = updatedAdmin is not null
            && passwordMatches
            && updatedAdmin.AccessFailedCount == 0
            && updatedAdmin.LockoutEnd is null
            && isActive
            && await users.IsInRoleAsync(updatedAdmin, "Admin");

        if (!verified)
        {
            logger.LogError("Admin recovery completed but final password/account-state verification failed.");
            return false;
        }

        logger.LogInformation(
            "Admin recovery succeeded: password verified; account active; AccessFailedCount=0; LockoutEnd=null. The password was not logged.");
        return true;
    }
}
