using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;

if (args.Contains("--run-tests", StringComparer.Ordinal))
{
    var passed = LibraryManagementSystem.Tests.BusinessLogicTests.Run();
    Environment.Exit(passed ? 0 : 1);
    return;
}

var builder = WebApplication.CreateBuilder(args);

var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "obj", "dataprotection");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("ShelfwiseLibrary");

var dbProvider = builder.Configuration.GetValue<string>("DatabaseProvider");

if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException(
            "DatabaseProvider is SqlServer but ConnectionStrings__DefaultConnection is not set in the environment.");

    builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
}
else if (string.IsNullOrWhiteSpace(dbProvider)
         || string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    var sqliteConnection = string.IsNullOrWhiteSpace(connectionString) || !connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
        ? "Data Source=library.db"
        : connectionString;
    builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(sqliteConnection));
}
else
{
    throw new InvalidOperationException($"Unsupported DatabaseProvider '{dbProvider}'. Use SqlServer or Sqlite.");
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "LibraryManagement.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddScoped<DbInitializer>();
builder.Services.AddScoped<BookService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<CirculationService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddSingleton<LibraryCalendar>();

var app = builder.Build();

if (args.Contains("--inspect-admin-account", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment()
        || !string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogError("Admin inspection is available only in Development with DatabaseProvider=SqlServer.");
        Environment.Exit(1);
        return;
    }

    try
    {
        await using var inspectionScope = app.Services.CreateAsyncScope();
        var inspected = await AdminAccountRecovery.InspectAsync(inspectionScope.ServiceProvider);
        Environment.Exit(inspected ? 0 : 1);
    }
    catch (Exception exception)
    {
        app.Logger.LogError("Admin inspection failed with {ExceptionType}; credentials were not logged.",
            exception.GetType().Name);
        Environment.Exit(1);
    }
    return;
}

if (args.Contains("--recover-admin-account", StringComparer.Ordinal))
{
    var confirmation = Environment.GetEnvironmentVariable("SHELFWISE_CONFIRM_ADMIN_RECOVERY");
    if (!app.Environment.IsDevelopment()
        || !string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(confirmation, AdminAccountRecovery.RequiredConfirmation, StringComparison.Ordinal))
    {
        app.Logger.LogError("Admin recovery requires Development, SQL Server, and SHELFWISE_CONFIRM_ADMIN_RECOVERY=admin@shelfwise.local. No account was changed.");
        Environment.Exit(1);
        return;
    }

    try
    {
        var recoveryPassword = Environment.GetEnvironmentVariable("SHELFWISE_ADMIN_RECOVERY_PASSWORD");
        await using var recoveryScope = app.Services.CreateAsyncScope();
        var reset = await AdminAccountRecovery.RecoverAsync(recoveryScope.ServiceProvider, recoveryPassword);
        Environment.Exit(reset ? 0 : 1);
    }
    catch (Exception exception)
    {
        app.Logger.LogError("Admin recovery failed with {ExceptionType}; passwords and credentials were not logged.",
            exception.GetType().Name);
        Environment.Exit(1);
    }
    return;
}

if (args.Contains("--run-sqlserver-smoke", StringComparer.Ordinal))
{
    var passed = await LibraryManagementSystem.Tests.SqlServerIntegrationSmokeTests.RunAsync(app.Services);
    Environment.Exit(passed ? 0 : 1);
    return;
}

if (args.Contains("--initialize-database", StringComparer.Ordinal))
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[INITIALIZATION COMPLETE] Database configured, roles seeded, and initial administrator provisioned.");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[DATABASE INITIALIZATION FAILED] Could not initialize database: {ex.Message}");
        Console.ResetColor();
        Environment.Exit(1);
    }
    return;
}

if (app.Environment.IsDevelopment())
{
    await using var devInitScope = app.Services.CreateAsyncScope();
    await devInitScope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<ActiveMemberMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
