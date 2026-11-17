using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<IssueRecord> IssueRecords => Set<IssueRecord>();
    public DbSet<FinePayment> FinePayments => Set<FinePayment>();
    public DbSet<LibrarySetting> LibrarySettings => Set<LibrarySetting>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (Database.IsSqlite())
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is Book book && (entry.State == EntityState.Added || entry.State == EntityState.Modified))
                {
                    book.RowVersion = Guid.NewGuid().ToByteArray();
                }
                else if (entry.Entity is IssueRecord issue && (entry.State == EntityState.Added || entry.State == EntityState.Modified))
                {
                    issue.RowVersion = Guid.NewGuid().ToByteArray();
                }
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var isSqlServer = Database.IsSqlServer();

        builder.Entity<Book>(entity =>
        {
            if (isSqlServer)
            {
                entity.HasIndex(book => book.Isbn).IsUnique().HasFilter("[Isbn] IS NOT NULL AND [IsActive] = 1");
                entity.Property(book => book.Isbn).IsUnicode(false);
                entity.Property(book => book.RowVersion).IsRowVersion();
                entity.ToTable(table => table.HasCheckConstraint("CK_Books_Quantities",
                    "[AvailableQuantity] >= 0 AND [TotalQuantity] >= [AvailableQuantity]"));
            }
            else
            {
                entity.HasIndex(book => book.Isbn).IsUnique().HasFilter("Isbn IS NOT NULL AND IsActive = 1");
                entity.Property(book => book.RowVersion).IsConcurrencyToken();
                entity.ToTable(table => table.HasCheckConstraint("CK_Books_Quantities",
                    "AvailableQuantity >= 0 AND TotalQuantity >= AvailableQuantity"));
            }
        });

        builder.Entity<Category>(entity =>
        {
            entity.HasIndex(category => category.Name).IsUnique();
        });

        builder.Entity<Member>().HasIndex(member => member.UserId).IsUnique();
        builder.Entity<Member>()
            .HasOne(member => member.User).WithOne(user => user.MemberProfile)
            .HasForeignKey<Member>(member => member.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<IssueRecord>(entity =>
        {
            entity.Property(issue => issue.FineRatePerDay).HasPrecision(10, 2);
            entity.Property(issue => issue.FineAmount).HasPrecision(10, 2);
            entity.Property(issue => issue.FinePaidAmount).HasPrecision(10, 2);

            if (isSqlServer)
            {
                entity.Property(issue => issue.RowVersion).IsRowVersion();
            }
            else
            {
                entity.Property(issue => issue.RowVersion).IsConcurrencyToken();
            }

            entity.HasIndex(issue => new { issue.Status, issue.DueDateUtc });
            entity.HasOne(issue => issue.Member).WithMany(member => member.IssueRecords)
                .HasForeignKey(issue => issue.MemberId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(issue => issue.Book).WithMany(book => book.IssueRecords)
                .HasForeignKey(issue => issue.BookId).OnDelete(DeleteBehavior.Restrict);

            if (isSqlServer)
            {
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_IssueRecords_DueAfterIssue", "[DueDateUtc] >= [IssueDateUtc]");
                    table.HasCheckConstraint("CK_IssueRecords_FineAmounts",
                        "[FineRatePerDay] >= 0 AND [FineAmount] >= 0 AND [FinePaidAmount] >= 0 AND [FinePaidAmount] <= [FineAmount]");
                    table.HasCheckConstraint("CK_IssueRecords_ReturnDate",
                        "([Status] = 0 AND [ReturnDateUtc] IS NULL) OR ([Status] IN (1, 2) AND [ReturnDateUtc] IS NOT NULL AND [ReturnDateUtc] >= [IssueDateUtc])");
                });
            }
            else
            {
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_IssueRecords_DueAfterIssue", "DueDateUtc >= IssueDateUtc");
                    table.HasCheckConstraint("CK_IssueRecords_FineAmounts",
                        "FineRatePerDay >= 0 AND FineAmount >= 0 AND FinePaidAmount >= 0 AND FinePaidAmount <= FineAmount");
                    table.HasCheckConstraint("CK_IssueRecords_ReturnDate",
                        "(Status = 0 AND ReturnDateUtc IS NULL) OR (Status IN (1, 2) AND ReturnDateUtc IS NOT NULL AND ReturnDateUtc >= IssueDateUtc)");
                });
            }
        });

        builder.Entity<FinePayment>(entity =>
        {
            entity.Property(payment => payment.Amount).HasPrecision(10, 2);
            entity.HasIndex(payment => new { payment.IssueRecordId, payment.PaymentDateUtc });
            entity.HasOne(payment => payment.IssueRecord).WithMany(issue => issue.FinePayments)
                .HasForeignKey(payment => payment.IssueRecordId).OnDelete(DeleteBehavior.Restrict);

            if (isSqlServer)
            {
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_FinePayments_Amount", "[Amount] > 0");
                    table.HasCheckConstraint("CK_FinePayments_Method", "[PaymentMethod] BETWEEN 0 AND 3");
                });
            }
            else
            {
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_FinePayments_Amount", "Amount > 0");
                    table.HasCheckConstraint("CK_FinePayments_Method", "PaymentMethod BETWEEN 0 AND 3");
                });
            }
        });

        builder.Entity<LibrarySetting>(entity =>
        {
            entity.Property(s => s.FineRatePerDay).HasPrecision(10, 2);
            entity.Property(s => s.LostBookPenaltyFee).HasPrecision(10, 2);
        });
    }
}
