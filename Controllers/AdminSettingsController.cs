using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminSettingsController(SettingsService settingsService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetSettingsAsync(cancellationToken);
        return View(new LibrarySettingsViewModel
        {
            LoanPeriodDays = settings.LoanPeriodDays,
            FineRatePerDay = settings.FineRatePerDay,
            MaxBooksPerMember = settings.MaxBooksPerMember,
            LostBookPenaltyFee = settings.LostBookPenaltyFee,
            GracePeriodDays = settings.GracePeriodDays,
            TimeZoneId = settings.TimeZoneId
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(LibrarySettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await settingsService.UpdateSettingsAsync(new LibrarySetting
        {
            LoanPeriodDays = model.LoanPeriodDays,
            FineRatePerDay = model.FineRatePerDay,
            MaxBooksPerMember = model.MaxBooksPerMember,
            LostBookPenaltyFee = model.LostBookPenaltyFee,
            GracePeriodDays = model.GracePeriodDays,
            TimeZoneId = model.TimeZoneId
        }, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }

        TempData["Success"] = "Library rules and operational settings have been updated.";
        return RedirectToAction(nameof(Index));
    }
}

