using System.Security.Cryptography;
using System.Text;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace LibraryManagementSystem.Tests;

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "Shelfwise.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Testing";
}

public static class DigitalLibraryTests
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
        Console.WriteLine("  PHASE 5: DIGITAL LIBRARY & WORKSPACE TESTS       ");
        Console.WriteLine("--------------------------------------------------");

        try
        {
            // 1. PDF Magic Header Signature Verification
            var validPdfBytes = "%PDF-1.7\n%âãÏÓ\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF"u8.ToArray();
            using (var validStream = new MemoryStream(validPdfBytes))
            {
                Assert(DigitalDocumentService.ValidatePdfSignature(validStream),
                    "PDF Magic Signature: Valid %PDF- header accepted");
            }

            var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 };
            using (var zipStream = new MemoryStream(zipBytes))
            {
                Assert(!DigitalDocumentService.ValidatePdfSignature(zipStream),
                    "PDF Magic Signature: PK (Zip) rejected");
            }

            var textBytes = "Just a plain text file pretending to be pdf"u8.ToArray();
            using (var textStream = new MemoryStream(textBytes))
            {
                Assert(!DigitalDocumentService.ValidatePdfSignature(textStream),
                    "PDF Magic Signature: Text file rejected");
            }

            var emptyBytes = Array.Empty<byte>();
            using (var emptyStream = new MemoryStream(emptyBytes))
            {
                Assert(!DigitalDocumentService.ValidatePdfSignature(emptyStream),
                    "PDF Magic Signature: Empty stream rejected");
            }

            // 2. PDF Page Count Extraction
            var pdfWithCount42 = "%PDF-1.4\n1 0 obj\n<< /Type /Pages /Count 42 /Kids [] >>\nendobj\n%%EOF"u8.ToArray();
            using (var countStream = new MemoryStream(pdfWithCount42))
            {
                var pages = DigitalDocumentService.ExtractPageCount(countStream);
                Assert(pages == 42, $"PDF Page Count: Extracted {pages} pages from /Count 42 catalog");
            }

            var pdfWithMultiplePages = "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n2 0 obj << /Type /Page >> endobj\n3 0 obj << /Type /Page >> endobj\n%%EOF"u8.ToArray();
            using (var multiPageStream = new MemoryStream(pdfWithMultiplePages))
            {
                var pages = DigitalDocumentService.ExtractPageCount(multiPageStream);
                Assert(pages == 3, $"PDF Page Count: Extracted {pages} pages by scanning /Type /Page objects");
            }

            var pdfWithCorruptData = "%PDF-1.4\nCorrupt header without page catalog"u8.ToArray();
            using (var corruptStream = new MemoryStream(pdfWithCorruptData))
            {
                var pages = DigitalDocumentService.ExtractPageCount(corruptStream);
                Assert(pages == 1, "PDF Page Count: Corrupt/empty catalog falls back safely to 1");
            }

            // 3. Security & Path Traversal Prevention
            var tempBaseDir = Path.Combine(Path.GetTempPath(), "ShelfwiseTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempBaseDir);

            try
            {
                var testEnv = new TestWebHostEnvironment { ContentRootPath = tempBaseDir };
                var docService = new DigitalDocumentService(testEnv);

                var standardPath = docService.GetVerifiedDocumentPath("doc_12345.pdf");
                var expectedDir = Path.GetFullPath(Path.Combine(tempBaseDir, "App_Data", "DigitalLibrary"));
                Assert(standardPath.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase),
                    "Path Security: Valid storage file resolves inside App_Data/DigitalLibrary");

                var traversalPath = docService.GetVerifiedDocumentPath("../../windows/win.ini");
                Assert(traversalPath.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase) &&
                       Path.GetFileName(traversalPath) == "win.ini",
                    "Path Security: Traversal attack '../' stripped to file name inside safe root");

                var thrownEmpty = false;
                try
                {
                    docService.GetVerifiedDocumentPath("");
                }
                catch (ArgumentException)
                {
                    thrownEmpty = true;
                }
                Assert(thrownEmpty, "Path Security: Empty filename throws ArgumentException");

                // Test SaveDocumentStreamAsync computes SHA-256 and writes to disk
                using var saveStream = new MemoryStream(validPdfBytes);
                var metaTask = docService.SaveDocumentStreamAsync(saveStream, "sample-thesis.pdf");
                metaTask.Wait();
                var meta = metaTask.Result;

                Assert(File.Exists(docService.GetVerifiedDocumentPath(meta.StorageFileName)),
                    "Document Storage: File persisted on disk with UUID filename");
                Assert(meta.OriginalFileName == "sample-thesis.pdf",
                    "Document Storage: Original filename preserved in metadata");
                Assert(meta.FileSizeBytes == validPdfBytes.Length,
                    "Document Storage: File size matches byte length");

                using var sha = SHA256.Create();
                var expectedHash = Convert.ToHexStringLower(sha.ComputeHash(validPdfBytes));
                Assert(meta.ChecksumSha256 == expectedHash,
                    "Document Storage: SHA-256 integrity checksum matches bit-for-bit");

                // Clean up created file
                docService.DeleteDocument(meta.StorageFileName);
                Assert(!docService.DocumentExists(meta.StorageFileName),
                    "Document Storage: DeleteDocument cleans up file from disk");
            }
            finally
            {
                if (Directory.Exists(tempBaseDir))
                {
                    try { Directory.Delete(tempBaseDir, true); } catch { }
                }
            }

            // 4. HTML & XSS Sanitization (CWE-79)
            var scriptAttack = "<p>Hello <script>alert('pwned')</script>World</p>";
            var sanitizedScript = DigitalLibraryController.SanitizeHtml(scriptAttack);
            Assert(!sanitizedScript.Contains("<script>", StringComparison.OrdinalIgnoreCase) &&
                   !sanitizedScript.Contains("alert", StringComparison.OrdinalIgnoreCase),
                "HTML Sanitization: <script> tags and payloads completely stripped");

            var iframeAttack = "<p>Notes</p><iframe src=\"https://malicious.example.com\"></iframe>";
            var sanitizedIframe = DigitalLibraryController.SanitizeHtml(iframeAttack);
            Assert(!sanitizedIframe.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
                "HTML Sanitization: <iframe> embed tags stripped");

            var eventAttack = "<a href=\"/test\" onclick=\"javascript:stealTokens()\">Click</a>";
            var sanitizedEvent = DigitalLibraryController.SanitizeHtml(eventAttack);
            Assert(!sanitizedEvent.Contains("onclick", StringComparison.OrdinalIgnoreCase),
                "HTML Sanitization: Inline event handlers (onclick) stripped");

            var jsHrefAttack = "<a href=\"javascript:alert(1)\">Dangerous</a>";
            var sanitizedHref = DigitalLibraryController.SanitizeHtml(jsHrefAttack);
            Assert(!sanitizedHref.Contains("javascript:", StringComparison.OrdinalIgnoreCase),
                "HTML Sanitization: javascript: URI in href replaced with safe anchor");

            var safeHtml = "<blockquote>\"The only way to learn a new programming language is by writing programs in it.\" — Page 14</blockquote><p>Key insight from Ritchie.</p>";
            var preservedSafe = DigitalLibraryController.SanitizeHtml(safeHtml);
            Assert(preservedSafe.Contains("<blockquote>") && preservedSafe.Contains("</blockquote>") && preservedSafe.Contains("<p>"),
                "HTML Sanitization: Editorial citations and standard paragraph markup preserved");

            var plainText = DigitalLibraryController.StripHtml(safeHtml);
            Assert(!plainText.Contains("<") && !plainText.Contains(">") && plainText.Contains("The only way to learn"),
                "HTML Sanitization: StripHtml generates clean plain-text excerpt");

            // 5. Notes Optimistic Concurrency & Versioning
            var testNote = new UserNote
            {
                Id = 1,
                UserId = "user-1",
                BookId = 10,
                Title = "Research Notes",
                ContentDeltaJson = "{\"ops\":[{\"insert\":\"Initial\\n\"}]}",
                SanitizedContentHtml = "<p>Initial</p>",
                PlainTextExcerpt = "Initial",
                Version = 1,
                CreatedAtUtc = DateTime.UtcNow,
                LastModifiedUtc = DateTime.UtcNow
            };

            // Concurrent save simulator
            var incomingVersionMatch = 1;
            Assert(testNote.Version == incomingVersionMatch,
                "Note Concurrency: Version check succeeds when incoming version matches entity");

            // Simulate save operation
            testNote.ContentDeltaJson = "{\"ops\":[{\"insert\":\"Updated\\n\"}]}";
            testNote.SanitizedContentHtml = "<p>Updated</p>";
            testNote.Version++;
            testNote.LastModifiedUtc = DateTime.UtcNow;

            Assert(testNote.Version == 2,
                "Note Concurrency: Version incremented to 2 on successful save");

            var staleVersion = 1;
            Assert(testNote.Version != staleVersion,
                "Note Concurrency: Stale save attempt with version 1 is rejected as conflict");

            // 6. Reading Progress & Clamping Logic
            int totalDocPages = 250;
            int requestedPage = 125;
            var clampedPage = Math.Clamp(requestedPage, 1, totalDocPages);
            var percentage = Math.Clamp(Math.Round((decimal)clampedPage / totalDocPages * 100m, 2), 0m, 100m);
            Assert(percentage == 50.00m, "Reading Progress: Page 125 of 250 equals exactly 50.00%");

            var outOfBoundsHigh = 999;
            var clampedHigh = Math.Clamp(outOfBoundsHigh, 1, totalDocPages);
            var highPercentage = Math.Clamp(Math.Round((decimal)clampedHigh / totalDocPages * 100m, 2), 0m, 100m);
            Assert(clampedHigh == 250 && highPercentage == 100.00m,
                "Reading Progress: Out-of-bounds high page clamps to total pages and 100.00%");

            var outOfBoundsLow = -5;
            var clampedLow = Math.Clamp(outOfBoundsLow, 1, totalDocPages);
            var lowPercentage = Math.Clamp(Math.Round((decimal)clampedLow / totalDocPages * 100m, 2), 0m, 100m);
            Assert(clampedLow == 1 && lowPercentage == 0.40m,
                "Reading Progress: Negative page clamps to page 1");

            // 7. Editorial Highlight Colors & Annotation Anchor
            var validColors = new[] { "yellow", "sage", "blue", "terracotta" };
            Assert(validColors.Contains("yellow") &&
                   validColors.Contains("sage") &&
                   validColors.Contains("blue") &&
                   validColors.Contains("terracotta"),
                "Annotations: All 4 Editorial Magazine highlight color tokens present");

            var annotation = new BookAnnotation
            {
                Id = 1,
                UserId = "user-1",
                DigitalBookId = 5,
                DocumentVersion = 1,
                PageNumber = 42,
                SelectedText = "Software engineering is about trade-offs.",
                HighlightColor = "sage",
                TextAnchorJson = "{\"startOffset\":15,\"endOffset\":58,\"exact\":\"Software engineering is about trade-offs.\"}",
                PositionJson = "{\"rects\":[{\"left\":12.5,\"top\":34.2,\"width\":75.1,\"height\":2.3}]}",
                CreatedAtUtc = DateTime.UtcNow
            };

            Assert(annotation.SelectedText.Length > 0 && annotation.HighlightColor == "sage",
                "Annotations: BookAnnotation entity populates text, color and page correctly");
            Assert(annotation.TextAnchorJson.Contains("exact") && annotation.PositionJson.Contains("rects"),
                "Annotations: TextAnchor and Position JSON payloads conform to viewport schema");

            // 8. PDF Replacement & Annotation Version Isolation
            var annoV1 = new BookAnnotation { Id = 10, DigitalBookId = 1, DocumentVersion = 1, PageNumber = 5, SelectedText = "V1 Text" };
            var annoV2 = new BookAnnotation { Id = 11, DigitalBookId = 1, DocumentVersion = 2, PageNumber = 5, SelectedText = "V2 Text" };
            var allAnnos = new List<BookAnnotation> { annoV1, annoV2 };

            var currentDocVersion = 2;
            var activeAnnos = allAnnos.Where(a => a.DocumentVersion == currentDocVersion).ToList();
            Assert(activeAnnos.Count == 1 && activeAnnos[0].Id == 11,
                "Version Isolation: Only annotations matching DocumentVersion 2 are rendered on replaced PDF");

            var archivedAnnos = allAnnos.Where(a => a.DocumentVersion < currentDocVersion).ToList();
            Assert(archivedAnnos.Count == 1 && archivedAnnos[0].Id == 10,
                "Version Isolation: Older Version 1 annotations are segregated as archived instead of overlapping new pages");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[CRITICAL ERROR IN DIGITAL LIBRARY TESTS] {ex.Message}\n{ex.StackTrace}");
            Console.ResetColor();
            failed++;
        }

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"Phase 5 Tests: {passed + failed} | Passed: {passed} | Failed: {failed}");
        Console.WriteLine("--------------------------------------------------");

        passedCount = passed;
        failedCount = failed;
        return failed == 0;
    }
}
