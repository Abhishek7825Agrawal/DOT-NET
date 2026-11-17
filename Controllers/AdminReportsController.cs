using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminReportsController(ReportingService reporting) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var data = await reporting.GetComprehensiveReportAsync(cancellationToken);
        return View(data);
    }

    public async Task<IActionResult> ExportBooks(CancellationToken cancellationToken)
    {
        var bytes = await reporting.ExportBooksCsvAsync(cancellationToken);
        return File(bytes, "text/csv", $"books-inventory-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> ExportCirculation(CancellationToken cancellationToken)
    {
        var bytes = await reporting.ExportCirculationCsvAsync(cancellationToken);
        return File(bytes, "text/csv", $"circulation-records-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> ExportPayments(CancellationToken cancellationToken)
    {
        var bytes = await reporting.ExportFinePaymentsCsvAsync(cancellationToken);
        return File(bytes, "text/csv", $"fine-payments-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

