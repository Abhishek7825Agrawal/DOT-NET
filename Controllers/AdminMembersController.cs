using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin,Staff")]
public sealed class AdminMembersController(
    MemberService members,
    LibraryCalendar calendar,
    CustomerImportService customerImport,
    ApplicationDbContext db,
    ActivityLogService activityLogs,
    UserManager<ApplicationUser> userManager) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private string BuildActivationUrl(string userId, string token) =>
        Url.Action("SetupPassword", "Account", new { userId, token }, Request.Scheme)
        ?? $"{Request.Scheme}://{Request.Host}/Account/SetupPassword?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;
        var query = members.Search(q);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.Query = q;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken));
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var model = await members.GetDetailsAsync(id, calendar.Today, cancellationToken);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetStatus(int id, bool active, bool returnToDetails = false, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await members.SetActiveAsync(id, active, cancellationToken)) return NotFound();
        var member = await db.Members.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (member is not null)
        {
            member.User.IsActive = active;
            await db.SaveChangesAsync(cancellationToken);
            await userManager.UpdateSecurityStampAsync(member.User);
            await transaction.CommitAsync(cancellationToken);
            var actorId = userManager.GetUserId(User);
            await activityLogs.RecordAsync(actorId, active ? "MemberReactivated" : "MemberDeactivated", "AccountManagement",
                $"You {(active ? "reactivated" : "deactivated")} member {member.FullName}.", entityType: "Member",
                entityReference: member.Id.ToString(), securityEvent: true, cancellationToken: cancellationToken);
        }
        TempData["Success"] = active ? "Member account activated." : "Member account deactivated.";
        if (returnToDetails) return RedirectToAction(nameof(Details), new { id });
        return RedirectToAction(nameof(Index));
    }

    #region Staff-Assisted Customer Registration

    [HttpGet]
    public IActionResult RegisterCustomer()
    {
        return View(new StaffRegisterCustomerViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> RegisterCustomer(
        StaffRegisterCustomerViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var result = await customerImport.RegisterSingleMemberAsync(
                model,
                CurrentUserId,
                BuildActivationUrl,
                cancellationToken);

            await activityLogs.RecordAsync(CurrentUserId, "MemberRegistered", "AccountManagement",
                $"You registered member {result.FullName}.", entityType: "Member", entityReference: result.Email,
                securityEvent: true, cancellationToken: cancellationToken);

            TempData["Success"] = $"Member account for '{result.FullName}' created successfully.";
            return View("RegistrationSuccess", result);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    #endregion

    #region Excel Bulk Import & Validation

    [HttpGet]
    public IActionResult BulkImport()
    {
        return View(new ExcelUploadViewModel());
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var fileBytes = customerImport.GenerateTemplate();
        var fileName = $"Shelfwise_Member_Registration_Template_{DateTime.UtcNow:yyyyMMdd}.xlsx";
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpPost]
    public async Task<IActionResult> UploadExcel(
        ExcelUploadViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || model.ExcelFile is null)
        {
            return View("BulkImport", model);
        }

        var file = model.ExcelFile;
        const long maxFileSize = 5 * 1024 * 1024; // 5 MB

        if (file.Length == 0)
        {
            ModelState.AddModelError("ExcelFile", "The selected file is empty. Please choose a valid spreadsheet.");
            return View("BulkImport", model);
        }

        if (file.Length > maxFileSize)
        {
            ModelState.AddModelError("ExcelFile", "File size exceeds the 5 MB limit. Please split into smaller batches.");
            return View("BulkImport", model);
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("ExcelFile", "Only Excel files (.xlsx) are supported.");
            return View("BulkImport", model);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var batch = await customerImport.ValidateAndStageExcelAsync(
                stream,
                file.FileName,
                CurrentUserId,
                cancellationToken);

            await activityLogs.RecordAsync(CurrentUserId, "MemberImportStaged", "DataImport",
                $"You staged member import batch {batch.BatchReference} for review.", entityType: "ImportBatch",
                entityReference: batch.BatchReference, securityEvent: true, cancellationToken: cancellationToken);

            return RedirectToAction(nameof(ImportPreview), new { batchId = batch.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("ExcelFile", $"Failed to process spreadsheet: {ex.Message}");
            return View("BulkImport", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> ImportPreview(int batchId, CancellationToken cancellationToken)
    {
        var batch = await db.CustomerImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return NotFound("Import batch not found.");
        }

        var viewModel = new ExcelImportPreviewViewModel
        {
            BatchId = batch.Id,
            BatchReference = batch.BatchReference,
            FileName = batch.OriginalFileName,
            CreatedAtUtc = batch.CreatedAtUtc,
            TotalRows = batch.TotalRows,
            ValidCount = batch.ValidRowsCount,
            InvalidCount = batch.InvalidRowsCount,
            DuplicateCount = batch.DuplicateRowsCount,
            Rows = batch.Rows.OrderBy(r => r.RowNumber).Select(r => new CustomerImportRowItemViewModel
            {
                RowNumber = r.RowNumber,
                FullName = r.FullName,
                Email = r.Email,
                PhoneNumber = r.PhoneNumber,
                Address = r.Address,
                Status = r.Status,
                ValidationMessage = r.ValidationMessage
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmImport(int batchId, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await customerImport.CommitBatchAsync(
                batchId,
                CurrentUserId,
                BuildActivationUrl,
                cancellationToken);

            await activityLogs.RecordAsync(CurrentUserId, "MemberImportCommitted", "DataImport",
                $"You committed import batch {summary.BatchReference} with {summary.SuccessfullyImported} new accounts.",
                entityType: "ImportBatch", entityReference: summary.BatchReference,
                securityEvent: true, cancellationToken: cancellationToken);

            TempData["Success"] = $"Batch '{summary.BatchReference}' committed successfully: {summary.SuccessfullyImported} cardholder accounts created.";
            return View("ImportSummary", summary);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Batch confirmation failed: {ex.Message}";
            return RedirectToAction(nameof(ImportPreview), new { batchId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadErrorReport(int batchId, CancellationToken cancellationToken)
    {
        var batch = await db.CustomerImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return NotFound("Import batch not found.");
        }

        var reportBytes = customerImport.GenerateErrorReport(batch);
        var fileName = $"Shelfwise_Import_Error_Audit_{batch.BatchReference}.xlsx";
        return File(reportBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> ImportHistory(CancellationToken cancellationToken)
    {
        var batches = await db.CustomerImportBatches
            .AsNoTracking()
            .Include(b => b.StaffUser)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        return View(batches);
    }

    #endregion
}
