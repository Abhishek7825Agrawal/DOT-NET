using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LibraryManagementSystem.Services;

public sealed class PaperDocumentMetadata
{
    public string StorageFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
}

public sealed class PaperStorageService(IWebHostEnvironment environment)
{
    private static readonly byte[] PdfMagicHeader = "%PDF-"u8.ToArray();
    public const long MaxPaperSizeBytes = 50 * 1024 * 1024; // 50 MB limit

    private string StorageDirectory
    {
        get
        {
            var path = Path.Combine(environment.ContentRootPath, "App_Data", "MarketplacePapers");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return Path.GetFullPath(path);
        }
    }

    /// <summary>
    /// Validates and saves an uploaded research paper PDF securely outside wwwroot.
    /// </summary>
    public async Task<PaperDocumentMetadata> SavePaperAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            throw new ArgumentException("Please select a non-empty PDF document.", nameof(file));
        }

        if (file.Length > MaxPaperSizeBytes)
        {
            throw new InvalidOperationException($"File size exceeds the maximum limit of {MaxPaperSizeBytes / (1024 * 1024)} MB.");
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only valid PDF files (.pdf) are supported for research manuscripts.");
        }

        await using var stream = file.OpenReadStream();
        return await SavePaperStreamAsync(stream, file.FileName, cancellationToken);
    }

    /// <summary>
    /// Stream overload for saving and validating PDFs from any readable stream.
    /// </summary>
    public async Task<PaperDocumentMetadata> SavePaperStreamAsync(
        Stream stream,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        if (!ValidatePdfSignature(stream))
        {
            throw new InvalidOperationException("The uploaded file does not have a valid PDF header (%PDF-). File was rejected for security.");
        }

        var pageCount = ExtractPageCount(stream);
        if (pageCount <= 0)
        {
            pageCount = 1;
        }

        var safeStorageFileName = $"paper_{Guid.NewGuid():N}.pdf";
        var targetFullPath = GetVerifiedPaperPath(safeStorageFileName);

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

        return new PaperDocumentMetadata
        {
            StorageFileName = safeStorageFileName,
            OriginalFileName = Path.GetFileName(originalFileName),
            FileSizeBytes = totalBytesWritten,
            PageCount = pageCount,
            ChecksumSha256 = checksumHex
        };
    }

    public static bool ValidatePdfHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < 5) return false;
        return headerBytes[..5].SequenceEqual(PdfMagicHeader);
    }

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

    public static int ExtractPageCount(Stream stream)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            var content = reader.ReadToEnd();

            var countMatches = Regex.Matches(content, @"/Type\s*/Pages.*?/Count\s+(\d+)", RegexOptions.Singleline);
            foreach (Match match in countMatches)
            {
                if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var count) && count > 0)
                {
                    return count;
                }
            }

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

    public string GetVerifiedPaperPath(string storageFileName)
    {
        if (string.IsNullOrWhiteSpace(storageFileName))
        {
            throw new ArgumentException("Storage file name is required.", nameof(storageFileName));
        }

        var fileNameOnly = Path.GetFileName(storageFileName);
        var baseDir = StorageDirectory;
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, fileNameOnly));

        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Potential path traversal attack detected.");
        }

        return fullPath;
    }

    public bool PaperExists(string storageFileName)
    {
        try
        {
            var path = GetVerifiedPaperPath(storageFileName);
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public void DeletePaper(string storageFileName)
    {
        try
        {
            var path = GetVerifiedPaperPath(storageFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore if file doesn't exist
        }
    }
}
