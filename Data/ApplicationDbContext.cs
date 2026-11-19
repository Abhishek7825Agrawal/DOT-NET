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
    public DbSet<CustomerImportBatch> CustomerImportBatches => Set<CustomerImportBatch>();
    public DbSet<CustomerImportRow> CustomerImportRows => Set<CustomerImportRow>();
    public DbSet<DigitalBook> DigitalBooks => Set<DigitalBook>();
    public DbSet<UserNote> UserNotes => Set<UserNote>();
    public DbSet<BookAnnotation> BookAnnotations => Set<BookAnnotation>();
    public DbSet<ReadingProgress> ReadingProgresses => Set<ReadingProgress>();
    public DbSet<BookBookmark> BookBookmarks => Set<BookBookmark>();
    public DbSet<ResearchPaper> ResearchPapers => Set<ResearchPaper>();
    public DbSet<PaperVersion> PaperVersions => Set<PaperVersion>();
    public DbSet<PaperOrder> PaperOrders => Set<PaperOrder>();
    public DbSet<PaperCopyrightReport> PaperCopyrightReports => Set<PaperCopyrightReport>();
    public DbSet<MarketplaceSettings> MarketplaceSettings => Set<MarketplaceSettings>();

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

        builder.Entity<CustomerImportBatch>(entity =>
        {
            entity.HasIndex(b => b.BatchReference).IsUnique();
            entity.HasOne(b => b.StaffUser).WithMany()
                .HasForeignKey(b => b.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CustomerImportRow>(entity =>
        {
            entity.HasIndex(r => new { r.BatchId, r.RowNumber });
            entity.HasOne(r => r.Batch).WithMany(b => b.Rows)
                .HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DigitalBook>(entity =>
        {
            entity.HasIndex(d => d.BookId).IsUnique();
            entity.HasOne(d => d.Book).WithOne(b => b.DigitalEdition)
                .HasForeignKey<DigitalBook>(d => d.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserNote>(entity =>
        {
            entity.HasIndex(n => new { n.UserId, n.BookId });
            entity.HasOne(n => n.User).WithMany()
                .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(n => n.Book).WithMany(b => b.Notes)
                .HasForeignKey(n => n.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BookAnnotation>(entity =>
        {
            entity.HasIndex(a => new { a.DigitalBookId, a.UserId, a.PageNumber });
            entity.HasOne(a => a.User).WithMany()
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.DigitalBook).WithMany(d => d.Annotations)
                .HasForeignKey(a => a.DigitalBookId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.LinkedNote).WithMany()
                .HasForeignKey(a => a.LinkedNoteId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ReadingProgress>(entity =>
        {
            entity.Property(p => p.PercentComplete).HasPrecision(5, 2);
            entity.HasIndex(p => new { p.UserId, p.DigitalBookId }).IsUnique();
            entity.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(p => p.DigitalBook).WithMany(d => d.ReadingProgresses)
                .HasForeignKey(p => p.DigitalBookId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BookBookmark>(entity =>
        {
            entity.HasIndex(b => new { b.UserId, b.DigitalBookId, b.PageNumber });
            entity.HasOne(b => b.User).WithMany()
                .HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(b => b.DigitalBook).WithMany(d => d.Bookmarks)
                .HasForeignKey(b => b.DigitalBookId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ResearchPaper>(entity =>
        {
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.HasIndex(p => new { p.Status, p.Category });
            entity.HasIndex(p => p.Title);
            entity.HasOne(p => p.SubmitterUser).WithMany()
                .HasForeignKey(p => p.SubmitterUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PaperVersion>(entity =>
        {
            entity.HasIndex(v => new { v.PaperId, v.VersionNumber }).IsUnique();
            entity.HasOne(v => v.Paper).WithMany(p => p.Versions)
                .HasForeignKey(v => v.PaperId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PaperOrder>(entity =>
        {
            entity.Property(o => o.Amount).HasPrecision(18, 2);
            entity.Property(o => o.PlatformCommissionRate).HasPrecision(5, 2);
            entity.Property(o => o.PlatformCommissionAmount).HasPrecision(18, 2);
            entity.Property(o => o.AuthorEarningsAmount).HasPrecision(18, 2);

            entity.HasIndex(o => o.OrderNumber).IsUnique();
            entity.HasIndex(o => new { o.BuyerUserId, o.PaperId, o.PaymentStatus });

            entity.HasOne(o => o.Paper).WithMany(p => p.Orders)
                .HasForeignKey(o => o.PaperId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.BuyerUser).WithMany()
                .HasForeignKey(o => o.BuyerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PaperCopyrightReport>(entity =>
        {
            entity.HasIndex(r => new { r.PaperId, r.Status });
            entity.HasOne(r => r.Paper).WithMany(p => p.CopyrightReports)
                .HasForeignKey(r => r.PaperId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.ReporterUser).WithMany()
                .HasForeignKey(r => r.ReporterUserId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<MarketplaceSettings>(entity =>
        {
            entity.Property(s => s.PlatformCommissionRate).HasPrecision(5, 2);
            entity.Property(s => s.MinimumListingPrice).HasPrecision(18, 2);
            entity.Property(s => s.MaximumListingPrice).HasPrecision(18, 2);
        });
    }
}
