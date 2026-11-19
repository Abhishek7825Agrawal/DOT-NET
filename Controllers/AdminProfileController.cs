using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin,Staff")]
public sealed class AdminProfileController(
    UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        return View(new AdminProfileViewModel
        {
            Email = user.Email ?? string.Empty,
            DisplayName = user.UserName ?? "Administrator",
            PhoneNumber = user.PhoneNumber
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(AdminProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        user.PhoneNumber = model.PhoneNumber?.Trim();
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        TempData["Success"] = "Admin profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }
}

