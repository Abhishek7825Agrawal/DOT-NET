using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed partial class CustomerImportService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager)
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRegex = new(
        @"^[\+0-9\s\-\(\)\.]{7,30}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Neutralizes CSV / Spreadsheet Formula Injection (CWE-1236).
    /// If an untrusted string begins with '=', '+', '-', '@', '\t', or '\r',
    /// prepends a single quote so spreadsheet engines treat it strictly as text.
    /// </summary>
    public static string SanitizeFormulaInjection(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var trimmed = input.Trim();
        if (trimmed.Length == 0) return string.Empty;

        var firstChar = trimmed[0];
        if (firstChar is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            return "'" + trimmed;
        }

        return trimmed;
    }

    /// <summary>
    /// Generates a standardized, beautifully branded Excel workbook template for staff to populate.
    /// </summary>
    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();

        // 1. Instructions Sheet
        var instructionsSheet = workbook.Worksheets.Add("Instructions");
        instructionsSheet.TabColor = XLColor.FromHtml("#162521");

        instructionsSheet.Cell("A1").Value = "SHELFWISE — CUSTOMER BULK REGISTRATION TEMPLATE";
        instructionsSheet.Cell("A1").Style.Font.Bold = true;
        instructionsSheet.Cell("A1").Style.Font.FontSize = 14;
        instructionsSheet.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#162521");

        instructionsSheet.Cell("A3").Value = "How to use this template:";
        instructionsSheet.Cell("A3").Style.Font.Bold = true;

        var instructions = new[]
        {
            "1. Switch to the 'Members' tab to enter cardholder details.",
            "2. 'Full Name' is REQUIRED (between 2 and 120 characters).",
            "3. 'Email Address' is REQUIRED and must be a valid email format.",
            "4. 'Phone Number' is optional, but if provided, must be a valid contact number (7-30 characters).",
            "5. 'Residential Address' is optional (up to 300 characters).",
            "6. 'Notes' is optional for branch reference or membership tags.",
            "7. Do NOT modify the column header titles in the 'Members' tab.",
            "8. Delete the sample rows before uploading your final spreadsheet.",
            "9. Security Notice: Shelfwise never assigns default passwords. Each member receives a unique, cryptographically secure account activation link."
        };

        for (var i = 0; i < instructions.Length; i++)
        {
            var row = 4 + i;
            instructionsSheet.Cell(row, 1).Value = instructions[i];
            instructionsSheet.Cell(row, 1).Style.Font.FontSize = 10;
        }

        instructionsSheet.Columns(1, 2).AdjustToContents();

        // 2. Data Sheet
        var dataSheet = workbook.Worksheets.Add("Members");
        dataSheet.TabColor = XLColor.FromHtml("#C85A32");

        var headers = new[]
        {
            "Full Name *",
            "Email Address *",
            "Phone Number",
            "Residential Address",
            "Notes"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = dataSheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#162521");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#162521");
        }
        dataSheet.Row(1).Height = 28;

        // Sample Data Rows (Clearly marked for staff reference)
        var sampleRows = new[]
        {
            new[] { "Jane Doe", "jane.doe@example.org", "+1 555-019-2834", "42 Library Ave, Apt 3B, New York, NY", "Faculty Cardholder" },
            new[] { "Rohan Sharma", "rohan.sharma@example.edu", "+91 98765 43210", "12 Heritage Road, Bengaluru, KA", "Research Fellow" }
        };

        for (var r = 0; r < sampleRows.Length; r++)
        {
            var rowIdx = 2 + r;
            for (var c = 0; c < sampleRows[r].Length; c++)
            {
                var cell = dataSheet.Cell(rowIdx, c + 1);
                cell.Value = sampleRows[r][c];
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E0D8CB");
            }
        }

        dataSheet.Columns(1, 5).AdjustToContents(15.0, 50.0);

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Parses and validates an uploaded Excel file row-by-row, detects duplicates
    /// within the file and against the database, and stages an uncommitted batch for preview.
    /// </summary>
    public async Task<CustomerImportBatch> ValidateAndStageExcelAsync(
        Stream excelStream,
        string originalFileName,
        string staffUserId,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Members", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault();

        if (worksheet is null)
        {
            throw new InvalidOperationException("The uploaded Excel workbook contains no readable worksheets.");
        }

        var batch = new CustomerImportBatch
        {
            BatchReference = $"BATCH-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            StaffUserId = staffUserId,
            OriginalFileName = Path.GetFileName(originalFileName),
            CreatedAtUtc = DateTime.UtcNow,
            IsCommitted = false
        };

        var seenEmailsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPhonesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Preload active database emails and phones to optimize batch duplicate lookups
        var existingEmails = await db.Users.AsNoTracking()
            .Where(u => u.Email != null)
            .Select(u => u.NormalizedEmail!)
            .ToHashSetAsync(cancellationToken);

        var existingPhones = await db.Members.AsNoTracking()
            .Where(m => !string.IsNullOrEmpty(m.PhoneNumber))
            .Select(m => m.PhoneNumber!.Trim())
            .ToHashSetAsync(cancellationToken);

        var lastRowUsed = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var batchRows = new List<CustomerImportRow>();

        for (var rowNumber = 2; rowNumber <= lastRowUsed; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty()) continue;

            var rawFullName = row.Cell(1).GetString()?.Trim() ?? string.Empty;
            var rawEmail = row.Cell(2).GetString()?.Trim() ?? string.Empty;
            var rawPhone = row.Cell(3).GetString()?.Trim();
            var rawAddress = row.Cell(4).GetString()?.Trim();

            // Skip completely empty rows
            if (string.IsNullOrWhiteSpace(rawFullName) &&
                string.IsNullOrWhiteSpace(rawEmail) &&
                string.IsNullOrWhiteSpace(rawPhone) &&
                string.IsNullOrWhiteSpace(rawAddress))
            {
                continue;
            }

            var importRow = new CustomerImportRow
            {
                RowNumber = rowNumber,
                FullName = rawFullName,
                Email = rawEmail,
                PhoneNumber = string.IsNullOrWhiteSpace(rawPhone) ? null : rawPhone,
                Address = string.IsNullOrWhiteSpace(rawAddress) ? null : rawAddress,
                Status = ImportRowStatus.Valid
            };

            // 1. Validation checks
            if (string.IsNullOrWhiteSpace(rawFullName))
            {
                importRow.Status = ImportRowStatus.Invalid;
                importRow.ValidationMessage = "Full Name is required.";
            }
            else if (rawFullName.Length < 2 || rawFullName.Length > 120)
            {
                importRow.Status = ImportRowStatus.Invalid;
                importRow.ValidationMessage = "Full Name must be between 2 and 120 characters.";
            }
            else if (string.IsNullOrWhiteSpace(rawEmail))
            {
                importRow.Status = ImportRowStatus.Invalid;
                importRow.ValidationMessage = "Email address is required.";
            }
            else if (rawEmail.Length > 256 || !EmailRegex.IsMatch(rawEmail))
            {
                importRow.Status = ImportRowStatus.Invalid;
                importRow.ValidationMessage = "Invalid email address format.";
            }
            else if (!string.IsNullOrWhiteSpace(rawPhone) && (rawPhone.Length > 30 || !PhoneRegex.IsMatch(rawPhone)))
            {
                importRow.Status = ImportRowStatus.Invalid;
                importRow.ValidationMessage = "Invalid phone number format.";
            }
            else if (!string.IsNullOrWhiteSpace(rawAddress) && rawAddress.Length > 300)
            {
                importRow.Status = ImportRowStatus.Invalid;
                importRow.ValidationMessage = "Address cannot exceed 300 characters.";
            }

            // 2. Duplicate checks (Only if valid so far)
            if (importRow.Status == ImportRowStatus.Valid)
            {
                var normalizedEmail = rawEmail.ToUpperInvariant();

                if (seenEmailsInFile.Contains(normalizedEmail))
                {
                    importRow.Status = ImportRowStatus.Duplicate;
                    importRow.ValidationMessage = $"Duplicate email address found within this spreadsheet: {rawEmail}";
                }
                else if (existingEmails.Contains(normalizedEmail))
                {
                    importRow.Status = ImportRowStatus.Duplicate;
                    importRow.ValidationMessage = $"An account with email '{rawEmail}' already exists in the library system.";
                }
                else if (!string.IsNullOrWhiteSpace(rawPhone))
                {
                    if (seenPhonesInFile.Contains(rawPhone))
                    {
                        importRow.Status = ImportRowStatus.Duplicate;
                        importRow.ValidationMessage = $"Duplicate phone number found within this spreadsheet: {rawPhone}";
                    }
                    else if (existingPhones.Contains(rawPhone))
                    {
                        importRow.Status = ImportRowStatus.Duplicate;
                        importRow.ValidationMessage = $"A member with phone number '{rawPhone}' already exists in the system.";
                    }
                }

                if (importRow.Status == ImportRowStatus.Valid)
                {
                    seenEmailsInFile.Add(normalizedEmail);
                    if (!string.IsNullOrWhiteSpace(rawPhone))
                    {
                        seenPhonesInFile.Add(rawPhone);
                    }
                }
            }

            batchRows.Add(importRow);
        }

        batch.TotalRows = batchRows.Count;
        batch.ValidRowsCount = batchRows.Count(r => r.Status == ImportRowStatus.Valid);
        batch.InvalidRowsCount = batchRows.Count(r => r.Status == ImportRowStatus.Invalid);
        batch.DuplicateRowsCount = batchRows.Count(r => r.Status == ImportRowStatus.Duplicate);
        batch.Rows = batchRows;

        db.CustomerImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        return batch;
    }

    /// <summary>
    /// Executes explicit staff confirmation to commit only the Valid rows from a staged batch.
    /// Operates inside a secure database transaction.
    /// </summary>
    public async Task<CustomerImportSummaryViewModel> CommitBatchAsync(
        int batchId,
        string staffUserId,
        Func<string, string, string> activationUrlGenerator,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.CustomerImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new KeyNotFoundException($"Import batch #{batchId} not found.");

        if (batch.IsCommitted)
        {
            throw new InvalidOperationException($"Batch '{batch.BatchReference}' has already been committed.");
        }

        var validRows = batch.Rows.Where(r => r.Status == ImportRowStatus.Valid).OrderBy(r => r.RowNumber).ToList();
        var summary = new CustomerImportSummaryViewModel
        {
            BatchId = batch.Id,
            BatchReference = batch.BatchReference,
            OriginalFileName = batch.OriginalFileName,
            TotalProcessed = batch.TotalRows,
            InvalidSkipped = batch.InvalidRowsCount,
            DuplicateSkipped = batch.DuplicateRowsCount,
            CommittedAtUtc = DateTime.UtcNow
        };

        if (validRows.Count == 0)
        {
            batch.IsCommitted = true;
            batch.CommittedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return summary;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var successfullyImported = 0;
        foreach (var row in validRows)
        {
            // Verify concurrency: ensure user wasn't registered in the interim
            var existingUser = await userManager.FindByEmailAsync(row.Email);
            if (existingUser is not null)
            {
                row.Status = ImportRowStatus.Duplicate;
                row.ValidationMessage = $"Concurrent registration detected: user with email '{row.Email}' was registered.";
                continue;
            }

            // Cryptographically secure 32-character internal secret
            var randomSecret = GenerateSecureRandomPassword();

            var user = new ApplicationUser
            {
                UserName = row.Email.Trim(),
                Email = row.Email.Trim(),
                PhoneNumber = row.PhoneNumber?.Trim(),
                EmailConfirmed = false
            };

            var createResult = await userManager.CreateAsync(user, randomSecret);
            if (!createResult.Succeeded)
            {
                row.Status = ImportRowStatus.Failed;
                row.ValidationMessage = string.Join("; ", createResult.Errors.Select(e => e.Description));
                continue;
            }

            var roleResult = await userManager.AddToRoleAsync(user, "Member");
            if (!roleResult.Succeeded)
            {
                row.Status = ImportRowStatus.Failed;
                row.ValidationMessage = "Failed to assign Member role: " + string.Join("; ", roleResult.Errors.Select(e => e.Description));
                continue;
            }

            var member = new Member
            {
                UserId = user.Id,
                FullName = row.FullName.Trim(),
                PhoneNumber = row.PhoneNumber?.Trim(),
                Address = row.Address?.Trim(),
                RegisteredAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            db.Members.Add(member);
            await db.SaveChangesAsync(cancellationToken);

            // Generate password setup token
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            row.Status = ImportRowStatus.Imported;
            row.CreatedUserId = user.Id;
            row.ActivationToken = token;

            var activationUrl = activationUrlGenerator(user.Id, token);
            summary.ImportedMembers.Add(new ImportedRowSummaryItem
            {
                RowNumber = row.RowNumber,
                FullName = row.FullName,
                Email = row.Email,
                ActivationUrl = activationUrl
            });

            successfullyImported++;
        }

        batch.SuccessImportedCount = successfullyImported;
        batch.IsCommitted = true;
        batch.CommittedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        summary.SuccessfullyImported = successfullyImported;
        return summary;
    }

    /// <summary>
    /// Generates a downloadable Excel error report for any batch with Invalid or Duplicate rows.
    /// Incorporates formula injection neutralization on every text cell.
    /// </summary>
    public byte[] GenerateErrorReport(CustomerImportBatch batch)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Errors and Duplicates");
        sheet.TabColor = XLColor.FromHtml("#C85A32");

        // Title Block
        sheet.Cell("A1").Value = $"SHELFWISE IMPORT ERROR AUDIT — BATCH {SanitizeFormulaInjection(batch.BatchReference)}";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 13;
        sheet.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#C85A32");

        sheet.Cell("A2").Value = $"Original File: {SanitizeFormulaInjection(batch.OriginalFileName)} | Audit Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC";
        sheet.Cell("A2").Style.Font.FontSize = 9;
        sheet.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#555555");

        var headers = new[]
        {
            "Excel Row #",
            "Full Name",
            "Email Address",
            "Phone Number",
            "Residential Address",
            "Classification",
            "Validation Failure Reason"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = sheet.Cell(4, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#162521");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#162521");
        }
        sheet.Row(4).Height = 26;

        var rejectedRows = batch.Rows
            .Where(r => r.Status is ImportRowStatus.Invalid or ImportRowStatus.Duplicate or ImportRowStatus.Failed)
            .OrderBy(r => r.RowNumber)
            .ToList();

        for (var i = 0; i < rejectedRows.Count; i++)
        {
            var r = rejectedRows[i];
            var rowIdx = 5 + i;

            sheet.Cell(rowIdx, 1).Value = r.RowNumber;
            sheet.Cell(rowIdx, 2).Value = SanitizeFormulaInjection(r.FullName);
            sheet.Cell(rowIdx, 3).Value = SanitizeFormulaInjection(r.Email);
            sheet.Cell(rowIdx, 4).Value = SanitizeFormulaInjection(r.PhoneNumber);
            sheet.Cell(rowIdx, 5).Value = SanitizeFormulaInjection(r.Address);
            sheet.Cell(rowIdx, 6).Value = r.Status.ToString();
            sheet.Cell(rowIdx, 7).Value = SanitizeFormulaInjection(r.ValidationMessage);

            // Row styling
            var statusCell = sheet.Cell(rowIdx, 6);
            if (r.Status == ImportRowStatus.Duplicate)
            {
                statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF3C7"); // light amber
                statusCell.Style.Font.FontColor = XLColor.FromHtml("#92400E");
            }
            else
            {
                statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEE2E2"); // light red
                statusCell.Style.Font.FontColor = XLColor.FromHtml("#991B1B");
            }
            statusCell.Style.Font.Bold = true;

            for (var c = 1; c <= 7; c++)
            {
                sheet.Cell(rowIdx, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                sheet.Cell(rowIdx, c).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E5E7EB");
            }
        }

        sheet.Columns(1, 7).AdjustToContents(12.0, 60.0);

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Performs individual manual customer registration at the staff circulation desk.
    /// Safely assigns cryptographically strong credentials and yields a single-use setup token.
    /// </summary>
    public async Task<StaffRegistrationSuccessViewModel> RegisterSingleMemberAsync(
        StaffRegisterCustomerViewModel model,
        string staffUserId,
        Func<string, string, string> activationUrlGenerator,
        CancellationToken cancellationToken = default)
    {
        var email = model.Email.Trim();

        // 1. Duplicate checks
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            throw new InvalidOperationException($"An account with email address '{email}' already exists.");
        }

        if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
        {
            var phoneTrimmed = model.PhoneNumber.Trim();
            var phoneExists = await db.Members.AnyAsync(m => m.PhoneNumber == phoneTrimmed, cancellationToken);
            if (phoneExists)
            {
                throw new InvalidOperationException($"A member with phone number '{phoneTrimmed}' already exists.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var randomSecret = GenerateSecureRandomPassword();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            PhoneNumber = model.PhoneNumber?.Trim(),
            EmailConfirmed = false
        };

        var created = await userManager.CreateAsync(user, randomSecret);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        var roleResult = await userManager.AddToRoleAsync(user, "Member");
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        var member = new Member
        {
            UserId = user.Id,
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber?.Trim(),
            Address = model.Address?.Trim(),
            RegisteredAtUtc = DateTime.UtcNow,
            IsActive = true
        };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        await transaction.CommitAsync(cancellationToken);

        var activationUrl = activationUrlGenerator(user.Id, token);

        return new StaffRegistrationSuccessViewModel
        {
            MemberId = member.Id,
            UserId = user.Id,
            FullName = member.FullName,
            Email = user.Email!,
            PhoneNumber = member.PhoneNumber,
            Address = member.Address,
            ActivationToken = token,
            ActivationUrl = activationUrl,
            RegisteredAtUtc = member.RegisteredAtUtc
        };
    }

    /// <summary>
    /// Generates an unpredictable 32-character random string satisfying complex password policies.
    /// </summary>
    public static string GenerateSecureRandomPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string specials = "!@#$%^&*()-_=+";
        const string allChars = upper + lower + digits + specials;

        Span<char> password = stackalloc char[32];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[2] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[3] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[4] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[5] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[6] = specials[RandomNumberGenerator.GetInt32(specials.Length)];
        password[7] = specials[RandomNumberGenerator.GetInt32(specials.Length)];

        for (var i = 8; i < 32; i++)
        {
            password[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];
        }

        // Cryptographically shuffle the characters
        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
