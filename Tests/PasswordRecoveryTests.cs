using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Tests;

public static class PasswordRecoveryTests
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var passed = 0;
        var failed = 0;
        void Assert(bool value, string name)
        {
            Console.WriteLine(value ? $"[PASS] {name}" : $"[FAIL] {name}");
            if (value) passed++; else failed++;
        }

        var databasePath = Path.Combine(Path.GetTempPath(), $"shelfwise-password-test-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddScoped<DbInitializer>();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMilliseconds(250));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        try
        {
            await using var provider = services.BuildServiceProvider();
            string userId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                await roles.CreateAsync(new IdentityRole("Member"));
                var user = new ApplicationUser { UserName = "reader@example.test", Email = "reader@example.test" };
                var created = await users.CreateAsync(user, "InitialPass9!");
                await users.AddToRoleAsync(user, "Member");
                userId = user.Id;
                Assert(created.Succeeded, "Identity accepts a password that meets configured policy");

                var token = await users.GeneratePasswordResetTokenAsync(user);
                var reset = await users.ResetPasswordAsync(user, token, "ReplacementPass9!");
                Assert(reset.Succeeded, "A valid reset token changes the password");
                await users.UpdateSecurityStampAsync(user);
                var reuse = await users.ResetPasswordAsync(user, token, "ThirdPassword9!");
                Assert(!reuse.Succeeded, "A completed reset token cannot be reused");
                Assert(!await users.CheckPasswordAsync(user, "InitialPass9!")
                    && await users.CheckPasswordAsync(user, "ReplacementPass9!"),
                    "Old password is rejected and new password is accepted");

                var weak = await users.ResetPasswordAsync(user,
                    await users.GeneratePasswordResetTokenAsync(user), "weak");
                Assert(!weak.Succeeded, "Reset uses the configured Identity password rules");

                var expiringToken = await users.GeneratePasswordResetTokenAsync(user);
                await Task.Delay(350);
                var expired = await users.ResetPasswordAsync(user, expiringToken, "LaterPassword9!");
                Assert(!expired.Succeeded, "Expired reset tokens are rejected");
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var persisted = await users.FindByIdAsync(userId);
                Assert(persisted is not null && await users.CheckPasswordAsync(persisted, "ReplacementPass9!"),
                    "The new password persists after a fresh service scope and database reopen");
                var modelProperties = typeof(LibraryManagementSystem.ViewModels.AdminUserRowViewModel).GetProperties().Select(property => property.Name).ToArray();
                Assert(!modelProperties.Contains("Password") && !modelProperties.Contains("PasswordHash"),
                    "Admin account view models expose neither passwords nor password hashes");
            }

            var previousAdminEmail = Environment.GetEnvironmentVariable("LIBRARY_ADMIN_EMAIL");
            var previousAdminPassword = Environment.GetEnvironmentVariable("LIBRARY_ADMIN_PASSWORD");
            var previousStaffPassword = Environment.GetEnvironmentVariable("LIBRARY_STAFF_PASSWORD");
            var previousMemberPassword = Environment.GetEnvironmentVariable("LIBRARY_SAMPLE_MEMBER_PASSWORD");
            var adminEmail = $"admin-{Guid.NewGuid():N}@example.test";
            try
            {
                Environment.SetEnvironmentVariable("LIBRARY_ADMIN_EMAIL", adminEmail);
                Environment.SetEnvironmentVariable("LIBRARY_ADMIN_PASSWORD", "InitialAdminPass9!");
                Environment.SetEnvironmentVariable("LIBRARY_STAFF_PASSWORD", null);
                Environment.SetEnvironmentVariable("LIBRARY_SAMPLE_MEMBER_PASSWORD", null);
                await using (var scope = provider.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync();

                Environment.SetEnvironmentVariable("LIBRARY_ADMIN_PASSWORD", "ChangedSeedPass9!");
                await using (var scope = provider.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync();

                await using var verifyScope = provider.CreateAsyncScope();
                var users = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var seededAdmin = await users.FindByEmailAsync(adminEmail);
                Assert(seededAdmin is not null
                    && await users.CheckPasswordAsync(seededAdmin, "InitialAdminPass9!")
                    && !await users.CheckPasswordAsync(seededAdmin, "ChangedSeedPass9!"),
                    "Startup seeding preserves an existing Admin password");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LIBRARY_ADMIN_EMAIL", previousAdminEmail);
                Environment.SetEnvironmentVariable("LIBRARY_ADMIN_PASSWORD", previousAdminPassword);
                Environment.SetEnvironmentVariable("LIBRARY_STAFF_PASSWORD", previousStaffPassword);
                Environment.SetEnvironmentVariable("LIBRARY_SAMPLE_MEMBER_PASSWORD", previousMemberPassword);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }

        return (passed, failed);
    }
}
