using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LibraryManagementSystem.Services;

public sealed class DigitalDocumentMetadata
{
    public string StorageFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
}

public sealed partial class DigitalDocumentService(IWebHostEnvironment environment)
{
    private static readonly byte[] PdfMagicHeader = "%PDF-"u8.ToArray();
    public const long MaxPdfSizeBytes = 100 * 1024 * 1024; // 100 MB

    private string StorageDirectory
    {
        get
        {
            var path = Path.Combine(environment.ContentRootPath, "App_Data", "DigitalLibrary");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return Path.GetFullPath(path);
        }
    }

    /// <summary>
    /// Validates and saves an uploaded PDF document safely outside wwwroot.
    /// Performs file signature checks, extracts page count, and computes SHA-256 integrity hash.
    /// </summary>
    public async Task<DigitalDocumentMetadata> SaveDocumentAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            throw new ArgumentException("Please select a non-empty PDF document.", nameof(file));
        }

        if (file.Length > MaxPdfSizeBytes)
        {
            throw new InvalidOperationException($"File size exceeds the maximum limit of {MaxPdfSizeBytes / (1024 * 1024)} MB.");
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only valid PDF files (.pdf) are supported.");
        }

        await using var stream = file.OpenReadStream();
        return await SaveDocumentStreamAsync(stream, file.FileName, cancellationToken);
    }

    /// <summary>
    /// Stream overload for saving and validating PDFs from any readable stream.
    /// </summary>
    public async Task<DigitalDocumentMetadata> SaveDocumentStreamAsync(
        Stream stream,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        // 1. Verify PDF Magic Signature (%PDF-)
        if (!ValidatePdfSignature(stream))
        {
            throw new InvalidOperationException("The uploaded file does not have a valid PDF header (%PDF-). File was rejected for security.");
        }

        // 2. Extract Page Count from PDF Catalog
        var pageCount = ExtractPageCount(stream);
        if (pageCount <= 0)
        {
            pageCount = 1; // Fallback minimum
        }

        // 3. Generate secure non-guessable storage filename
        var safeStorageFileName = $"doc_{Guid.NewGuid():N}.pdf";
        var targetFullPath = GetVerifiedDocumentPath(safeStorageFileName);

        // 4. Save file to disk and compute SHA-256 hash simultaneously
        stream.Position = 0;
        using var sha256 = SHA256.Create();
        long totalBytesWritten;

        await using (var fileStream = new FileStream(targetFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var cryptoStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write))
        {
            await stream.CopyToAsync(cryptoStream, cancellationToken);
            await cryptoStream.FlushFinalBlockAsync(cancellationToken);
            totalBytesWritten = fileStream.Length;
        }

        var hashBytes = sha256.Hash ?? [];
        var checksumHex = Convert.ToHexStringLower(hashBytes);

        return new DigitalDocumentMetadata
        {
            StorageFileName = safeStorageFileName,
            OriginalFileName = Path.GetFileName(originalFileName),
            FileSizeBytes = totalBytesWritten,
            PageCount = pageCount,
            ChecksumSha256 = checksumHex
        };
    }

    /// <summary>
    /// Verifies that the initial bytes match the %PDF- signature.
    /// </summary>
    public static bool ValidatePdfSignature(Stream stream)
    {
        if (stream.Length < 5) return false;
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var header = new byte[5];
            var bytesRead = stream.Read(header, 0, 5);
            if (bytesRead < 5) return false;

            for (var i = 0; i < 5; i++)
            {
                if (header[i] != PdfMagicHeader[i]) return false;
            }

            return true;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Lightweight, memory-efficient scanner that extracts page count from the PDF document structure.
    /// Looks for the root Pages dictionary /Count, or counts individual /Type /Page references.
    /// </summary>
    public static int ExtractPageCount(Stream stream)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            var content = reader.ReadToEnd();

            // Match root pages dictionary count e.g. /Type /Pages ... /Count 142
            var countMatches = Regex.Matches(content, @"/Type\s*/Pages.*?/Count\s+(\d+)", RegexOptions.Singleline);
            foreach (Match match in countMatches)
            {
                if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var count) && count > 0)
                {
                    return count;
                }
            }

            // Secondary match: /Count (\d+) anywhere before /Kids
            var simpleCountMatches = Regex.Matches(content, @"/Count\s+(\d+)");
            var maxCount = 0;
            foreach (Match match in simpleCountMatches)
            {
                if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var c) && c > maxCount)
                {
                    maxCount = c;
                }
            }
            if (maxCount > 0) return maxCount;

            // Tertiary match: count occurrences of /Type /Page (excluding /Pages)
            var pageObjectsCount = Regex.Matches(content, @"/Type\s*/Page\b").Count;
            return pageObjectsCount > 0 ? pageObjectsCount : 1;
        }
        catch
        {
            return 1;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Securely resolves the absolute file path, preventing path traversal attacks.
    /// </summary>
    public string GetVerifiedDocumentPath(string storageFileName)
    {
        if (string.IsNullOrWhiteSpace(storageFileName))
        {
            throw new ArgumentException("Storage file name is required.", nameof(storageFileName));
        }

        var fileNameOnly = Path.GetFileName(storageFileName);
        var baseDir = StorageDirectory;
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, fileNameOnly));

        // Strict path traversal check
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Potential path traversal attack detected.");
        }

        return fullPath;
    }

    /// <summary>
    /// Checks whether the specified document file exists in secure storage.
    /// </summary>
    public bool DocumentExists(string storageFileName)
    {
        try
        {
            var path = GetVerifiedDocumentPath(storageFileName);
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a digital document file from secure storage.
    /// </summary>
    public void DeleteDocument(string storageFileName)
    {
        try
        {
            var path = GetVerifiedDocumentPath(storageFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore if file doesn't exist or is locked
        }
    }
}
