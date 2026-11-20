using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public sealed class DbInitializer(
    ApplicationDbContext db,
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSqlitePhase7SchemaAsync(cancellationToken);
        }
        else
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        foreach (var role in new[] { "Admin", "Staff", "Member" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        if (!await db.LibrarySettings.AnyAsync(cancellationToken))
        {
            db.LibrarySettings.Add(new LibrarySetting
            {
                LoanPeriodDays = 14,
                FineRatePerDay = 5.00m,
                MaxBooksPerMember = 5,
                LostBookPenaltyFee = 250.00m,
                GracePeriodDays = 0,
                TimeZoneId = "Asia/Kolkata",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.MarketplaceSettings.AnyAsync(cancellationToken))
        {
            db.MarketplaceSettings.Add(new MarketplaceSettings
            {
                PlatformCommissionRate = 15.00m,
                RequireAdminApproval = true,
                MinimumListingPrice = 0.00m,
                MaximumListingPrice = 25000.00m,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.Categories.AnyAsync(cancellationToken))
        {
            db.Categories.AddRange(
                new Category { Name = "Computer Science", Description = "Software engineering, algorithms, architectures, and programming" },
                new Category { Name = "Science & Technology", Description = "Physics, chemistry, biology, mathematics, and engineering" },
                new Category { Name = "Literature & Fiction", Description = "Classic literature, modern fiction, drama, and poetry" },
                new Category { Name = "Business & Economics", Description = "Finance, management, entrepreneurship, and economics" },
                new Category { Name = "History & Philosophy", Description = "World history, philosophical treatises, and civilizations" },
                new Category { Name = "Self-Help & Biography", Description = "Personal development, leadership, and notable biographies" }
            );
            await db.SaveChangesAsync(cancellationToken);
        }

        var adminEmail = Environment.GetEnvironmentVariable("LIBRARY_ADMIN_EMAIL");
        var adminPassword = Environment.GetEnvironmentVariable("LIBRARY_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(adminEmail)) adminEmail = "admin@shelfwise.local";

        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogWarning("No Admin account exists. Set LIBRARY_ADMIN_PASSWORD to provision the initial Admin account.");
            }
            else
            {
                admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                var created = await userManager.CreateAsync(admin, adminPassword);
                if (!created.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Description)));
            }
        }

        if (admin is not null && !await userManager.IsInRoleAsync(admin, "Admin"))
        {
            var assigned = await userManager.AddToRoleAsync(admin, "Admin");
            if (!assigned.Succeeded)
                throw new InvalidOperationException(string.Join("; ", assigned.Errors.Select(error => error.Description)));
        }

        // Create the optional operations account only when a deployment supplies
        // its initial secret. Never create a reusable default credential.
        var staffEmail = "staff@shelfwise.local";
        var staffUser = await userManager.FindByEmailAsync(staffEmail);
        if (staffUser is null)
        {
            var staffPass = Environment.GetEnvironmentVariable("LIBRARY_STAFF_PASSWORD");
            if (!string.IsNullOrWhiteSpace(staffPass))
            {
                staffUser = new ApplicationUser { UserName = staffEmail, Email = staffEmail, EmailConfirmed = true };
                var created = await userManager.CreateAsync(staffUser, staffPass);
                if (!created.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Description)));
                var assigned = await userManager.AddToRoleAsync(staffUser, "Staff");
                if (!assigned.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", assigned.Errors.Select(error => error.Description)));
            }
        }
        else if (!await userManager.IsInRoleAsync(staffUser, "Staff"))
        {
            await userManager.AddToRoleAsync(staffUser, "Staff");
        }

        // Provision sample member for demonstrations
        var memberEmail = "member@shelfwise.local";
        var sampleMemberPassword = Environment.GetEnvironmentVariable("LIBRARY_SAMPLE_MEMBER_PASSWORD");
        var sampleUser = await userManager.FindByEmailAsync(memberEmail);
        Member? memberProfile = null;
        if (sampleUser is null && !string.IsNullOrWhiteSpace(sampleMemberPassword))
        {
            sampleUser = new ApplicationUser { UserName = memberEmail, Email = memberEmail, EmailConfirmed = true };
            var created = await userManager.CreateAsync(sampleUser, sampleMemberPassword);
            if (created.Succeeded)
            {
                await userManager.AddToRoleAsync(sampleUser, "Member");
                memberProfile = new Member
                {
                    UserId = sampleUser.Id,
                    FullName = "Rahul Sharma",
                    PhoneNumber = "+91 98765 43210",
                    Address = "Sector 14, Gurugram, Haryana",
                    IsActive = true,
                    RegisteredAtUtc = DateTime.UtcNow.AddMonths(-2)
                };
                db.Members.Add(memberProfile);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        else if (sampleUser is not null)
        {
            memberProfile = await db.Members.FirstOrDefaultAsync(m => m.UserId == sampleUser.Id, cancellationToken);
        }

        // Provision rich sample books for demonstrations if catalogue is empty
        if (!await db.Books.AnyAsync(cancellationToken))
        {
            var sampleBooks = new List<Book>
            {
                new Book
                {
                    Title = "Clean Code: A Handbook of Agile Software Craftsmanship",
                    Author = "Robert C. Martin",
                    Isbn = "9780132350884",
                    Category = "Computer Science",
                    Publisher = "Prentice Hall",
                    PublicationYear = 2008,
                    Description = "Even bad code can function. But if code isn't clean, it can bring a development organization to its knees. This book presents revolutionary techniques to produce clean, maintainable code.",
                    TotalQuantity = 5,
                    AvailableQuantity = 4,
                    IsActive = true
                },
                new Book
                {
                    Title = "The Pragmatic Programmer: Your Journey to Mastery",
                    Author = "David Thomas, Andrew Hunt",
                    Isbn = "9780135957059",
                    Category = "Computer Science",
                    Publisher = "Addison-Wesley Professional",
                    PublicationYear = 2019,
                    Description = "One of the most significant books in modern software engineering, covering core career lessons, architecture, and craftsmanship.",
                    TotalQuantity = 4,
                    AvailableQuantity = 4,
                    IsActive = true
                },
                new Book
                {
                    Title = "Introduction to Algorithms, Fourth Edition",
                    Author = "Thomas H. Cormen, Charles E. Leiserson, Ronald L. Rivest, Clifford Stein",
                    Isbn = "9780262046305",
                    Category = "Computer Science",
                    Publisher = "MIT Press",
                    PublicationYear = 2022,
                    Description = "A comprehensive textbook and standard reference on modern computer algorithms.",
                    TotalQuantity = 6,
                    AvailableQuantity = 6,
                    IsActive = true
                },
                new Book
                {
                    Title = "Design Patterns: Elements of Reusable Object-Oriented Software",
                    Author = "Erich Gamma, Richard Helm, Ralph Johnson, John Vlissides",
                    Isbn = "9780201633610",
                    Category = "Computer Science",
                    Publisher = "Addison-Wesley",
                    PublicationYear = 1994,
                    Description = "The seminal Gang of Four catalog of 23 classic software design patterns.",
                    TotalQuantity = 3,
                    AvailableQuantity = 3,
                    IsActive = true
                },
                new Book
                {
                    Title = "Atomic Habits: An Easy & Proven Way to Build Good Habits",
                    Author = "James Clear",
                    Isbn = "9780735211292",
                    Category = "Self-Help & Biography",
                    Publisher = "Avery",
                    PublicationYear = 2018,
                    Description = "A proven framework for improving every day. James Clear reveals practical strategies on tiny changes that lead to remarkable results.",
                    TotalQuantity = 8,
                    AvailableQuantity = 7,
                    IsActive = true
                },
                new Book
                {
                    Title = "Zero to One: Notes on Startups, or How to Build the Future",
                    Author = "Peter Thiel, Blake Masters",
                    Isbn = "9780804139298",
                    Category = "Business & Economics",
                    Publisher = "Crown Business",
                    PublicationYear = 2014,
                    Description = "The great secret of our time is that there are still uncharted frontiers to explore and new inventions to create.",
                    TotalQuantity = 4,
                    AvailableQuantity = 4,
                    IsActive = true
                },
                new Book
                {
                    Title = "A Brief History of Time",
                    Author = "Stephen Hawking",
                    Isbn = "9780553380163",
                    Category = "Science & Technology",
                    Publisher = "Bantam",
                    PublicationYear = 1998,
                    Description = "A landmark volume in science writing by one of the great minds of our time exploring black holes, time, and space.",
                    TotalQuantity = 5,
                    AvailableQuantity = 5,
                    IsActive = true
                },
                new Book
                {
                    Title = "Sapiens: A Brief History of Humankind",
                    Author = "Yuval Noah Harari",
                    Isbn = "9780062316097",
                    Category = "History & Philosophy",
                    Publisher = "Harper",
                    PublicationYear = 2014,
                    Description = "Explores how biology and history have defined us and enhanced our understanding of what it means to be human.",
                    TotalQuantity = 4,
                    AvailableQuantity = 4,
                    IsActive = true
                }
            };

            db.Books.AddRange(sampleBooks);
            await db.SaveChangesAsync(cancellationToken);

            // Seed demonstration circulation records
            if (memberProfile != null)
            {
                var cleanCode = sampleBooks.First(b => b.Isbn == "9780132350884");
                var atomicHabits = sampleBooks.First(b => b.Isbn == "9780735211292");
                var algorithms = sampleBooks.First(b => b.Isbn == "9780262046305");

                var today = DateTime.UtcNow.Date;

                // 1. Active Loan (Clean Code): issued 4 days ago, due in 10 days
                var activeLoan = new IssueRecord
                {
                    MemberId = memberProfile.Id,
                    BookId = cleanCode.Id,
                    IssueDateUtc = today.AddDays(-4),
                    DueDateUtc = today.AddDays(10),
                    Status = IssueStatus.Active,
                    FineRatePerDay = 5.00m,
                    FineAmount = 0m,
                    FinePaidAmount = 0m
                };

                // 2. Overdue Loan (Atomic Habits): issued 20 days ago, due 6 days ago -> overdue!
                var overdueLoan = new IssueRecord
                {
                    MemberId = memberProfile.Id,
                    BookId = atomicHabits.Id,
                    IssueDateUtc = today.AddDays(-20),
                    DueDateUtc = today.AddDays(-6),
                    Status = IssueStatus.Active,
                    FineRatePerDay = 5.00m,
                    FineAmount = 0m,
                    FinePaidAmount = 0m
                };

                // 3. Returned Loan with Paid Fine (Algorithms): issued 30 days ago, due 16 days ago, returned 7 days ago (9 days overdue @ Rs 5 = Rs 45 fine)
                var returnedLoan = new IssueRecord
                {
                    MemberId = memberProfile.Id,
                    BookId = algorithms.Id,
                    IssueDateUtc = today.AddDays(-30),
                    DueDateUtc = today.AddDays(-16),
                    ReturnDateUtc = today.AddDays(-7),
                    Status = IssueStatus.Returned,
                    FineRatePerDay = 5.00m,
                    FineAmount = 45.00m,
                    FinePaidAmount = 45.00m,
                    Remarks = "Returned with complete fine payment in cash."
                };

                db.IssueRecords.AddRange(activeLoan, overdueLoan, returnedLoan);
                await db.SaveChangesAsync(cancellationToken);

                // Record payment for the returned loan
                db.FinePayments.Add(new FinePayment
                {
                    IssueRecordId = returnedLoan.Id,
                    Amount = 45.00m,
                    PaymentDateUtc = DateTimeOffset.UtcNow.AddDays(-7),
                    PaymentMethod = FinePaymentMethod.Cash,
                    ReferenceNumber = "REC-2026-001"
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task EnsureSqlitePhase7SchemaAsync(CancellationToken cancellationToken)
    {
        // EnsureCreated is a no-op for an existing SQLite database. Add the
        // Phase 7A structures explicitly so upgrades preserve existing data.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var hasIsActive = false;
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('AspNetUsers')";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    if (string.Equals(reader.GetString(1), "IsActive", StringComparison.OrdinalIgnoreCase))
                        hasIsActive = true;
            }
            if (!hasIsActive)
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AspNetUsers ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1",
                    cancellationToken);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS UserActivityLogs (
                    Id INTEGER NOT NULL CONSTRAINT PK_UserActivityLogs PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL, UserRole TEXT NOT NULL, ActivityType TEXT NOT NULL,
                    Category TEXT NOT NULL, Description TEXT NOT NULL, Status TEXT NOT NULL,
                    EntityType TEXT NULL, EntityReference TEXT NULL, ContextJson TEXT NULL,
                    CorrelationId TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS SecurityAuditLogs (
                    Id INTEGER NOT NULL CONSTRAINT PK_SecurityAuditLogs PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NULL, UserRole TEXT NULL, EventType TEXT NOT NULL,
                    Category TEXT NOT NULL, Severity TEXT NOT NULL, Outcome TEXT NOT NULL,
                    Description TEXT NOT NULL, EntityType TEXT NULL, EntityReference TEXT NULL,
                    RequestPath TEXT NULL, CorrelationId TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_UserActivityLogs_UserId_CreatedAtUtc ON UserActivityLogs(UserId, CreatedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_UserActivityLogs_ActivityType_CreatedAtUtc ON UserActivityLogs(ActivityType, CreatedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_UserActivityLogs_CorrelationId ON UserActivityLogs(CorrelationId);
                CREATE INDEX IF NOT EXISTS IX_SecurityAuditLogs_UserId_CreatedAtUtc ON SecurityAuditLogs(UserId, CreatedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_SecurityAuditLogs_Severity_CreatedAtUtc ON SecurityAuditLogs(Severity, CreatedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_SecurityAuditLogs_EventType_CreatedAtUtc ON SecurityAuditLogs(EventType, CreatedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_SecurityAuditLogs_CorrelationId ON SecurityAuditLogs(CorrelationId);
                """, cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
