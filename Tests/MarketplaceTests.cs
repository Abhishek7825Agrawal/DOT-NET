using System.Security.Cryptography;
using System.Text;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Tests;

public static class MarketplaceTests
{
    public static bool Run() => Run(out _, out _);

    public static bool Run(out int passedCount, out int failedCount)
    {
        var passed = 0;
        var failed = 0;

        void Assert(bool condition, string testName)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                Console.ResetColor();
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName}");
                Console.ResetColor();
                failed++;
            }
        }

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("  PHASE 6: RESEARCH PAPER MARKETPLACE TESTS       ");
        Console.WriteLine("--------------------------------------------------");

        try
        {
            // ==================================================
            // 1. Commission Calculation Logic
            // ==================================================
            var price1 = 500.00m;
            var commRate1 = 15.00m;
            var commAmt1 = Math.Round(price1 * (commRate1 / 100m), 2);
            var net1 = price1 - commAmt1;
            Assert(commAmt1 == 75.00m && net1 == 425.00m,
                "Commission Calculation: ₹500 at 15% yields ₹75.00 fee and ₹425.00 author net");

            var price2 = 1250.50m;
            var commRate2 = 12.50m;
            var commAmt2 = Math.Round(price2 * (commRate2 / 100m), 2);
            var net2 = price2 - commAmt2;
            Assert(commAmt2 == 156.31m && net2 == 1094.19m,
                "Commission Calculation: ₹1,250.50 at 12.5% yields ₹156.31 fee and ₹1,094.19 author net");

            var price3 = 0.00m;
            var commRate3 = 15.00m;
            var commAmt3 = Math.Round(price3 * (commRate3 / 100m), 2);
            var net3 = price3 - commAmt3;
            Assert(commAmt3 == 0.00m && net3 == 0.00m,
                "Commission Calculation: Open Access ₹0 manuscript yields ₹0 commission and ₹0 earnings");

            // ==================================================
            // 2. Storage Security & PDF Signature Validation
            // ==================================================
            var validPdfHeader = "%PDF-1.7\n%âãÏÓ\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF"u8.ToArray();
            Assert(PaperStorageService.ValidatePdfHeader(validPdfHeader),
                "Storage Security: Valid %PDF- header byte sequence accepted");

            var exeHeader = "MZ\x90\x00\x03\x00\x00\x00"u8.ToArray();
            Assert(!PaperStorageService.ValidatePdfHeader(exeHeader),
                "Storage Security: Executable MZ header rejected");

            var htmlHeader = "<html><head><title>Phish</title></head></html>"u8.ToArray();
            Assert(!PaperStorageService.ValidatePdfHeader(htmlHeader),
                "Storage Security: HTML document header rejected");

            var emptyHeader = Array.Empty<byte>();
            Assert(!PaperStorageService.ValidatePdfHeader(emptyHeader),
                "Storage Security: Empty byte payload rejected");

            // Test Path Traversal Protection
            var tempBaseDir = Path.Combine(Path.GetTempPath(), "ShelfwiseMarketplace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempBaseDir);

            try
            {
                var testEnv = new TestWebHostEnvironment { ContentRootPath = tempBaseDir };
                var storage = new PaperStorageService(testEnv);

                var standardPath = storage.GetVerifiedPaperPath("paper_abc123.pdf");
                var expectedDir = Path.GetFullPath(Path.Combine(tempBaseDir, "App_Data", "MarketplacePapers"));
                Assert(standardPath.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase),
                    "Storage Path Security: Valid paper file resolves inside App_Data/MarketplacePapers");

                var traversalPath = storage.GetVerifiedPaperPath("../../../etc/shadow");
                Assert(traversalPath.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase) &&
                       Path.GetFileName(traversalPath) == "shadow",
                    "Storage Path Security: Traversal attack '../' stripped to file name inside safe root");

                var emptyThrown = false;
                try
                {
                    storage.GetVerifiedPaperPath("   ");
                }
                catch (ArgumentException)
                {
                    emptyThrown = true;
                }
                Assert(emptyThrown, "Storage Path Security: Blank filename throws ArgumentException");
            }
            finally
            {
                if (Directory.Exists(tempBaseDir))
                {
                    try { Directory.Delete(tempBaseDir, true); } catch { }
                }
            }

            // ==================================================
            // 3. Database & Relational Integrity (In-Memory SQLite)
            // ==================================================
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            using var db = new ApplicationDbContext(options);
            db.Database.EnsureCreated();

            // Seed Users
            var adminUser = new ApplicationUser { Id = "admin-1", UserName = "admin@test.com", Email = "admin@test.com" };
            var authorUser = new ApplicationUser { Id = "author-1", UserName = "author@test.com", Email = "author@test.com" };
            var buyerUser = new ApplicationUser { Id = "buyer-1", UserName = "buyer@test.com", Email = "buyer@test.com" };
            var strangerUser = new ApplicationUser { Id = "stranger-1", UserName = "stranger@test.com", Email = "stranger@test.com" };

            db.Users.AddRange(adminUser, authorUser, buyerUser, strangerUser);
            db.MarketplaceSettings.Add(new MarketplaceSettings { Id = 1, PlatformCommissionRate = 15.0m });

            // Seed 2 Papers: Paper 1 = Free Open Access, Paper 2 = Paid ₹500
            var freePaper = new ResearchPaper
            {
                Id = 1,
                Title = "Open Access Quantum Computing Primer",
                Abstract = "An extensive open-access foundational overview of quantum circuits, qubits, and Grover's algorithm.",
                Authors = "Dr. Alice Turing",
                Affiliation = "Institute for Quantum Research",
                Category = "Physics",
                Keywords = "quantum, computing, circuits",
                IsPaid = false,
                Price = 0.00m,
                Status = PaperStatus.Approved,
                CurrentVersion = 1,
                StorageFileName = "paper_free_01.pdf",
                OriginalFileName = "quantum-primer.pdf",
                ChecksumSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                PageCount = 18,
                FileSizeBytes = 1048576,
                SubmitterUserId = authorUser.Id,
                CopyrightAccepted = true,
                CopyrightAcceptedAtUtc = DateTime.UtcNow
            };

            var paidPaper = new ResearchPaper
            {
                Id = 2,
                Title = "High-Frequency Algorithmic Execution Systems",
                Abstract = "Empirical study on sub-microsecond order execution architectures, limit order books, and latency arbitrage.",
                Authors = "Dr. Alice Turing, Marcus Vance",
                Affiliation = "Institute for Quantum Research",
                Category = "Economics & Finance",
                Keywords = "finance, algorithms, latency",
                IsPaid = true,
                Price = 500.00m,
                Status = PaperStatus.Approved,
                CurrentVersion = 1,
                StorageFileName = "paper_paid_02.pdf",
                OriginalFileName = "hft-execution.pdf",
                ChecksumSha256 = "872983ac12094571259837198273918237192837192837192837192837192837",
                PageCount = 42,
                FileSizeBytes = 4194304,
                SubmitterUserId = authorUser.Id,
                CopyrightAccepted = true,
                CopyrightAcceptedAtUtc = DateTime.UtcNow
            };

            db.ResearchPapers.AddRange(freePaper, paidPaper);
            db.SaveChanges();

            var marketplaceService = new MarketplaceService(db);

            // ==================================================
            // 4. Access Rights Matrix Tests
            // ==================================================
            // Admin always has access
            var adminAccessTask = marketplaceService.CanUserAccessPaperAsync(adminUser.Id, paidPaper.Id, isAdminOrStaff: true);
            adminAccessTask.Wait();
            Assert(adminAccessTask.Result, "Access Matrix: Admin user is granted access to paid paper unconditionally");

            // Author always has access to own paper
            var authorAccessTask = marketplaceService.CanUserAccessPaperAsync(authorUser.Id, paidPaper.Id, isAdminOrStaff: false);
            authorAccessTask.Wait();
            Assert(authorAccessTask.Result, "Access Matrix: Author is granted access to their own manuscript");

            // Stranger cannot access paid paper without order
            var strangerAccessTask = marketplaceService.CanUserAccessPaperAsync(strangerUser.Id, paidPaper.Id, isAdminOrStaff: false);
            strangerAccessTask.Wait();
            Assert(!strangerAccessTask.Result, "Access Matrix: Unregistered stranger is denied access to paid paper");

            // Open access papers are readable once approved
            var strangerFreeTask = marketplaceService.CanUserAccessPaperAsync(strangerUser.Id, freePaper.Id, isAdminOrStaff: false);
            strangerFreeTask.Wait();
            Assert(strangerFreeTask.Result, "Access Matrix: Open Access approved paper is accessible for reading");

            // ==================================================
            // 5. Self-Purchase & Duplicate Purchase Prevention
            // ==================================================
            // Author attempting to buy own paper
            var selfBuyTask = marketplaceService.CanUserPurchasePaperAsync(authorUser.Id, paidPaper.Id);
            selfBuyTask.Wait();
            var (canSelfBuy, selfBuyReason) = selfBuyTask.Result;
            Assert(!canSelfBuy && (selfBuyReason?.Contains("own", StringComparison.OrdinalIgnoreCase) == true),
                "Self-Purchase Prevention: Author is blocked from purchasing their own paper");

            // Non-owner attempting to buy paper before purchase
            var buyerCanBuyTask = marketplaceService.CanUserPurchasePaperAsync(buyerUser.Id, paidPaper.Id);
            buyerCanBuyTask.Wait();
            var (canBuyerBuy, _) = buyerCanBuyTask.Result;
            Assert(canBuyerBuy, "Purchase Check: First-time buyer is allowed to purchase manuscript");

            // ==================================================
            // 6. Free Manuscript Acquisition (Zero-Cost Completed Order)
            // ==================================================
            var freeOrderTask = marketplaceService.CreateOrderAsync(buyerUser.Id, freePaper.Id, "FreeAccess");
            freeOrderTask.Wait();
            var (freeSuccess, freeOrder, freeErr) = freeOrderTask.Result;

            Assert(freeSuccess && freeOrder != null, "Free Acquisition: Order successfully created for open-access paper");
            Assert(freeOrder?.Amount == 0.00m && freeOrder?.PaymentStatus == PaperPaymentStatus.Completed,
                "Free Acquisition: Order completed with ₹0.00 price and Completed payment status");

            // Now buyer has access to free paper
            var buyerFreeAccessTask = marketplaceService.CanUserAccessPaperAsync(buyerUser.Id, freePaper.Id, isAdminOrStaff: false);
            buyerFreeAccessTask.Wait();
            Assert(buyerFreeAccessTask.Result, "Access Matrix: Buyer granted access to free paper after completed acquisition");

            // Duplicate acquisition test
            var duplicateFreeTask = marketplaceService.CanUserPurchasePaperAsync(buyerUser.Id, freePaper.Id);
            duplicateFreeTask.Wait();
            var (canDupFree, dupFreeReason) = duplicateFreeTask.Result;
            Assert(!canDupFree && (dupFreeReason?.Contains("already", StringComparison.OrdinalIgnoreCase) == true),
                "Duplicate Purchase Prevention: Buyer is blocked from re-acquiring already-owned paper");

            // ==================================================
            // 7. Paid Order Creation & Pending Payment Security
            // ==================================================
            var paidOrderTask = marketplaceService.CreateOrderAsync(buyerUser.Id, paidPaper.Id, "GatewayPending");
            paidOrderTask.Wait();
            var (paidSuccess, paidOrder, paidErr) = paidOrderTask.Result;

            Assert(paidSuccess && paidOrder != null, "Paid Order: Order created with GatewayPending method");
            Assert(paidOrder?.PaymentStatus == PaperPaymentStatus.Pending,
                "Paid Order: Initial payment status is strictly Pending");
            Assert(paidOrder?.PlatformCommissionRate == 15.0m &&
                   paidOrder?.PlatformCommissionAmount == 75.00m &&
                   paidOrder?.AuthorEarningsAmount == 425.00m,
                "Paid Order: Financial snapshot accurately records 15% platform commission (₹75) and author earnings (₹425)");

            // CRITICAL TEST: Pending order MUST NOT grant access to paid PDF!
            var pendingAccessTask = marketplaceService.CanUserAccessPaperAsync(buyerUser.Id, paidPaper.Id, isAdminOrStaff: false);
            pendingAccessTask.Wait();
            Assert(!pendingAccessTask.Result,
                "CRITICAL SECURITY: Pending paid order strictly DENIES access to PDF document");

            // Simulate Gateway Settlement (Transition Pending -> Completed)
            paidOrder!.PaymentStatus = PaperPaymentStatus.Completed;
            paidPaper.PurchaseCount++;
            db.SaveChanges();

            var completedAccessTask = marketplaceService.CanUserAccessPaperAsync(buyerUser.Id, paidPaper.Id, isAdminOrStaff: false);
            completedAccessTask.Wait();
            Assert(completedAccessTask.Result,
                "Payment Settlement: Completed paid order successfully grants access to PDF document");

            // Author financial metrics verification
            var authorMetricsTask = marketplaceService.GetAuthorMetricsAsync(authorUser.Id);
            authorMetricsTask.Wait();
            var metrics = authorMetricsTask.Result;
            Assert(metrics.TotalSubmissions == 2, "Author Metrics: Correct total submissions count (2)");
            Assert(metrics.ApprovedCount == 2, "Author Metrics: Correct approved papers count (2)");
            Assert(metrics.TotalSalesCount == 2, "Author Metrics: Correct total acquisitions count (1 free + 1 paid)");
            Assert(metrics.GrossRevenue == 500.00m, "Author Metrics: Correct gross revenue (₹500.00)");
            Assert(metrics.TotalCommissionDeducted == 75.00m, "Author Metrics: Correct commission retained (₹75.00)");
            Assert(metrics.NetEarnings == 425.00m, "Author Metrics: Correct net author earnings (₹425.00)");

            // ==================================================
            // 8. Immutable Version History on Correction / Revision
            // ==================================================
            var initialVersions = db.PaperVersions.Count(v => v.PaperId == paidPaper.Id);
            Assert(initialVersions == 0, "Version History: No manual revision history prior to author correction");

            // Set paper to CorrectionRequested so author can submit revisions
            paidPaper.Status = PaperStatus.CorrectionRequested;
            db.SaveChanges();

            // Author submits revision
            var revisionTask = marketplaceService.SubmitCorrectionAsync(
                paidPaper.Id,
                authorUser.Id,
                newPdfMeta: null, // retain existing document
                title: "High-Frequency Algorithmic Execution Systems (2nd Revised Ed.)",
                abstractText: "Updated empirical study with microseconds analysis and novel latency benchmarks.",
                authors: paidPaper.Authors,
                affiliation: paidPaper.Affiliation,
                category: paidPaper.Category,
                keywords: paidPaper.Keywords + ", benchmarks",
                changeSummary: "Updated Section 4 benchmark tables and added latency comparative graphs."
            );
            revisionTask.Wait();

            var reloadedPaper = db.ResearchPapers.First(p => p.Id == paidPaper.Id);
            Assert(reloadedPaper.CurrentVersion == 2, "Version History: CurrentVersion incremented to 2");
            Assert(reloadedPaper.Status == PaperStatus.PendingReview, "Version History: Paper returned to PendingReview status");

            var loggedVersions = db.PaperVersions.Where(v => v.PaperId == paidPaper.Id).OrderBy(v => v.VersionNumber).ToList();
            Assert(loggedVersions.Count == 1, "Version History: Exactly 1 immutable version history entry recorded");
            Assert(loggedVersions[0].VersionNumber == 2 &&
                   loggedVersions[0].ChangeSummary?.Contains("benchmark", StringComparison.OrdinalIgnoreCase) == true,
                "Version History: Version record stores VersionNumber 2 and author's ChangeSummary");

            // ==================================================
            // 9. Copyright Report & Manuscript Suspension Flow
            // ==================================================
            var claim = new PaperCopyrightReport
            {
                PaperId = freePaper.Id,
                ReporterName = "Prof. Harold Finch",
                ReporterEmail = "finch@cambridge.ac.uk",
                InfringedWorkTitle = "Foundations of Quantum Algorithms (1998)",
                InfringementDescription = "Direct plagiarism of Chapter 3 proof regarding polynomial bounds.",
                GoodFaithDeclaration = true,
                Status = CopyrightReportStatus.UnderReview,
                ReportedAtUtc = DateTime.UtcNow
            };
            db.PaperCopyrightReports.Add(claim);
            db.SaveChanges();

            Assert(claim.Id > 0, "Copyright DMCA: Infringement claim created in UnderReview status");

            // Admin resolves claim: Upholds claim & suspends paper
            claim.Status = CopyrightReportStatus.UpheldSuspended;
            claim.ResolvedAtUtc = DateTime.UtcNow;
            claim.ResolvedByUserId = adminUser.Id;
            freePaper.Status = PaperStatus.Suspended;
            freePaper.EditorialFeedback = "Suspended due to copyright claim #" + claim.Id;
            db.SaveChanges();

            var suspendedPaper = db.ResearchPapers.First(p => p.Id == freePaper.Id);
            Assert(suspendedPaper.Status == PaperStatus.Suspended,
                "Manuscript Suspension: ResearchPaper status updated to Suspended");

            // Verify catalogue excludes suspended paper
            var catalogPapers = db.ResearchPapers.Where(p => p.Status == PaperStatus.Approved).ToList();
            Assert(!catalogPapers.Any(p => p.Id == freePaper.Id),
                "Catalogue Integrity: Suspended manuscript completely excluded from approved catalogue query");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[CRITICAL EXCEPTION] MarketplaceTests: {ex.Message}\n{ex.StackTrace}");
            Console.ResetColor();
            failed++;
        }

        passedCount = passed;
        failedCount = failed;
        Console.WriteLine($"  Phase 6 Sub-suite: {passed} passed, {failed} failed");
        return failed == 0;
    }
}
