using ClosedXML.Excel;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Tests;

public static class ExcelImportTests
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
        Console.WriteLine("  PHASE 4: EXCEL BULK IMPORT & VALIDATION TESTS   ");
        Console.WriteLine("--------------------------------------------------");

        try
        {
            // 1. Formula Injection Sanitization Tests (CWE-1236)
            Assert(CustomerImportService.SanitizeFormulaInjection("=SUM(A1:B10)") == "'=SUM(A1:B10)",
                "Formula injection: '=' prepended with single quote");
            Assert(CustomerImportService.SanitizeFormulaInjection("+123456789") == "'+123456789",
                "Formula injection: '+' prepended with single quote");
            Assert(CustomerImportService.SanitizeFormulaInjection("-500.00") == "'-500.00",
                "Formula injection: '-' prepended with single quote");
            Assert(CustomerImportService.SanitizeFormulaInjection("@cmd|' /C calc'!A0") == "'@cmd|' /C calc'!A0",
                "Formula injection: '@' prepended with single quote");
            Assert(CustomerImportService.SanitizeFormulaInjection("Jane Austen") == "Jane Austen",
                "Normal text without dangerous prefix remains unaltered");
            Assert(CustomerImportService.SanitizeFormulaInjection(null) == string.Empty,
                "Null input returns empty string");
            Assert(CustomerImportService.SanitizeFormulaInjection("   ") == string.Empty,
                "Whitespace-only input returns empty string");

            // 2. Cryptographic Password Generator Tests
            var samplePasswords = Enumerable.Range(0, 10)
                .Select(_ => CustomerImportService.GenerateSecureRandomPassword())
                .ToList();

            var allHaveLength32 = samplePasswords.All(p => p.Length == 32);
            var allHaveUpper = samplePasswords.All(p => p.Any(char.IsUpper));
            var allHaveLower = samplePasswords.All(p => p.Any(char.IsLower));
            var allHaveDigit = samplePasswords.All(p => p.Any(char.IsDigit));
            var allHaveSpecial = samplePasswords.All(p => p.Any(ch => "!@#$%^&*()-_=+".Contains(ch)));
            var allUnique = samplePasswords.Distinct().Count() == 10;

            Assert(allHaveLength32, "Cryptographic password generator produces 32-character secrets");
            Assert(allHaveUpper && allHaveLower && allHaveDigit && allHaveSpecial,
                "Cryptographic passwords satisfy complex Identity requirements (uppercase, lowercase, digit, symbol)");
            Assert(allUnique, "Cryptographic password generator produces unique secrets across invocations");

            // 3. Excel Template Generation Tests
            using var db = CreateInMemoryDbContext();
            var service = new CustomerImportService(db, null!);
            var templateBytes = service.GenerateTemplate();

            Assert(templateBytes != null && templateBytes.Length > 1000,
                "Generated Excel template is a valid non-empty byte array (> 1 KB)");

            using var templateStream = new MemoryStream(templateBytes!);
            using var templateWb = new XLWorkbook(templateStream);

            Assert(templateWb.Worksheets.Contains("Instructions"), "Template contains 'Instructions' worksheet");
            Assert(templateWb.Worksheets.Contains("Members"), "Template contains 'Members' data worksheet");

            var membersSheet = templateWb.Worksheet("Members");
            Assert(membersSheet.Cell(1, 1).GetString().Contains("Full Name"), "Header 1 is 'Full Name'");
            Assert(membersSheet.Cell(1, 2).GetString().Contains("Email Address"), "Header 2 is 'Email Address'");
            Assert(membersSheet.Cell(1, 3).GetString().Contains("Phone Number"), "Header 3 is 'Phone Number'");
            Assert(membersSheet.Cell(1, 4).GetString().Contains("Address"), "Header 4 is 'Address'");

            // 4. Staged Excel Parsing, Row Validation, and Duplicate Detection
            using var uploadWb = new XLWorkbook();
            var uploadSheet = uploadWb.Worksheets.Add("Members");

            uploadSheet.Cell(1, 1).Value = "Full Name *";
            uploadSheet.Cell(1, 2).Value = "Email Address *";
            uploadSheet.Cell(1, 3).Value = "Phone Number";
            uploadSheet.Cell(1, 4).Value = "Residential Address";

            // Row 2: Valid Row
            uploadSheet.Cell(2, 1).Value = "Alice Walker";
            uploadSheet.Cell(2, 2).Value = "alice.walker@example.org";
            uploadSheet.Cell(2, 3).Value = "+1 555-019-2834";
            uploadSheet.Cell(2, 4).Value = "10 Downing St";

            // Row 3: Invalid (Missing Name)
            uploadSheet.Cell(3, 1).Value = "";
            uploadSheet.Cell(3, 2).Value = "noname@example.com";
            uploadSheet.Cell(3, 3).Value = "+1 555-999-8888";

            // Row 4: Invalid (Malformed Email)
            uploadSheet.Cell(4, 1).Value = "Bob Smith";
            uploadSheet.Cell(4, 2).Value = "not-an-email";
            uploadSheet.Cell(4, 3).Value = "+1 555-777-6666";

            // Row 5: Duplicate in file (Same email as Row 2)
            uploadSheet.Cell(5, 1).Value = "Alice Walker Clone";
            uploadSheet.Cell(5, 2).Value = "alice.walker@example.org";
            uploadSheet.Cell(5, 3).Value = "+1 555-444-3333";

            // Row 6: Duplicate in file (Same phone as Row 2)
            uploadSheet.Cell(6, 1).Value = "Charlie Green";
            uploadSheet.Cell(6, 2).Value = "charlie.green@example.org";
            uploadSheet.Cell(6, 3).Value = "+1 555-019-2834";

            // Row 7: Valid Row 2
            uploadSheet.Cell(7, 1).Value = "David Copperfield";
            uploadSheet.Cell(7, 2).Value = "david.copperfield@example.org";
            uploadSheet.Cell(7, 3).Value = "+44 20 7946 0991";

            using var uploadStream = new MemoryStream();
            uploadWb.SaveAs(uploadStream);
            uploadStream.Position = 0;

            var batch = service.ValidateAndStageExcelAsync(
                uploadStream,
                "Test_Bulk_Upload.xlsx",
                "staff-test-user-id")
                .GetAwaiter().GetResult();

            Assert(batch.TotalRows == 6, $"Parsed 6 non-empty rows (got {batch.TotalRows})");
            Assert(batch.ValidRowsCount == 2, $"Detected exactly 2 Valid rows (got {batch.ValidRowsCount})");
            Assert(batch.InvalidRowsCount == 2, $"Detected exactly 2 Invalid rows (got {batch.InvalidRowsCount})");
            Assert(batch.DuplicateRowsCount == 2, $"Detected exactly 2 Duplicate rows (got {batch.DuplicateRowsCount})");

            var row3 = batch.Rows.First(r => r.RowNumber == 3);
            Assert(row3.Status == ImportRowStatus.Invalid && row3.ValidationMessage!.Contains("Full Name is required"),
                "Row 3 identified as Invalid: Full Name is required");

            var row4 = batch.Rows.First(r => r.RowNumber == 4);
            Assert(row4.Status == ImportRowStatus.Invalid && row4.ValidationMessage!.Contains("email"),
                "Row 4 identified as Invalid: Invalid email format");

            var row5 = batch.Rows.First(r => r.RowNumber == 5);
            Assert(row5.Status == ImportRowStatus.Duplicate && row5.ValidationMessage!.Contains("Duplicate email"),
                "Row 5 identified as Duplicate: Duplicate email address within spreadsheet");

            var row6 = batch.Rows.First(r => r.RowNumber == 6);
            Assert(row6.Status == ImportRowStatus.Duplicate && row6.ValidationMessage!.Contains("Duplicate phone"),
                "Row 6 identified as Duplicate: Duplicate phone number within spreadsheet");

            // 5. Error Report Generation Test
            var errorReportBytes = service.GenerateErrorReport(batch);
            Assert(errorReportBytes != null && errorReportBytes.Length > 1000,
                "Error report generated with ClosedXML (> 1 KB)");

            using var errorStream = new MemoryStream(errorReportBytes!);
            using var errorWb = new XLWorkbook(errorStream);
            var errSheet = errorWb.Worksheets.First();
            var errRowsCount = errSheet.LastRowUsed()?.RowNumber() ?? 0;
            // 4 header rows (title, subtitle, empty, columns) + 4 rejected rows = 8 rows total
            Assert(errRowsCount >= 8, $"Error report sheet contains all 4 rejected entries (row count {errRowsCount})");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[EXCEPTION] Unexpected failure in ExcelImportTests: {ex.Message}\n{ex.StackTrace}");
            Console.ResetColor();
            failed++;
        }

        passedCount = passed;
        failedCount = failed;
        return failed == 0;
    }

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        db.Users.Add(new ApplicationUser
        {
            Id = "staff-test-user-id",
            UserName = "staff@shelfwise.test",
            NormalizedUserName = "STAFF@SHELFWISE.TEST",
            Email = "staff@shelfwise.test",
            NormalizedEmail = "STAFF@SHELFWISE.TEST"
        });
        db.SaveChanges();

        return db;
    }
}
